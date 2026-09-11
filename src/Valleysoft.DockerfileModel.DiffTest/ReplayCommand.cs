namespace Valleysoft.DockerfileModel.DiffTest;

internal static class ReplayCommand
{
    public static string Create(
        DiffCase testCase,
        string leanCliPath,
        string? projectPath = null,
        IEnumerable<string>? projectSearchPaths = null)
    {
        string resolvedLeanCliPath = Path.GetFullPath(leanCliPath);
        string? resolvedProjectPath = projectPath is null
            ? ResolveProjectPath(projectSearchPaths)
            : Path.GetFullPath(projectPath);
        string invocation = resolvedProjectPath is null
            ? $"dotnet {QuoteArgument(typeof(ReplayCommand).Assembly.Location)}"
            : $"dotnet run --project {QuoteArgument(resolvedProjectPath)} --";

        return $"{invocation} --replay " +
            $"--lean-cli {QuoteArgument(resolvedLeanCliPath)} " +
            $"--instruction {QuoteArgument(testCase.InstructionType)} " +
            $"--escape-code {(int)testCase.EscapeChar} " +
            $"--input-base64 {QuoteArgument(testCase.InputBase64)}";
    }

    internal static string? ResolveProjectPath(
        IEnumerable<string>? startingPaths = null)
    {
        string relativeProjectPath = Path.Combine(
            "src",
            "Valleysoft.DockerfileModel.DiffTest",
            "Valleysoft.DockerfileModel.DiffTest.csproj");
        string projectFileName = "Valleysoft.DockerfileModel.DiffTest.csproj";

        startingPaths ??=
            new[] { Environment.CurrentDirectory, AppContext.BaseDirectory };
        foreach (string startingPath in startingPaths)
        {
            DirectoryInfo? directory = new(startingPath);
            while (directory is not null)
            {
                string directCandidate = Path.Combine(
                    directory.FullName,
                    projectFileName);
                if (File.Exists(directCandidate))
                {
                    return directCandidate;
                }

                string repositoryCandidate = Path.Combine(
                    directory.FullName,
                    relativeProjectPath);
                if (File.Exists(repositoryCandidate))
                {
                    return repositoryCandidate;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static string QuoteArgument(string value) =>
        QuoteArgument(value, OperatingSystem.IsWindows());

    internal static string QuoteArgument(string value, bool isWindows) =>
        isWindows
            ? "'" + value.Replace("'", "''") + "'"
            : "'" + value.Replace("'", "'\"'\"'") + "'";
}
