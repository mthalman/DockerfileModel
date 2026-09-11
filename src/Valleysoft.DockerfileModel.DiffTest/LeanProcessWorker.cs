using System.Diagnostics;
using System.Text;

namespace Valleysoft.DockerfileModel.DiffTest;

public interface ILeanParser : IAsyncDisposable
{
    Task<string> ParseAsync(string input, char escapeChar, CancellationToken cancellationToken);
}

public sealed class LeanParseException : Exception
{
    public LeanParseException(string message)
        : base(message)
    {
    }
}

public sealed class LeanInfrastructureException : Exception
{
    public LeanInfrastructureException(string message)
        : base(message)
    {
    }

    public LeanInfrastructureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LeanProcessWorker : ILeanParser
{
    private readonly string _leanCliPath;
    private readonly string? _leanLibDir;
    private readonly TimeSpan _timeout;
    private Process? _process;
    private Task<string>? _stderrTask;
    private long _nextRequestId;

    public LeanProcessWorker(string leanCliPath, TimeSpan? timeout = null)
    {
        _leanCliPath = Path.GetFullPath(leanCliPath);
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _leanLibDir = FindLeanLibDir();
    }

    public async Task<string> ParseAsync(
        string input,
        char escapeChar,
        CancellationToken cancellationToken)
    {
        EnsureStarted();
        long requestId = Interlocked.Increment(ref _nextRequestId);
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
        string request = $"{requestId}\t{(int)escapeChar}\t{payload}";

        try
        {
            await _process!.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);

            string? response = await _process.StandardOutput
                .ReadLineAsync(cancellationToken)
                .AsTask()
                .WaitAsync(_timeout, cancellationToken);

            if (response is null)
            {
                throw await CreateExitedExceptionAsync("Lean batch process closed stdout.");
            }

            string[] fields = response.Split('\t');
            if (fields.Length != 3 ||
                !long.TryParse(fields[0], out long responseId) ||
                responseId != requestId)
            {
                throw new LeanInfrastructureException(
                    $"Malformed Lean response frame for request {requestId}: '{response}'.");
            }

            string body;
            try
            {
                body = Encoding.UTF8.GetString(Convert.FromBase64String(fields[2]));
            }
            catch (FormatException ex)
            {
                throw new LeanInfrastructureException(
                    $"Lean response {requestId} contained invalid base64.", ex);
            }

            return fields[1] switch
            {
                "ok" => body,
                "parse-error" => throw new LeanParseException(body),
                "error" => throw new LeanInfrastructureException(body),
                _ => throw new LeanInfrastructureException(
                    $"Lean response {requestId} had unknown status '{fields[1]}'.")
            };
        }
        catch (LeanParseException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopAsync();
            throw;
        }
        catch (Exception ex) when (ex is not LeanInfrastructureException)
        {
            await StopAsync();
            throw CreateRequestFailure(requestId, ex);
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    internal static LeanInfrastructureException CreateRequestFailure(
        long requestId,
        Exception exception) =>
        new(
            $"Lean batch request {requestId} failed: " +
            $"{exception.GetType().Name}: {exception.Message}",
            exception);

    private void EnsureStarted()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        _process?.Dispose();
        Process process = new()
        {
            StartInfo = CreateStartInfo()
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            process.Dispose();
            throw new LeanInfrastructureException(
                $"Failed to start Lean CLI '{_leanCliPath}'.", ex);
        }

        _process = process;
        _stderrTask = process.StandardError.ReadToEndAsync();
    }

    private ProcessStartInfo CreateStartInfo()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _leanCliPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--batch");

        if (_leanLibDir is not null)
        {
            string currentPath = startInfo.EnvironmentVariables["PATH"] ?? "";
            startInfo.EnvironmentVariables["PATH"] =
                _leanLibDir + Path.PathSeparator + currentPath;

            if (!OperatingSystem.IsWindows())
            {
                string currentLdPath =
                    Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
                startInfo.EnvironmentVariables["LD_LIBRARY_PATH"] =
                    _leanLibDir + Path.PathSeparator + currentLdPath;
            }
        }

        return startInfo;
    }

    private async Task<LeanInfrastructureException> CreateExitedExceptionAsync(string message)
    {
        string stderr = _stderrTask is null ? "" : (await _stderrTask).Trim();
        int? exitCode = _process is { HasExited: true } ? _process.ExitCode : null;
        return CreateExitedException(message, exitCode, stderr);
    }

    internal static LeanInfrastructureException CreateExitedException(
        string message,
        int? exitCode,
        string stderr)
    {
        string exitDetail = exitCode is null ? "" : $" Exit code: {exitCode}.";
        string stderrDetail = string.IsNullOrWhiteSpace(stderr) ? "" : $" {stderr.Trim()}";
        return new LeanInfrastructureException(
            $"{message}{exitDetail}{stderrDetail}");
    }

    private async Task StopAsync()
    {
        Process? process = _process;
        Task<string>? stderrTask = _stderrTask;
        _process = null;
        _stderrTask = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(1000))
                {
                    process.Kill(entireProcessTree: true);
                }
            }

            if (stderrTask is not null)
            {
                await stderrTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }
        catch (Exception) when (process.HasExited)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string? FindLeanLibDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string elanDirectory = Path.Combine(home, ".elan", "toolchains");
        if (!Directory.Exists(elanDirectory))
        {
            return null;
        }

        string sharedLibrary = OperatingSystem.IsWindows()
            ? "libInit_shared.dll"
            : "libInit_shared.so";

        foreach (string toolchain in Directory.GetDirectories(elanDirectory))
        {
            string binDirectory = Path.Combine(toolchain, "bin");
            if (File.Exists(Path.Combine(binDirectory, sharedLibrary)))
            {
                return binDirectory;
            }

            string libraryDirectory = Path.Combine(toolchain, "lib", "lean");
            if (File.Exists(Path.Combine(libraryDirectory, sharedLibrary)))
            {
                return libraryDirectory;
            }
        }

        return null;
    }
}
