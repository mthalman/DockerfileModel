using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Valleysoft.DockerfileModel.DiffTest;

public sealed class UpstreamCorpus
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly Dictionary<string, KnownUpstreamDeviation> _deviations = new(StringComparer.Ordinal);

    public UpstreamCorpus(string directory, string compatibilityPath)
    {
        DirectoryPath = Path.GetFullPath(directory);
        CompatibilityPath = Path.GetFullPath(compatibilityPath);
    }

    public string DirectoryPath { get; }

    public string CompatibilityPath { get; }

    public IReadOnlyList<DiffCase> Load()
    {
        _deviations.Clear();
        string generated = Path.Combine(DirectoryPath, "generated");
        CompatibilityTarget target = Read<CompatibilityTarget>(CompatibilityPath);
        UpstreamManifest manifest = Read<UpstreamManifest>(Path.Combine(generated, "manifest.json"));
        ValidateIdentity(target.SchemaVersion, target.Repository, target.FrontendVersion, target.SourceCommit);
        ValidateIdentity(manifest.SchemaVersion, manifest.Repository, manifest.FrontendVersion, manifest.SourceCommit);
        if (target.Repository != manifest.Repository ||
            target.FrontendVersion != manifest.FrontendVersion ||
            target.SourceCommit != manifest.SourceCommit)
        {
            throw new InvalidDataException(
                "Compatibility target and upstream corpus differ. Review the frontend update and regenerate the corpus.");
        }

        ValidateImporterFile("go.mod", manifest.ImporterGoModSha256);
        ValidateImporterFile("go.sum", manifest.ImporterGoSumSha256);

        if (manifest.Files is null || manifest.Files.Length == 0)
        {
            throw new InvalidDataException("Upstream manifest must enumerate its generated files.");
        }

        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (UpstreamFile file in manifest.Files)
        {
            if (file is null)
            {
                throw new InvalidDataException("Upstream manifest contains a null file entry.");
            }

            string path = ResolveFile(generated, file.Path);
            if (file.Path == "manifest.json" || !IsHash(file.Sha256, 64) || files.ContainsKey(file.Path))
            {
                throw new InvalidDataException($"Invalid or duplicate upstream manifest entry '{file.Path}'.");
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (Hash(bytes) != file.Sha256)
            {
                throw new InvalidDataException($"Upstream file hash mismatch: {file.Path}");
            }

            files.Add(file.Path, bytes);
        }

        string[] actualFiles = Directory.EnumerateFiles(generated, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(generated, path).Replace('\\', '/'))
            .Where(path => path != "manifest.json")
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (!actualFiles.SequenceEqual(files.Keys.OrderBy(path => path, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("Generated upstream files differ from the manifest inventory.");
        }

        List<DiffCase> cases = new();
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (string path in files.Keys
            .Where(path => path.StartsWith("cases/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            UpstreamFixture fixture = Read<UpstreamFixture>(files[path], path);
            DiffCase testCase = new RegressionFixture(
                fixture.Id, fixture.InstructionType, fixture.InputBase64, fixture.EscapeCharacter).ToCase();
            if (!ids.Add(testCase.Id) ||
                fixture.ExpectedParse is not ("accept" or "reject") ||
                fixture.EscapeCharacter is not ("\\" or "`") ||
                !IsHash(fixture.InputSha256, 64) ||
                Hash(Encoding.UTF8.GetBytes(testCase.Input)) != fixture.InputSha256)
            {
                throw new InvalidDataException($"Invalid upstream fixture '{path}'.");
            }

            ResolveFile(generated, fixture.SourcePath);
            if (string.IsNullOrWhiteSpace(fixture.SourcePath) ||
                !files.TryGetValue(
                    "sources/" + Hash(Encoding.UTF8.GetBytes(fixture.SourcePath))[..16] + ".source",
                    out byte[]? source) ||
                !GetSourceLines(source, fixture.StartLine, fixture.EndLine)
                    .SequenceEqual(Encoding.UTF8.GetBytes(testCase.Input)))
            {
                throw new InvalidDataException($"Upstream source span does not match fixture '{path}'.");
            }

            cases.Add(testCase with
            {
                Source = DiffCaseSource.Upstream,
                Upstream = new UpstreamExpectation(
                    fixture.ExpectedParse == "accept", target.Repository, target.FrontendVersion,
                    target.SourceCommit, fixture.SourcePath, fixture.StartLine, fixture.EndLine,
                    fixture.InputSha256, DirectoryPath, CompatibilityPath)
            });
        }

        if (cases.Count == 0)
        {
            throw new InvalidDataException("The upstream corpus must contain executable cases.");
        }

        Dictionary<string, DiffCase> byId = cases.ToDictionary(testCase => testCase.Id, StringComparer.Ordinal);
        foreach (KnownUpstreamDeviation deviation in
            Read<KnownUpstreamDeviation[]>(Path.Combine(DirectoryPath, "known-deviations.json")))
        {
            if (deviation is null || string.IsNullOrWhiteSpace(deviation.Id) ||
                !byId.TryGetValue(deviation.Id, out DiffCase? testCase) ||
                testCase.Upstream!.InputSha256 != deviation.InputSha256 ||
                !IsHash(deviation.OutcomeSignature, 64) ||
                string.IsNullOrWhiteSpace(deviation.Reason) ||
                deviation.Issue is null ||
                !Regex.IsMatch(deviation.Issue,
                    @"^https://github\.com/mthalman/DockerfileModel/issues/[1-9][0-9]*$",
                    RegexOptions.CultureInvariant) ||
                !_deviations.TryAdd(deviation.Id, deviation))
            {
                throw new InvalidDataException($"Invalid, duplicate or stale known upstream deviation '{deviation?.Id}'.");
            }
        }

        return cases;
    }

    public UpstreamEvaluation Evaluate(DiffResult result)
    {
        if (result.Case.Upstream is null)
        {
            throw new ArgumentException("Only upstream results can be evaluated.", nameof(result));
        }

        _deviations.TryGetValue(result.Case.Id, out KnownUpstreamDeviation? deviation);
        if (deviation is null)
        {
            return new UpstreamEvaluation(result, null, result.Match ? null : "Unreviewed upstream difference.");
        }

        string? failure = result.Outcome == DiffOutcomeKind.InfrastructureError
            ? "Infrastructure failures cannot be expected failures."
            : result.Match
                ? "Unexpected pass: remove the stale known deviation."
                : deviation.InputSha256 != result.Case.Upstream.InputSha256 ||
                  deviation.OutcomeSignature != OutcomeSignature(result)
                    ? "Known upstream deviation changed; review the new outcome."
                    : null;
        return new UpstreamEvaluation(result, deviation, failure);
    }

    public static string OutcomeSignature(DiffResult result) =>
        Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            outcome = result.Outcome.ToString(),
            result.CSharpStatus,
            result.LeanStatus,
            result.CrashType,
            result.CSharpJson,
            result.LeanJson
        }, JsonOptions));

    public static string ResolveDefaultDirectory() =>
        ResolvePath(Path.Combine("src", "Valleysoft.DockerfileModel.DiffTest", "UpstreamCorpus"),
            "UpstreamCorpus");

    public static string ResolveDefaultCompatibilityPath() =>
        ResolvePath("upstream-compatibility.json", "upstream-compatibility.json");

    private void ValidateImporterFile(string name, string expectedHash)
    {
        string sourcePath = Path.Combine(
            Path.GetDirectoryName(CompatibilityPath)!, "tools", "BuildKitCorpus", name);
        string path = File.Exists(sourcePath)
            ? sourcePath
            : Path.Combine(DirectoryPath, "importer", name);
        if (!IsHash(expectedHash, 64) || Hash(File.ReadAllBytes(path)) != expectedHash)
        {
            throw new InvalidDataException(
                $"Importer {name} differs from corpus provenance. Regenerate and review the corpus.");
        }
    }

    private static string ResolvePath(string sourcePath, string deployedPath)
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                string project = Path.Combine(directory.FullName, "src",
                    "Valleysoft.DockerfileModel.DiffTest", "Valleysoft.DockerfileModel.DiffTest.csproj");
                if (File.Exists(project))
                {
                    return Path.Combine(directory.FullName, sourcePath);
                }
            }
        }

        return Path.Combine(AppContext.BaseDirectory, deployedPath);
    }

    private static T Read<T>(string path) => Read<T>(File.ReadAllBytes(path), path);

    private static T Read<T>(byte[] bytes, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidDataException($"Empty upstream metadata: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid upstream metadata '{path}': {ex.Message}", ex);
        }
    }

    private static void ValidateIdentity(int schema, string repository, string version, string commit)
    {
        if (schema != 1 || repository != "moby/buildkit" ||
            version is null || !Regex.IsMatch(version, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") ||
            !IsHash(commit, 40))
        {
            throw new InvalidDataException("Invalid upstream compatibility identity.");
        }
    }

    private static string ResolveFile(string directory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains('\\') ||
            relativePath.Contains(':') || relativePath.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException($"Invalid upstream relative path '{relativePath}'.");
        }

        string path = directory;
        foreach (string part in relativePath.Split('/'))
        {
            path = Path.Combine(path, part);
            if ((File.Exists(path) || Directory.Exists(path)) &&
                File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException($"Upstream paths cannot contain links: {relativePath}");
            }
        }

        return path;
    }

    private static byte[] GetSourceLines(byte[] source, int startLine, int endLine)
    {
        if (startLine < 1 || endLine < startLine)
        {
            throw new InvalidDataException("Invalid upstream source line range.");
        }

        List<int> starts = new() { 0 };
        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] == (byte)'\n' && index + 1 < source.Length)
            {
                starts.Add(index + 1);
            }
        }

        if (endLine > starts.Count)
        {
            throw new InvalidDataException("Upstream source range exceeds source length.");
        }

        int end = endLine < starts.Count ? starts[endLine] : source.Length;
        return source[starts[startLine - 1]..end];
    }

    private static bool IsHash(string? value, int length) =>
        value is not null && value.Length == length &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

public sealed record KnownUpstreamDeviation(
    string Id, string InputSha256, string OutcomeSignature, string Issue, string Reason);

public sealed record UpstreamEvaluation(
    DiffResult Result, KnownUpstreamDeviation? Deviation, string? Failure)
{
    public bool Passed => Failure is null;
    public bool ExpectedFailure => Passed && Deviation is not null;
}

internal sealed record CompatibilityTarget(
    int SchemaVersion, string Repository, string FrontendVersion, string SourceCommit);

internal sealed record UpstreamManifest(
    int SchemaVersion, string Repository, string FrontendVersion, string SourceCommit,
    string ImporterGoModSha256, string ImporterGoSumSha256, UpstreamFile[] Files);

internal sealed record UpstreamFile(string Path, string Sha256);

internal sealed record UpstreamFixture(
    string Id, string InstructionType, string InputBase64, string EscapeCharacter,
    string ExpectedParse, string SourcePath, int StartLine, int EndLine, string InputSha256);
