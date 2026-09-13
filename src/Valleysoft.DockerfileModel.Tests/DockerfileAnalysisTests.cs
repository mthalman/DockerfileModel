using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class DockerfileAnalysisTests
{
    [Theory]
    [InlineData("sha256", 32, 'a')]
    [InlineData("sha256", 65, 'a')]
    [InlineData("sha256", 64, 'A')]
    [InlineData("unknown", 64, 'a')]
    [InlineData("sha512", 64, 'a')]
    public void InvalidDigestEncodingIsNotInventoried(string algorithm, int length, char digit)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM alpine@{algorithm}:{new string(digit, length)}\n").Analyze();

        Assert.Empty(analysis.ExternalImages);
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.InvalidReference);
    }

    [Theory]
    [InlineData("sha256", 64)]
    [InlineData("sha384", 96)]
    [InlineData("sha512", 128)]
    public void ValidDigestAlgorithmsAreInventoried(string algorithm, int length)
    {
        string image = $"alpine:latest@{algorithm}:{new string('a', length)}";
        DockerfileAnalysis analysis = Dockerfile.Parse($"FROM {image}\n").Analyze();

        Assert.Equal(image, Assert.Single(analysis.ExternalImages).ResolvedValue);
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("from=busybox,source=.\\,from=build,target=/src")]
    [InlineData("source=.\\,from=build,target=/src")]
    public void DecodedMountBoundariesDoNotProduceGuessedReferences(string mount)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            $"FROM alpine AS build\nFROM scratch\nRUN --mount={mount} true\n");

        DockerfileAnalysis analysis = dockerfile.Analyze();

        Assert.Empty(analysis.Dependencies);
        Assert.DoesNotContain(analysis.ExternalImages, reference => reference.Kind == DockerfileReferenceKind.MountSource);
        DockerfileReference unresolved = Assert.Single(analysis.UnresolvedReferences);
        Assert.Equal(AnalysisDiagnosticCode.UnsupportedEvaluation, Assert.Single(unresolved.Diagnostics).Code);
        Assert.Same(Assert.Single(dockerfile.Items.OfType<RunInstruction>()).Mounts[0], unresolved.OperandToken);
    }

    [Fact]
    public void UnmappedOnBuildMountRetainsDeclarationAndChildProvenance()
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "FROM alpine AS parent\nONBUILD RUN --mount=source=.\\,from=tools,target=/src true\n" +
            "FROM busybox AS tools\nFROM parent AS child\n");

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference deferred = Assert.Single(analysis.References,
            reference => reference.Phase == DockerfileReferencePhase.DeferredOnBuild);
        DockerfileReference inherited = Assert.Single(analysis.UnresolvedReferences);
        Assert.Same(deferred.OperandToken, inherited.OperandToken);
        Assert.Same(deferred.OnBuildInstruction, inherited.OnBuildInstruction);
        Assert.Equal(2, inherited.SourceStage!.Index);
        Assert.Null(inherited.TargetStage);
        Assert.Single(analysis.Dependencies);
        Assert.Contains(inherited.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.UnsupportedEvaluation);
    }

    [Fact]
    public void MountEntryKeysAreDecodedBeforeLookup()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine AS build\nFROM scratch\nRUN --mount=fr\\om=build,target=/src true\n").Analyze();

        Assert.Equal(0, Assert.Single(analysis.Dependencies).TargetStage!.Index);
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("\"build\"")]
    [InlineData("'build'")]
    [InlineData("b\\uild")]
    [InlineData("\"b\\uild\"")]
    [InlineData("'b\\uild'")]
    public void BuilderFlagDecodingPrecedesSourceLookup(string operand)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM alpine AS build\nFROM scratch\nCOPY --from={operand} /app /app\n" +
            $"RUN --mount=from={operand},target=/app echo hello\n").Analyze();

        Assert.Equal(2, analysis.Dependencies.Count);
        Assert.All(analysis.Dependencies, reference =>
        {
            Assert.Equal("build", reference.ResolvedValue);
            Assert.Equal(operand, reference.OriginalText);
            Assert.Equal(0, reference.TargetStage!.Index);
        });
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void SurvivingEscapesInCopyGuardDoNotBecomeStageNames()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine AS build\nFROM scratch\nCOPY --from=\\\\build /app /app\n").Analyze();

        Assert.Empty(analysis.Dependencies);
        Assert.Contains(analysis.Diagnostics, diagnostic =>
            diagnostic.Code == AnalysisDiagnosticCode.UnsupportedVariableExpansion);
    }

    [Theory]
    [InlineData("alpine")]
    [InlineData("example.com:5000/team/app:Tag")]
    [InlineData("[2001:db8::1]:5000/team/app:_tag")]
    [InlineData("example.com/team/a__b")]
    [InlineData("example.com/team/a--b")]
    [InlineData("example.com/team/a.b")]
    public void RecognizesExternalImageSyntax(string image)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse($"FROM {image}\n").Analyze();

        Assert.Equal(image, Assert.Single(analysis.ExternalImages).ResolvedValue);
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void RecognizesTagAndDigestTogetherWithoutRewriting()
    {
        string image = "example.com/team/app:tag@sha256:" + new string('a', 64);
        Dockerfile dockerfile = Dockerfile.Parse($"FROM {image}\nCOPY --from={image} /app /app\n");

        DockerfileAnalysis analysis = dockerfile.Analyze();

        Assert.Equal(new[] { image, image }, analysis.ExternalImages.Select(reference => reference.ResolvedValue));
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal($"FROM {image}\nCOPY --from={image} /app /app\n", dockerfile.ToString());
    }

    [Theory]
    [InlineData("Uppercase")]
    [InlineData("bad:name:tag")]
    [InlineData("alpine:")]
    [InlineData("image@sha256:short")]
    [InlineData("a..b")]
    public void MalformedImageReferencesAreNotInventoried(string image)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse($"FROM {image}\n").Analyze();

        Assert.Empty(analysis.ExternalImages);
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.InvalidReference);
    }

    [Fact]
    public void LookupAndDependenciesRetainOriginalObjects()
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "FROM alpine AS build\nFROM build AS final\nCOPY --from=0 /app /app\n");

        DockerfileAnalysis analysis = dockerfile.Analyze();

        Assert.Equal(2, analysis.Stages.Count);
        Assert.Same(analysis.Stages[0], analysis.GetStage("BUILD"));
        Assert.Same(analysis.Stages[1], analysis.GetStage(1));
        Assert.Same(dockerfile.Items.OfType<FromInstruction>().First(), analysis.Stages[0].FromInstruction);
        Assert.Equal(new[] { DockerfileReferenceKind.BaseStage, DockerfileReferenceKind.CopySource },
            analysis.Dependencies.Select(reference => reference.Kind));
        Assert.All(analysis.Dependencies, reference =>
        {
            Assert.Same(analysis.Stages[1], reference.SourceStage);
            Assert.Same(analysis.Stages[0], reference.TargetStage);
        });
        DockerfileReference image = Assert.Single(analysis.ExternalImages);
        Assert.Equal("alpine", image.ResolvedValue);
        Assert.Same(analysis.References[0], image);
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void MountsIncludeOmittedAndReorderedTypes()
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "FROM alpine AS build\nFROM scratch\n" +
            "RUN --mount=from=build,target=/src --mount=from=busybox,type=bind,target=/bin echo hello\n");

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference dependency = Assert.Single(analysis.Dependencies);
        Assert.Equal(DockerfileReferenceKind.MountSource, dependency.Kind);
        Assert.Equal(0, dependency.TargetStage!.Index);
        Assert.Equal(new[] { "alpine", "busybox" }, analysis.ExternalImages.Select(image => image.ResolvedValue));
        RunInstruction run = Assert.Single(dockerfile.Items.OfType<RunInstruction>());
        Assert.Same(run, dependency.Instruction);
        Assert.Contains(dependency.OperandToken,
            run.Mounts[0].Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>().Select(token => token.ValueToken));
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void UnknownVariablesRemainExplicit()
    {
        Dockerfile dockerfile = Dockerfile.Parse("ARG BASE\nFROM ${BASE}\n");

        DockerfileAnalysis analysis = dockerfile.Analyze();

        Assert.Empty(analysis.ExternalImages);
        DockerfileReference reference = Assert.Single(analysis.UnresolvedReferences);
        Assert.Equal("${BASE}", reference.OriginalText);
        Assert.Null(reference.ResolvedValue);
        Assert.Contains(analysis.Diagnostics, diagnostic =>
            diagnostic.Code == AnalysisDiagnosticCode.UnresolvedVariable &&
            diagnostic.VariableNames.Contains("BASE"));
        Assert.Equal("ARG BASE\nFROM ${BASE}\n", dockerfile.ToString());
    }

    [Fact]
    public void InheritedOnBuildDependenciesBelongToChild()
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "FROM alpine AS parent\nONBUILD COPY --from=tools /tool /tool\n" +
            "FROM busybox AS tools\nFROM parent AS child\nFROM child AS grandchild\n");

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference deferred = Assert.Single(analysis.References, reference =>
            reference.Phase == DockerfileReferencePhase.DeferredOnBuild);
        Assert.Null(deferred.SourceStage);
        Assert.Null(deferred.TargetStage);
        DockerfileReference inherited = Assert.Single(analysis.References, reference =>
            reference.Phase == DockerfileReferencePhase.InheritedOnBuild);
        Assert.Equal(2, inherited.SourceStage!.Index);
        Assert.Equal(1, inherited.TargetStage!.Index);
        Assert.Same(deferred.Instruction, inherited.Instruction);
        Assert.Same(deferred.OperandToken, inherited.OperandToken);
        Assert.Same(deferred.OnBuildInstruction, inherited.OnBuildInstruction);
        Assert.Empty(analysis.Diagnostics);
    }
}
