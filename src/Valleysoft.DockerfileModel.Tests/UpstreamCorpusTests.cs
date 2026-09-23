using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Valleysoft.DockerfileModel.Parsing;
using Valleysoft.DockerfileModel.DiffTest;

namespace Valleysoft.DockerfileModel.Tests;

public class UpstreamCorpusTests
{
    [Theory]
    [InlineData("FROM ", true, false)]
    [InlineData("FROM alpine", false, false)]
    [InlineData("FROM ", false, true)]
    [InlineData("FROM alpine", true, true)]
    public async Task Runner_EnforcesUpstreamExpectation(
        string input, bool expectedAccept, bool match)
    {
        DiffCase testCase = UpstreamCase(input, expectedAccept);
        DiffResult result = await new DiffTestRunner(() => new MatchingParser())
            .RunSingleAsync(testCase);

        Assert.Equal(match, result.Match);
        Assert.Equal(
            match ? DiffOutcomeKind.Match : DiffOutcomeKind.UpstreamExpectationMismatch,
            result.Outcome);
        Assert.Equal(input == "FROM " ? "rejected" : "accepted", result.CSharpStatus);
        Assert.Equal(result.CSharpStatus, result.LeanStatus);
    }

    [Fact]
    public async Task Runner_EmptyVolumeDoesNotBypassComparison()
    {
        MatchingParser parser = new();
        DiffCase testCase = UpstreamCase("VOLUME []", true) with
        {
            InstructionType = "VOLUME"
        };

        DiffResult result = await new DiffTestRunner(() => parser).RunSingleAsync(testCase);

        Assert.True(parser.Called);
        Assert.False(result.Match);
        Assert.Equal(DiffOutcomeKind.JsonMismatch, result.Outcome);
    }

    [Fact]
    public async Task Runner_CSharpCrashStillExecutesLean()
    {
        MatchingParser parser = new();
        DiffResult result = await new DiffTestRunner(() => parser)
            .RunSingleAsync(UpstreamCase("FROM alpine", true) with { InstructionType = "UNKNOWN" });

        Assert.True(parser.Called);
        Assert.Equal("crashed", result.CSharpStatus);
        Assert.Equal("accepted", result.LeanStatus);
        Assert.Equal(DiffOutcomeKind.CSharpParserCrash, result.Outcome);
    }

    [Fact]
    public async Task Runner_InfrastructureFailureCannotBecomeConformanceFailure()
    {
        DiffResult result = await new DiffTestRunner(() => new BrokenParser())
            .RunSingleAsync(UpstreamCase("FROM ", false));

        Assert.Equal(DiffOutcomeKind.InfrastructureError, result.Outcome);
    }

    [Fact]
    public void Loader_PreservesInputAndProvenance()
    {
        using CorpusDirectory corpus = new();
        DiffCase testCase = Assert.Single(corpus.Load());

        Assert.Equal(DiffCaseSource.Upstream, testCase.Source);
        Assert.Equal("FROM alpine\r\n", testCase.Input);
        Assert.NotNull(testCase.Upstream);
        Assert.Equal("1.27.0", testCase.Upstream.FrontendVersion);
        Assert.Equal(1, testCase.Upstream.StartLine);
    }

    [Fact]
    public void Loader_RejectsRenovateVersionOnlyBump()
    {
        using CorpusDirectory corpus = new();
        File.WriteAllText(corpus.Compatibility,
            File.ReadAllText(corpus.Compatibility).Replace("1.27.0", "1.28.0"));

        Assert.Throws<InvalidDataException>(() => corpus.Load());
    }

    [Fact]
    public void Loader_RejectsChangedSourceBytes()
    {
        using CorpusDirectory corpus = new();
        File.AppendAllText(corpus.Source, "RUN echo changed");

        Assert.Throws<InvalidDataException>(() => corpus.Load());
    }

    [Theory]
    [InlineData("go.mod")]
    [InlineData("go.sum")]
    public void Loader_RejectsImporterDependencyDrift(string name)
    {
        using CorpusDirectory corpus = new();
        File.AppendAllText(Path.Combine(corpus.DirectoryPath, "importer", name), "changed");

        Assert.Throws<InvalidDataException>(() => corpus.Load());
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("repository", "\"other/repository\"")]
    [InlineData("frontendVersion", "\"1.27.0-labs\"")]
    [InlineData("sourceCommit", "\"not-a-commit\"")]
    public void Loader_RejectsInvalidCompatibilityIdentity(string field, string value)
    {
        using CorpusDirectory corpus = new();
        JsonNode target = JsonNode.Parse(File.ReadAllText(corpus.Compatibility))!;
        target[field] = JsonNode.Parse(value);
        File.WriteAllText(corpus.Compatibility, target.ToJsonString());

        Assert.Throws<InvalidDataException>(() => corpus.Load());
    }

