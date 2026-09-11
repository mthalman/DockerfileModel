using System.Text;
using Valleysoft.DockerfileModel.DiffTest;

string mode = "";
string leanCliPath = "";
string corpusPath = RegressionCorpus.ResolveDefaultDirectory();
string? replayInstruction = null;
string? replayInputBase64 = null;
int count = 1000;
int seed = 42;
int workers = Math.Min(Environment.ProcessorCount, 4);
int escapeCode = '\\';
bool promoteFailures = false;

for (int index = 0; index < args.Length; index++)
{
    string argument = args[index];
    switch (argument)
    {
        case "--parse":
            mode = "parse";
            break;
        case "--compare":
            mode = "compare";
            break;
        case "--generate":
            mode = "generate";
            break;
        case "--replay":
            mode = "replay";
            break;
        case "--lean-cli":
            leanCliPath = ReadValue(argument);
            break;
        case "--corpus":
            corpusPath = ReadValue(argument);
            break;
        case "--count":
            count = ReadInt(argument, minimum: 0);
            break;
        case "--seed":
            seed = ReadInt(argument);
            break;
        case "--workers":
            workers = ReadInt(argument, minimum: 1);
            break;
        case "--escape":
            string escape = ReadValue(argument);
            if (escape.Length != 1)
            {
                throw new ArgumentException("--escape requires exactly one character.");
            }

            escapeCode = escape[0];
            break;
        case "--escape-code":
            escapeCode = ReadInt(argument, minimum: char.MinValue, maximum: char.MaxValue);
            break;
        case "--instruction":
            replayInstruction = ReadValue(argument);
            break;
        case "--input-base64":
            replayInputBase64 = ReadValue(argument);
            break;
        case "--promote-failures":
            promoteFailures = true;
            break;
        default:
            throw new ArgumentException($"Unknown argument '{argument}'.");
    }

    string ReadValue(string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[++index];
    }

    int ReadInt(
        string option,
        int minimum = int.MinValue,
        int maximum = int.MaxValue)
    {
        string value = ReadValue(option);
        if (!int.TryParse(value, out int parsed) || parsed < minimum || parsed > maximum)
        {
            throw new ArgumentException(
                $"{option} requires an integer from {minimum} through {maximum}.");
        }

        return parsed;
    }
}

return mode switch
{
    "parse" => await RunParseAsync(),
    "compare" => await RunCompareAsync(),
    "generate" => RunGenerate(),
    "replay" => await RunReplayAsync(),
    _ => ShowUsage()
};

async Task<int> RunParseAsync()
{
    string input = await Console.In.ReadToEndAsync();
    string instruction = DetectInstruction(input);

    try
    {
        Console.WriteLine(DiffTestRunner.ParseCSharp(
            instruction,
            input,
            (char)escapeCode));
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Parse error: {ex.Message}");
        return 1;
    }
}

async Task<int> RunCompareAsync()
{
    RequireLeanCli();
    RegressionCorpus corpus = new(corpusPath);
    IReadOnlyList<DiffCase> regressionCases = corpus.Load();
    List<DiffCase> generatedCases = InputGenerator.Generate(count, seed);
    DiffTestRunner runner = new(leanCliPath);

    Console.WriteLine(
        $"Running {regressionCases.Count} regression cases with {workers} persistent Lean worker(s)...");
    IReadOnlyList<DiffResult> regressionResults =
        await runner.RunBatchAsync(regressionCases, workers);
    PrintProgress(regressionResults.Count, regressionResults.Count);

    Console.WriteLine(
        $"Running {generatedCases.Count} generated cases (seed={seed})...");
    IReadOnlyList<DiffResult> generatedResults =
        await runner.RunBatchAsync(generatedCases, workers);
    PrintProgress(generatedResults.Count, generatedResults.Count);

    DiffResult[] failures = regressionResults
        .Concat(generatedResults)
        .Where(result => !result.Match)
        .ToArray();
    int infrastructureErrors =
        failures.Count(result => result.Outcome == DiffOutcomeKind.InfrastructureError);

    foreach (DiffResult failure in failures)
    {
        DiffResult minimized = failure;
        if (failure.Outcome != DiffOutcomeKind.InfrastructureError)
        {
            FailureMinimizer minimizer = new(
                () => new LeanProcessWorker(leanCliPath));
            minimized = await minimizer.MinimizeAsync(failure);
        }

        PrintFailure(failure, minimized);
        if (promoteFailures && minimized.Outcome != DiffOutcomeKind.InfrastructureError)
        {
            string promotedPath = corpus.Promote(minimized.Case);
            Console.Error.WriteLine($"  Promoted: {promotedPath}");
        }

        Console.Error.WriteLine();
    }

    Console.WriteLine(
        $"Results: {regressionCases.Count} regression, {generatedCases.Count} generated, " +
        $"{failures.Length - infrastructureErrors} parser differences, " +
        $"{infrastructureErrors} infrastructure errors");
    Console.WriteLine(failures.Length == 0 ? "PASS" : "FAIL");
    return failures.Length == 0 ? 0 : 1;
}

