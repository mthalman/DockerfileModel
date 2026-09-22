using Valleysoft.DockerfileModel.TestSupport;
using Valleysoft.DockerfileModel.Parsing;

namespace Valleysoft.DockerfileModel.DiffTest;

public sealed class DiffTestRunner
{
    private readonly Func<ILeanParser> _parserFactory;

    public DiffTestRunner(string leanCliPath, TimeSpan? timeout = null)
        : this(() => new LeanProcessWorker(leanCliPath, timeout))
    {
    }

    public DiffTestRunner(Func<ILeanParser> parserFactory)
    {
        _parserFactory = parserFactory;
    }

    public static string ParseCSharp(
        string instructionType,
        string input,
        char escapeChar = '\\') =>
        InstructionSerializer.ParseCSharp(instructionType, input, escapeChar);

    public async Task<DiffResult> RunSingleAsync(
        DiffCase testCase,
        CancellationToken cancellationToken = default)
    {
        await using ILeanParser parser = _parserFactory();
        return await RunSingleAsync(parser, testCase, cancellationToken);
    }

    public async Task<IReadOnlyList<DiffResult>> RunBatchAsync(
        IReadOnlyList<DiffCase> inputs,
        int workerCount,
        CancellationToken cancellationToken = default)
    {
        if (workerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount));
        }

        if (inputs.Count == 0)
        {
            return Array.Empty<DiffResult>();
        }

        int actualWorkerCount = Math.Min(workerCount, inputs.Count);
        DiffResult?[] results = new DiffResult[inputs.Count];
        int nextIndex = -1;

        Task[] workers = Enumerable.Range(0, actualWorkerCount)
            .Select(_ => RunWorkerAsync())
            .ToArray();
        await Task.WhenAll(workers);

        return results.Select(result => result!).ToArray();

        async Task RunWorkerAsync()
        {
            await using ILeanParser parser = _parserFactory();
            while (true)
            {
                int index = Interlocked.Increment(ref nextIndex);
                if (index >= inputs.Count)
                {
                    return;
                }

                results[index] = await RunSingleAsync(
                    parser,
                    inputs[index],
                    cancellationToken);
            }
        }
    }

    internal static async Task<DiffResult> RunSingleAsync(
        ILeanParser parser,
        DiffCase testCase,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (testCase.Source == DiffCaseSource.Upstream || testCase.Upstream is not null)
        {
            if (testCase.Source != DiffCaseSource.Upstream || testCase.Upstream is null)
            {
                throw new InvalidDataException("Upstream cases require provenance and an expectation.");
            }

            return await RunUpstreamAsync(parser, testCase, cancellationToken);
        }

        string csharpJson;
        try
        {
            csharpJson = ParseCSharp(
                testCase.InstructionType,
                testCase.Input,
                testCase.EscapeChar);
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsKnownCrash(testCase.InstructionType, testCase.Input))
            {
                try
                {
                    await parser.ParseAsync(
                        testCase.Input,
                        testCase.EscapeChar,
                        cancellationToken);
                    return new DiffResult(testCase, "", "", DiffOutcomeKind.Match);
                }
                catch (LeanParseException)
                {
                    return new DiffResult(testCase, "", "", DiffOutcomeKind.Match);
                }
                catch (Exception leanException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new DiffResult(
                        testCase,
                        "",
                        "",
                        DiffOutcomeKind.InfrastructureError,
                        leanException.Message);
                }
            }

            if (ex is not ParseException)
            {
                return new DiffResult(
                    testCase,
                    "",
                    "",
                    DiffOutcomeKind.CSharpParserCrash,
                    $"{ex.GetType().Name}: {ex.Message}");
            }

            string leanResultJson;
            try
            {
                leanResultJson = await parser.ParseAsync(
                    testCase.Input,
                    testCase.EscapeChar,
                    cancellationToken);
            }
            catch (LeanParseException)
            {
                return new DiffResult(testCase, "", "", DiffOutcomeKind.Match);
            }
            catch (Exception leanException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                return new DiffResult(
                    testCase,
                    "",
                    "",
                    DiffOutcomeKind.InfrastructureError,
                    leanException.Message);
            }

            return new DiffResult(
                testCase,
                "",
                leanResultJson,
                DiffOutcomeKind.CSharpParseError,
                ex.Message);
        }

        string leanJson;
        try
        {
            leanJson = await parser.ParseAsync(
                testCase.Input,
                testCase.EscapeChar,
                cancellationToken);
        }
        catch (LeanParseException ex)
        {
            return new DiffResult(
                testCase,
                csharpJson,
                "",
                DiffOutcomeKind.LeanParseError,
                ex.Message);
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return new DiffResult(
                testCase,
                csharpJson,
                "",
                DiffOutcomeKind.InfrastructureError,
                ex.Message);
        }

        DiffOutcomeKind outcome = string.Equals(
            csharpJson,
            leanJson,
            StringComparison.Ordinal)
            ? DiffOutcomeKind.Match
            : DiffOutcomeKind.JsonMismatch;

        return new DiffResult(testCase, csharpJson, leanJson, outcome);
    }

    private static async Task<DiffResult> RunUpstreamAsync(
        ILeanParser parser, DiffCase testCase, CancellationToken cancellationToken)
    {
        string csharpJson = "";
        string leanJson = "";
        string csharpStatus = "accepted";
        string leanStatus = "accepted";
        string? crashType = null;
        List<string> errors = new();

        try
        {
            csharpJson = ParseCSharp(testCase.InstructionType, testCase.Input, testCase.EscapeChar);
        }
        catch (ParseException ex)
        {
            csharpStatus = "rejected";
            errors.Add($"C#: {ex.Message}");
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            csharpStatus = "crashed";
            crashType = ex.GetType().FullName;
            errors.Add($"C#: {crashType}: {ex.Message}");
        }

        try
        {
            leanJson = await parser.ParseAsync(
                testCase.Input, testCase.EscapeChar, cancellationToken);
        }
        catch (LeanParseException ex)
        {
            leanStatus = "rejected";
            errors.Add($"Lean: {ex.Message}");
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new DiffResult(testCase, csharpJson, "", DiffOutcomeKind.InfrastructureError,
                ex.Message, csharpStatus, "infrastructure-error", crashType);
        }

        string expectedStatus = testCase.Upstream!.Accept ? "accepted" : "rejected";
        DiffOutcomeKind outcome = csharpStatus == "crashed"
            ? DiffOutcomeKind.CSharpParserCrash
            : csharpStatus != expectedStatus || leanStatus != expectedStatus
                ? DiffOutcomeKind.UpstreamExpectationMismatch
                : csharpJson != leanJson
                    ? DiffOutcomeKind.JsonMismatch
                    : DiffOutcomeKind.Match;
        if (outcome == DiffOutcomeKind.UpstreamExpectationMismatch)
        {
            errors.Insert(0,
                $"BuildKit expects {expectedStatus}; C# {csharpStatus}, Lean {leanStatus}.");
        }

        return new DiffResult(testCase, csharpJson, leanJson, outcome,
            errors.Count == 0 ? null : string.Join(Environment.NewLine, errors),
            csharpStatus, leanStatus, crashType);
    }

    private static bool IsKnownCrash(string instructionType, string input)
    {
        if (!string.Equals(instructionType, "VOLUME", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string trimmed = input.TrimStart();
        int spaceIndex = trimmed.IndexOfAny(new[] { ' ', '\t' });
        string arguments = spaceIndex >= 0 ? trimmed[spaceIndex..].TrimStart() : "";
        string withoutWhitespace = string.Concat(
            arguments.Where(character => !char.IsWhiteSpace(character)));
        return string.Equals(withoutWhitespace, "[]", StringComparison.Ordinal);
    }
}
