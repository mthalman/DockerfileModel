using System.Diagnostics;
using System.Text;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.DiffTest;

/// <summary>
/// Result of comparing C# and Lean parser outputs for a single input.
/// </summary>
public record DiffResult(
    string InstructionType,
    string Input,
    string CSharpJson,
    string LeanJson,
    bool Match,
    string? Error = null);

/// <summary>
/// Core comparison engine: parses each input with both C# and Lean parsers,
/// serializes to canonical JSON, and compares for byte-identical output.
/// </summary>
public class DiffTestRunner
{
    private readonly string _leanCliPath;
    private readonly string? _leanLibDir;
    private readonly TimeSpan _timeout;

    public DiffTestRunner(string leanCliPath, TimeSpan? timeout = null)
    {
        _leanCliPath = Path.GetFullPath(leanCliPath);
        _timeout = timeout ?? TimeSpan.FromSeconds(30);

        // Determine the Lean shared library directory.
        // The binary is at <lake-build>/bin/DockerfileModelDiffTest(.exe).
        // Lean shared libraries (libInit_shared.so/.dll) are in the elan toolchain bin dir.
        // We find the toolchain by looking for elan-managed lean in PATH or common locations.
        _leanLibDir = FindLeanLibDir();
    }

    /// <summary>
    /// Parse an instruction with the C# parser and serialize to canonical JSON.
    /// </summary>
    public static string ParseCSharp(string instructionType, string input)
    {
        Token token = instructionType.ToUpperInvariant() switch
        {
            "FROM" => FromInstruction.Parse(input),
            "ARG" => ArgInstruction.Parse(input),
            _ => throw new ArgumentException($"Unsupported instruction type: {instructionType}")
        };
        return TokenJsonSerializer.Serialize(token);
    }

    /// <summary>
    /// Parse an instruction with the Lean CLI and capture JSON from stdout.
    /// </summary>
    public async Task<string> ParseLeanAsync(string input)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _leanCliPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Ensure Lean shared libraries are findable
        if (_leanLibDir != null)
        {
            string currentPath = process.StartInfo.EnvironmentVariables["PATH"] ?? "";
            process.StartInfo.EnvironmentVariables["PATH"] = _leanLibDir + Path.PathSeparator + currentPath;

            // On Linux, also set LD_LIBRARY_PATH
            if (!OperatingSystem.IsWindows())
            {
                string currentLdPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
                process.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] =
                    _leanLibDir + Path.PathSeparator + currentLdPath;
            }
        }

        process.Start();

        // Write input to stdin and close
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();

        // Read stdout and stderr
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        bool exited = process.WaitForExit((int)_timeout.TotalMilliseconds);
        if (!exited)
        {
            process.Kill();
            throw new TimeoutException($"Lean CLI timed out after {_timeout.TotalSeconds}s");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Lean CLI exited with code {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout.TrimEnd('\n', '\r');
    }

    /// <summary>
    /// Run both parsers on a single input and compare JSON output.
    /// </summary>
    public async Task<DiffResult> RunSingleAsync(string instructionType, string input)
    {
        string csharpJson;
        try
        {
            csharpJson = ParseCSharp(instructionType, input);
        }
        catch (Exception ex)
        {
            return new DiffResult(instructionType, input, "", "", false,
                $"C# parse error: {ex.Message}");
        }

        string leanJson;
        try
        {
            leanJson = await ParseLeanAsync(input);
        }
        catch (Exception ex)
        {
            return new DiffResult(instructionType, input, csharpJson, "", false,
                $"Lean parse error: {ex.Message}");
        }

        bool match = string.Equals(csharpJson, leanJson, StringComparison.Ordinal);
        return new DiffResult(instructionType, input, csharpJson, leanJson, match);
    }

    /// <summary>
    /// Run both parsers on a batch of inputs, reporting progress.
    /// </summary>
    public async Task<List<DiffResult>> RunBatchAsync(
        List<(string InstructionType, string Text)> inputs,
        Action<int, int, DiffResult>? onProgress = null)
    {
        List<DiffResult> results = new();
        int total = inputs.Count;

        for (int i = 0; i < total; i++)
        {
            (string type, string text) = inputs[i];
            DiffResult result = await RunSingleAsync(type, text);
            results.Add(result);
            onProgress?.Invoke(i + 1, total, result);
        }

        return results;
    }

    /// <summary>
    /// Find the directory containing Lean shared libraries (libInit_shared.so/.dll).
    /// Searches common elan toolchain locations and the lean CLI's own directory tree.
    /// </summary>
    private static string? FindLeanLibDir()
    {
        // Check elan's default location
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string elandir = Path.Combine(home, ".elan", "toolchains");

        if (Directory.Exists(elandir))
        {
            // Find the active toolchain's bin directory
            foreach (string toolchain in Directory.GetDirectories(elandir))
            {
                string binDir = Path.Combine(toolchain, "bin");
                if (Directory.Exists(binDir))
                {
                    string libSharedPattern = OperatingSystem.IsWindows()
                        ? "libInit_shared.dll"
                        : "libInit_shared.so";

                    if (File.Exists(Path.Combine(binDir, libSharedPattern)))
                    {
                        return binDir;
                    }

                    // On some Linux setups, shared libs are in lib/lean/
                    string libDir = Path.Combine(toolchain, "lib", "lean");
                    if (File.Exists(Path.Combine(libDir, libSharedPattern)))
                    {
                        return libDir;
                    }
                }
            }
        }

        return null;
    }
}