    [Theory]
    [InlineData("inputBase64", "\"/w==\"")]
    [InlineData("sourcePath", "\"../../outside\"")]
    [InlineData("startLine", "2")]
    [InlineData("endLine", "99")]
    [InlineData("expectedParse", "\"anything\"")]
    [InlineData("instructionType", "\"UNKNOWN\"")]
    public void Loader_ValidatesFixtureBeyondManifestHash(string field, string value)
    {
        using CorpusDirectory corpus = new();
        string path = Path.Combine(corpus.Generated, "cases", "basic.json");
        JsonNode fixture = JsonNode.Parse(File.ReadAllText(path))!;
        fixture[field] = JsonNode.Parse(value);
        File.WriteAllText(path, fixture.ToJsonString());
        corpus.RefreshHashes();

        Assert.Throws<InvalidDataException>(() => corpus.Load());
    }

    [Fact]
    public void Loader_RejectsUnlistedFilesAndEmptyCases()
    {
        using CorpusDirectory corpus = new();
        string extra = Path.Combine(corpus.Generated, "extra.json");
        File.WriteAllText(extra, "{}");
        Assert.Throws<InvalidDataException>(() => corpus.Load());
        File.Delete(extra);
        File.Delete(Path.Combine(corpus.Generated, "cases", "basic.json"));
        corpus.RefreshHashes();
        Assert.Throws<InvalidDataException>(() => corpus.Load());
    }

    [Fact]
    public void KnownDeviations_RequireExactObservedFailureAndRejectStalePass()
    {
        using CorpusDirectory directory = new();
        DiffCase testCase = Assert.Single(directory.Load());
        DiffResult failure = new(testCase, "{}", "[]", DiffOutcomeKind.JsonMismatch,
            CSharpStatus: "accepted", LeanStatus: "accepted");
        directory.WriteDeviation(failure);
        UpstreamCorpus corpus = new(directory.DirectoryPath, directory.Compatibility);
        corpus.Load();

        Assert.True(corpus.Evaluate(failure).ExpectedFailure);
        Assert.False(corpus.Evaluate(failure with { LeanJson = "{}" }).Passed);
        Assert.False(corpus.Evaluate(failure with { Outcome = DiffOutcomeKind.Match }).Passed);
        Assert.False(corpus.Evaluate(failure with { Outcome = DiffOutcomeKind.InfrastructureError }).Passed);
        Assert.True(corpus.Evaluate(failure with { Error = "different localized message" }).ExpectedFailure);
    }

    [Theory]
    [InlineData("id", "\"removed-fixture\"")]
    [InlineData("issue", "\"\"")]
    [InlineData("inputSha256", "\"wrong-input\"")]
    [InlineData("outcomeSignature", "\"bad-signature\"")]
    [InlineData("reason", "\"\"")]
    public void KnownDeviations_RejectMissingProvenanceAndStaleEntries(string field, string value)
    {
        using CorpusDirectory directory = new();
        directory.WriteDeviation(new DiffResult(
            Assert.Single(directory.Load()), "{}", "[]", DiffOutcomeKind.JsonMismatch));
        string path = Path.Combine(directory.DirectoryPath, "known-deviations.json");
        JsonNode deviations = JsonNode.Parse(File.ReadAllText(path))!;
        deviations[0]![field] = JsonNode.Parse(value);
        File.WriteAllText(path, deviations.ToJsonString());

        Assert.Throws<InvalidDataException>(() => directory.Load());
    }

    [Fact]
    public async Task ImportedCases_AreNeverMinimizedOrPromoted()
    {
        using CorpusDirectory directory = new();
        DiffCase testCase = Assert.Single(directory.Load());
        DiffResult failure = new(testCase, "{}", "[]", DiffOutcomeKind.JsonMismatch);
        FailureMinimizer minimizer = new(() => throw new InvalidOperationException("Must not launch parser"));

        Assert.Same(failure, await minimizer.MinimizeAsync(failure));
        Assert.Throws<InvalidDataException>(() =>
            new RegressionCorpus(Path.Combine(directory.Root, "local")).Promote(testCase));
        Assert.False(Directory.Exists(Path.Combine(directory.Root, "local")));
    }

    [Fact]
    public void Replay_PreservesCorpusIdentityAndExpectations()
    {
        using CorpusDirectory directory = new();
        DiffCase testCase = Assert.Single(directory.Load());

        string command = ReplayCommand.Create(testCase, "lean.exe");

        Assert.Contains("--replay-upstream 'basic'", command);
        Assert.Contains("--upstream-corpus ", command);
        Assert.Contains(directory.DirectoryPath, command);
        Assert.Contains(directory.Compatibility, command);
        Assert.DoesNotContain("--input-base64", command);
    }