async Task<int> RunReplayAsync()
{
    RequireLeanCli();
    if (string.IsNullOrWhiteSpace(replayInstruction) ||
        string.IsNullOrWhiteSpace(replayInputBase64))
    {
        throw new ArgumentException(
            "--replay requires --instruction and --input-base64.");
    }

    string input;
    try
    {
        input = Encoding.UTF8.GetString(Convert.FromBase64String(replayInputBase64));
    }
    catch (FormatException ex)
    {
        throw new ArgumentException("--input-base64 is not valid base64.", ex);
    }

    DiffCase replayCase = new(
        "replay",
        DiffCaseSource.Replay,
        replayInstruction,
        input,
        (char)escapeCode);
    DiffResult result = await new DiffTestRunner(leanCliPath).RunSingleAsync(replayCase);
    PrintFailure(result, result);
    return result.Match ? 0 : 1;
}

int RunGenerate()
{
    foreach (DiffCase testCase in InputGenerator.Generate(count, seed))
    {
        Console.WriteLine(
            $"{testCase.InstructionType}\t{testCase.InputBase64}\t" +
            $"{(int)testCase.EscapeChar}\t{testCase.Generator}\t{testCase.CaseIndex}");
    }

    return 0;
}

void PrintFailure(DiffResult original, DiffResult minimized)
{
    DiffCase testCase = original.Case;
    Console.Error.WriteLine(
        $"{original.Outcome.ToString().ToUpperInvariant()} " +
        $"({testCase.Source}, {testCase.InstructionType}, id={testCase.Id})");
    if (testCase.Generator is not null)
    {
        Console.Error.WriteLine(
            $"  Seed: {testCase.Seed}, generator: {testCase.Generator}, " +
            $"case index: {testCase.CaseIndex}, escape: {(int)testCase.EscapeChar}");
    }

    Console.Error.WriteLine($"  Input:     {Escape(testCase.Input)}");
    if (minimized.Case.Input != testCase.Input)
    {
        Console.Error.WriteLine($"  Minimized: {Escape(minimized.Case.Input)}");
    }

    if (original.Outcome == DiffOutcomeKind.JsonMismatch)
    {
        Console.Error.WriteLine($"  C#:   {minimized.CSharpJson}");
        Console.Error.WriteLine($"  Lean: {minimized.LeanJson}");
    }
    else if (minimized.Error is not null || original.Error is not null)
    {
        Console.Error.WriteLine($"  Error: {minimized.Error ?? original.Error}");
        if (original.Outcome == DiffOutcomeKind.CSharpParseError)
        {
            Console.Error.WriteLine($"  Lean: {minimized.LeanJson}");
        }
        else if (original.Outcome == DiffOutcomeKind.LeanParseError)
        {
            Console.Error.WriteLine($"  C#:   {minimized.CSharpJson}");
        }
    }

    Console.Error.WriteLine(
        $"  Replay: {ReplayCommand.Create(minimized.Case, leanCliPath)}");
}

void RequireLeanCli()
{
    if (string.IsNullOrWhiteSpace(leanCliPath))
    {
        throw new ArgumentException("--lean-cli <path> is required.");
    }
}

static string DetectInstruction(string input)
{
    string trimmed = input.TrimStart();
    int end = trimmed.IndexOfAny(new[] { ' ', '\t', '\n', '\r' });
    return (end > 0 ? trimmed[..end] : trimmed).ToUpperInvariant();
}

static void PrintProgress(int current, int total) =>
    Console.WriteLine($"  Progress: {current}/{total}");

static string Escape(string value) =>
    value.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

static int ShowUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  --parse [--escape <char>]");
    Console.Error.WriteLine(
        "  --compare --lean-cli <path> [--count N] [--seed N] [--workers N] " +
        "[--corpus <path>] [--promote-failures]");
    Console.Error.WriteLine(
        "  --replay --lean-cli <path> --instruction <type> --input-base64 <value> " +
        "[--escape-code N]");
    Console.Error.WriteLine("  --generate [--count N] [--seed N]");
    return 1;
}
