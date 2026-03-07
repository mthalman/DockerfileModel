using Valleysoft.DockerfileModel.DiffTest;

// Parse command-line arguments
string mode = "";
string leanCliPath = "";
int count = 1000;
int seed = 42;
char escapeChar = '\\';

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
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
        case "--lean-cli":
            if (i + 1 < args.Length) leanCliPath = args[++i];
            break;
        case "--count":
            if (i + 1 < args.Length) count = int.Parse(args[++i]);
            break;
        case "--seed":
            if (i + 1 < args.Length) seed = int.Parse(args[++i]);
            break;
        case "--escape":
            if (i + 1 < args.Length && args[i + 1].Length == 1) escapeChar = args[++i][0];
            break;
    }
}

switch (mode)
{
    case "parse":
        return await RunParse();
    case "compare":
        return await RunCompare();
    case "generate":
        return RunGenerate();
    default:
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  --parse                          Read stdin, output JSON (symmetric with Lean CLI)");
        Console.Error.WriteLine("  --compare --lean-cli <path> --count N  Compare both parsers on N random inputs");
        Console.Error.WriteLine("  --generate --count N             Output test inputs as TYPE\\tBASE64 lines");
        Console.Error.WriteLine("  --escape <char>                  Set escape character (default: \\)");
        return 1;
}

/// <summary>
/// Parse mode: read stdin, detect instruction type, output canonical JSON.
/// Symmetric with the Lean CLI for manual smoke testing.
/// </summary>
async Task<int> RunParse()
{
    string input = await Console.In.ReadToEndAsync();
    string trimmed = input.TrimStart();
    int spaceIdx = trimmed.IndexOfAny(new[] { ' ', '\t', '\n', '\r' });
    string keyword = spaceIdx > 0 ? trimmed[..spaceIdx].ToUpperInvariant() : trimmed.ToUpperInvariant();

    try
    {
        string json = DiffTestRunner.ParseCSharp(keyword, input, escapeChar);
        Console.WriteLine(json);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Parse error: {ex.Message}");
        return 1;
    }
}

/// <summary>
/// Compare mode: generate random inputs, run both parsers, report mismatches.
/// </summary>
async Task<int> RunCompare()
{
    if (string.IsNullOrEmpty(leanCliPath))
    {
        Console.Error.WriteLine("Error: --lean-cli <path> is required for --compare mode");
        return 1;
    }

    Console.WriteLine($"Generating {count} random inputs (seed={seed}, escape='{escapeChar}')...");
    List<(string InstructionType, string Text, char EscapeChar)> inputs = InputGenerator.Generate(count, seed);

    Console.WriteLine($"Running differential test against Lean CLI: {leanCliPath}");
    Console.WriteLine();

    DiffTestRunner runner = new(leanCliPath);
    int mismatches = 0;
    int errors = 0;

    List<DiffResult> results = await runner.RunBatchAsync(inputs, (current, total, result) =>
    {
        if (!result.Match)
        {
            if (result.Error != null)
            {
                errors++;
                Console.Error.WriteLine($"[{current}/{total}] ERROR: {result.Error}");
                Console.Error.WriteLine($"  Input: {Escape(result.Input)}");
            }
            else
            {
                mismatches++;
                Console.Error.WriteLine($"[{current}/{total}] MISMATCH ({result.InstructionType}):");
                Console.Error.WriteLine($"  Input:  {Escape(result.Input)}");
                Console.Error.WriteLine($"  C#:     {result.CSharpJson}");
                Console.Error.WriteLine($"  Lean:   {result.LeanJson}");
            }
            Console.Error.WriteLine();
        }

        // Progress indicator every 1000 inputs
        if (current % 1000 == 0 || current == total)
        {
            Console.Write($"\r  Progress: {current}/{total}");
            if (current == total) Console.WriteLine();
        }
    });

    Console.WriteLine();
    Console.WriteLine($"Results: {count} inputs, {mismatches} mismatches, {errors} errors");

    if (mismatches > 0 || errors > 0)
    {
        Console.WriteLine("FAIL");
        return 1;
    }

    Console.WriteLine("PASS");
    return 0;
}

/// <summary>
/// Generate mode: output test inputs as TYPE\tBASE64 lines for external use.
/// </summary>
int RunGenerate()
{
    List<(string InstructionType, string Text, char EscapeChar)> inputs = InputGenerator.Generate(count, seed);
    foreach ((string type, string text, char esc) in inputs)
    {
        string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
        Console.WriteLine($"{type}\t{b64}\t{esc}");
    }
    return 0;
}

/// <summary>
/// Escape control characters for display.
/// </summary>
static string Escape(string s) =>
    s.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