    internal static DiffCase UpstreamCase(string input, bool accept) =>
        new("upstream-from", DiffCaseSource.Upstream, "FROM", input, '\\',
            Upstream: new UpstreamExpectation(
                accept, "moby/buildkit", "1.27.0", new string('a', 40),
                "frontend/dockerfile/parser/testfiles/basic/Dockerfile", 1, 1,
                Hash(Encoding.UTF8.GetBytes(input)), "corpus", "compatibility.json"));

    internal static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class MatchingParser : ILeanParser
    {
        public bool Called { get; private set; }

        public Task<string> ParseAsync(string input, char escapeChar, CancellationToken cancellationToken)
        {
            Called = true;
            if (input == "VOLUME []")
            {
                return Task.FromResult("{}");
            }

            try
            {
                return Task.FromResult(DiffTestRunner.ParseCSharp("FROM", input, escapeChar));
            }
            catch (ParseException)
            {
                throw new LeanParseException("rejected");
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BrokenParser : ILeanParser
    {
        public Task<string> ParseAsync(string input, char escapeChar, CancellationToken cancellationToken) =>
            throw new LeanInfrastructureException("broken pipe");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class CorpusDirectory : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "DockerfileModel-Upstream-" + Guid.NewGuid().ToString("N"));
        public string DirectoryPath => Path.Combine(Root, "UpstreamCorpus");
        public string Compatibility => Path.Combine(Root, "upstream-compatibility.json");
        public string Generated => Path.Combine(DirectoryPath, "generated");
        public string Source => Path.Combine(Generated,
            "sources", Hash(Encoding.UTF8.GetBytes(
                "frontend/dockerfile/parser/testfiles/basic/Dockerfile"))[..16] + ".source");

        public CorpusDirectory()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            Directory.CreateDirectory(Path.Combine(Generated, "cases"));
            Directory.CreateDirectory(Path.Combine(DirectoryPath, "importer"));
            File.WriteAllText(Path.Combine(DirectoryPath, "importer", "go.mod"), "test module");
            File.WriteAllText(Path.Combine(DirectoryPath, "importer", "go.sum"), "test sums");
            const string input = "FROM alpine\r\n";
            const string sourcePath = "frontend/dockerfile/parser/testfiles/basic/Dockerfile";
            var identity = new
            {
                schemaVersion = 1,
                repository = "moby/buildkit",
                frontendVersion = "1.27.0",
                sourceCommit = new string('a', 40)
            };
            Write(Compatibility, identity);
            File.WriteAllText(Source, input);
            Write(Path.Combine(Generated, "cases", "basic.json"), new
            {
                id = "basic",
                instructionType = "FROM",
                inputBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(input)),
                escapeCharacter = "\\",
                expectedParse = "accept",
                sourcePath,
                startLine = 1,
                endLine = 1,
                inputSha256 = Hash(Encoding.UTF8.GetBytes(input))
            });
            Write(Path.Combine(DirectoryPath, "known-deviations.json"), Array.Empty<object>());
            Write(Path.Combine(Generated, "manifest.json"), new
            {
                identity.schemaVersion, identity.repository,
                identity.frontendVersion, identity.sourceCommit,
                importerGoModSha256 = Hash(Encoding.UTF8.GetBytes("test module")),
                importerGoSumSha256 = Hash(Encoding.UTF8.GetBytes("test sums")),
                files = Directory.EnumerateFiles(Generated, "*", SearchOption.AllDirectories)
                    .Select(path => new
                    {
                        path = Path.GetRelativePath(Generated, path).Replace('\\', '/'),
                        sha256 = Hash(File.ReadAllBytes(path))
                    }).ToArray()
            });
        }

        public IReadOnlyList<DiffCase> Load() =>
            new UpstreamCorpus(DirectoryPath, Compatibility).Load();

        public static void Write(string path, object value) =>
            File.WriteAllText(path, JsonSerializer.Serialize(value,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        public void WriteDeviation(DiffResult result) =>
            Write(Path.Combine(DirectoryPath, "known-deviations.json"), new[]
            {
                new KnownUpstreamDeviation(result.Case.Id, result.Case.Upstream!.InputSha256,
                    UpstreamCorpus.OutcomeSignature(result),
                    "https://github.com/mthalman/DockerfileModel/issues/358", "Test-only expected difference.")
            });

        public void RefreshHashes()
        {
            string path = Path.Combine(Generated, "manifest.json");
            JsonNode manifest = JsonNode.Parse(File.ReadAllText(path))!;
            manifest["files"] = JsonSerializer.SerializeToNode(
                Directory.EnumerateFiles(Generated, "*", SearchOption.AllDirectories)
                    .Where(file => file != path)
                    .Select(file => new
                    {
                        path = Path.GetRelativePath(Generated, file).Replace('\\', '/'),
                        sha256 = Hash(File.ReadAllBytes(file))
                    }));
            File.WriteAllText(path, manifest.ToJsonString());
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
