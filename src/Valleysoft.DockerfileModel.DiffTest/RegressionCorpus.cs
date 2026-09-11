using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Valleysoft.DockerfileModel.DiffTest;

public sealed class RegressionCorpus
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public RegressionCorpus(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
    }

    public string DirectoryPath { get; }

    public IReadOnlyList<DiffCase> Load()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Regression corpus directory not found: {DirectoryPath}");
        }

        List<DiffCase> cases = new();
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(DirectoryPath, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            RegressionFixture fixture;
            try
            {
                fixture = JsonSerializer.Deserialize<RegressionFixture>(
                    File.ReadAllText(path),
                    JsonOptions)
                    ?? throw new InvalidDataException("Fixture is empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Invalid regression fixture '{path}': {ex.Message}", ex);
            }

            DiffCase testCase;
            try
            {
                testCase = fixture.ToCase();
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException(
                    $"Invalid regression fixture '{path}': {ex.Message}", ex);
            }

            if (!ids.Add(testCase.Id))
            {
                throw new InvalidDataException(
                    $"Duplicate regression fixture ID '{testCase.Id}' in '{path}'.");
            }

            cases.Add(testCase);
        }

        return cases;
    }

    public string Promote(DiffCase minimizedCase)
    {
        if (!InstructionTypes.IsSupported(minimizedCase.InstructionType))
        {
            throw new InvalidDataException(
                $"Cannot promote unsupported instruction type '{minimizedCase.InstructionType}'.");
        }

        Directory.CreateDirectory(DirectoryPath);
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{minimizedCase.InstructionType}\0{minimizedCase.EscapeChar}\0{minimizedCase.Input}")))
            .ToLowerInvariant()[..12];
        string instruction = minimizedCase.InstructionType.ToLowerInvariant();
        string id = $"local-{instruction}-{hash}";
        string path = Path.GetFullPath(
            Path.Combine(DirectoryPath, $"{instruction}-{hash}.json"));
        string corpusPrefix = DirectoryPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(corpusPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Promoted fixture path escapes the regression corpus: {path}");
        }

        RegressionFixture fixture = RegressionFixture.FromCase(
            minimizedCase with
            {
                Id = id,
                Source = DiffCaseSource.Regression,
                Generator = null,
                Seed = null,
                CaseIndex = null
            });
        string json = JsonSerializer.Serialize(fixture, JsonOptions) + Environment.NewLine;
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return path;
    }

    public static string ResolveDefaultDirectory()
    {
        DirectoryInfo? current = new(Environment.CurrentDirectory);
        while (current is not null)
        {
            string sourceDirectory = Path.Combine(
                current.FullName,
                "src",
                "Valleysoft.DockerfileModel.DiffTest",
                "RegressionCorpus");
            if (Directory.Exists(sourceDirectory))
            {
                return sourceDirectory;
            }

            string projectDirectory = Path.Combine(
                current.FullName,
                "Valleysoft.DockerfileModel.DiffTest",
                "RegressionCorpus");
            if (Directory.Exists(projectDirectory))
            {
                return projectDirectory;
            }

            current = current.Parent;
        }

        string deployedDirectory = Path.Combine(AppContext.BaseDirectory, "RegressionCorpus");
        return Directory.Exists(deployedDirectory)
            ? deployedDirectory
            : Path.Combine(
                Environment.CurrentDirectory,
                "src",
                "Valleysoft.DockerfileModel.DiffTest",
                "RegressionCorpus");
    }
}
