namespace Valleysoft.DockerfileModel.Tests;

public class DockerfileAnalysisResolutionTests
{
    [Theory]
    [InlineData(false, "$KIND")]
    [InlineData(false, "${KIND:-bind}")]
    [InlineData(true, "$KIND")]
    [InlineData(true, "${KIND:-bind}")]
    public void UnsetGlobalDoesNotClearExistingStageArg(bool inherited, string mountType)
    {
        Dockerfile dockerfile = ParseMountWithRedeclaredArg("ARG KIND", inherited, mountType);
        string original = dockerfile.ToString();

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference mount = Assert.Single(analysis.References,
            reference => reference.Kind == DockerfileReferenceKind.MountSource);
        Assert.Equal(DockerfileReferenceClassification.Invalid, mount.Classification);
        Assert.Equal("busybox", mount.ResolvedValue);
        Assert.Equal(AnalysisDiagnosticCode.InvalidMountSource, Assert.Single(mount.Diagnostics).Code);
        Assert.DoesNotContain(analysis.ExternalImages, reference => reference.ResolvedValue == "busybox");
        Assert.Empty(analysis.UnresolvedReferences);
        Assert.Equal(original, dockerfile.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyGlobalStillReplacesExistingStageArg(bool inherited)
    {
        DockerfileAnalysis analysis = ParseMountWithRedeclaredArg("ARG KIND=", inherited, "${KIND:-bind}").Analyze();

        DockerfileReference mount = Assert.Single(analysis.ExternalImages,
            reference => reference.Kind == DockerfileReferenceKind.MountSource);
        Assert.Equal("busybox", mount.ResolvedValue);
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnresolvedGlobalRetainsProvenanceOverExistingStageArg(bool inherited)
    {
        DockerfileAnalysis analysis = ParseMountWithRedeclaredArg("ARG KIND=$MISSING", inherited, "${KIND:-bind}").Analyze();

        DockerfileReference mount = Assert.Single(analysis.UnresolvedReferences);
        Assert.Equal(DockerfileReferenceKind.MountSource, mount.Kind);
        AnalysisDiagnostic diagnostic = Assert.Single(mount.Diagnostics);
        Assert.Equal(AnalysisDiagnosticCode.UnresolvedVariable, diagnostic.Code);
        Assert.Contains("MISSING", diagnostic.VariableNames);
        Assert.DoesNotContain(analysis.ExternalImages, reference => reference.ResolvedValue == "busybox");
    }

    private static Dockerfile ParseMountWithRedeclaredArg(string globalArg, bool inherited, string mountType) =>
        Dockerfile.Parse(globalArg + "\nFROM alpine AS parent\nARG KIND=secret\n" +
            (inherited ? "FROM parent AS child\n" : "") +
            $"ARG KIND\nRUN --mount=type={mountType},from=busybox,target=/src true\n");

    [Theory]
    [InlineData("FROM alpine\nARG KIND=bind\nARG KIND\n")]
    [InlineData("FROM alpine AS parent\nARG KIND=bind\nFROM parent AS child\nARG KIND\n")]
    public void BareStageArgImportsGlobalBeforeExistingStageValue(string stages)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse("ARG KIND=secret\n" + stages +
            "RUN --mount=type=$KIND,from=busybox,target=/src true\n").Analyze();

        Assert.Contains(analysis.Diagnostics, diagnostic =>
            diagnostic.Code == AnalysisDiagnosticCode.InvalidMountSource);
        Assert.DoesNotContain(analysis.ExternalImages, reference => reference.ResolvedValue == "busybox");
    }

    [Theory]
    [InlineData("SECRET")]
    [InlineData("'$KIND'")]
    [InlineData("'s\\ecret'")]
    [InlineData("not-a-mount-type")]
    [InlineData("''")]
    public void MountTypeUsesBuilderDecodingAndCaseInsensitiveValues(string type)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM alpine\nARG KIND=secret\nRUN --mount=type={type},from=busybox,target=/src true\n").Analyze();

        Assert.Contains(analysis.Diagnostics, diagnostic =>
            diagnostic.Code == AnalysisDiagnosticCode.InvalidMountSource);
        Assert.DoesNotContain(analysis.ExternalImages, reference => reference.ResolvedValue == "busybox");
    }

    [Theory]
    [InlineData("ARG BASE=alpine\nFROM ${BASE}\n", "alpine")]
    [InlineData("ARG BASE=alpine\nARG BASE=busybox\nFROM $BASE\n", "busybox")]
    [InlineData("ARG REGISTRY=example.com\nARG BASE=${REGISTRY}/app\nFROM $BASE\n", "example.com/app")]
    [InlineData("ARG BASE\nFROM ${BASE:-alpine}\n", "alpine")]
    [InlineData("ARG BASE=\nFROM ${BASE:-alpine}\n", "alpine")]
    [InlineData("ARG BASE=busybox\nFROM ${BASE:+alpine}\n", "alpine")]
    [InlineData("ARG BASE=busybox\nFROM ${BASE:-${MISSING:?unused}}\n", "busybox")]
    [InlineData("ARG BASE=alpine\nARG BASE\nFROM $BASE\n", "alpine")]
    public void ResolvesGlobalExpressions(string text, string expected)
    {
        Dockerfile dockerfile = Dockerfile.Parse(text);
        DockerfileAnalysis analysis = dockerfile.Analyze();

        Assert.Equal(expected, Assert.Single(analysis.ExternalImages).ResolvedValue);
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void OverridesShortCircuitDefaultsAndAreNotMutated()
    {
        Dockerfile dockerfile = Dockerfile.Parse("ARG BASE=${MISSING:?unused}\nFROM $BASE\n");
        Dictionary<string, string?> overrides = new() { ["BASE"] = "busybox" };

        DockerfileAnalysis analysis = dockerfile.Analyze(overrides);
        overrides["BASE"] = "alpine";

        Assert.Equal("busybox", Assert.Single(analysis.ExternalImages).ResolvedValue);
        Assert.Equal("alpine", Assert.Single(dockerfile.Analyze(overrides).ExternalImages).ResolvedValue);
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal("ARG BASE=${MISSING:?unused}\nFROM $BASE\n", dockerfile.ToString());
    }

    [Fact]
    public void OnlyDeclaredOrAutomaticArgsUseOverrides()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse("FROM $UNDECLARED\n")
            .Analyze(new Dictionary<string, string?> { ["UNDECLARED"] = "alpine" });

        Assert.Single(analysis.UnresolvedReferences);
        Assert.Empty(analysis.ExternalImages);
    }

    [Theory]
    [InlineData(null, "alpine")]
    [InlineData("", "alpine")]
    [InlineData("busybox", "busybox")]
    public void NullAndEmptyOverridesRespectDefaults(string? value, string expected)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse("ARG BASE=ubuntu\nFROM ${BASE:-alpine}\n")
            .Analyze(new Dictionary<string, string?> { ["BASE"] = value });

        Assert.Equal(expected, Assert.Single(analysis.ExternalImages).ResolvedValue);
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void UnresolvedChainsDoNotBecomeExternalImages()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "ARG FIRST=$MISSING\nARG SECOND=${FIRST:-alpine}\nFROM $SECOND\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.UnresolvedReferences);
        Assert.Null(reference.ResolvedValue);
        Assert.Empty(analysis.ExternalImages);
        Assert.Contains("MISSING", Assert.Single(reference.Diagnostics).VariableNames);
    }

    [Theory]
    [InlineData("FROM ${BASE:?a base is required}\n", AnalysisDiagnosticCode.VariableSubstitutionFailed)]
    [InlineData("ARG BASE=prefixalpine\nFROM ${BASE#prefix}\n", AnalysisDiagnosticCode.UnsupportedEvaluation)]
    public void EvaluationFailuresHaveStructuredDiagnostics(string text, AnalysisDiagnosticCode code)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(text).Analyze();

        Assert.Single(analysis.UnresolvedReferences);
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Empty(analysis.ExternalImages);
    }

    [Fact]
    public void GlobalOverrideCanResolveFromToPriorStage()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "ARG BASE=alpine\nFROM busybox AS build\nFROM $BASE\n")
            .Analyze(new Dictionary<string, string?> { ["BASE"] = "build" });

        Assert.Equal(0, Assert.Single(analysis.Dependencies).TargetStage!.Index);
        Assert.Single(analysis.ExternalImages);
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void StageArgsDoNotLeakIntoFrom()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "ARG BASE=alpine\nFROM busybox\nARG BASE=ubuntu\nENV BASE=debian\nFROM $BASE\n").Analyze();

        Assert.Equal(new[] { "busybox", "alpine" }, analysis.ExternalImages.Select(image => image.ResolvedValue));
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("COPY --from=$BASE /app /app\n")]
    [InlineData("RUN --mount=from=$BASE,target=/app echo hello\n")]
    public void SelectorsNeverExpandOverrides(string instruction)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse("FROM alpine\nARG BASE=busybox\n" + instruction)
            .Analyze(new Dictionary<string, string?> { ["BASE"] = "ubuntu" });

        Assert.Equal("alpine", Assert.Single(analysis.ExternalImages).ResolvedValue);
        Assert.Contains(analysis.Diagnostics,
            diagnostic => diagnostic.Code == AnalysisDiagnosticCode.UnsupportedVariableExpansion);
        Assert.Empty(analysis.Dependencies);
    }

    [Fact]
    public void AutomaticArgsAreCallerProvidedNotHostInferred()
    {
        Dockerfile dockerfile = Dockerfile.Parse("FROM example.com/app:$TARGETARCH\n");

        Assert.Single(dockerfile.Analyze().UnresolvedReferences);
        DockerfileAnalysis analysis = dockerfile.Analyze(new Dictionary<string, string?> { ["TARGETARCH"] = "arm64" });

        Assert.Equal("example.com/app:arm64", Assert.Single(analysis.ExternalImages).ResolvedValue);
    }

    [Fact]
    public void InheritedStageEnvironmentResolvesMountType()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine AS parent\nARG KIND=secret\nENV KIND=bind\n" +
            "FROM parent\nRUN --mount=type=$KIND,from=busybox,target=/app echo hello\n").Analyze();

        Assert.Equal(new[] { "alpine", "busybox" }, analysis.ExternalImages.Select(image => image.ResolvedValue));
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void InvalidResolvedMountTypeSourceIsDiagnosed()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine\nARG KIND=secret\nRUN --mount=type=$KIND,from=busybox,target=/app echo hello\n").Analyze();

        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.InvalidMountSource);
        Assert.Single(analysis.ExternalImages);
    }

    [Fact]
    public void UnknownMountTypeIsNotAssumedValid()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine\nRUN --mount=type=$KIND,from=busybox,target=/app echo hello\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.UnresolvedReferences);
        Assert.Equal("busybox", reference.OriginalText);
        Assert.Contains("KIND", Assert.Single(reference.Diagnostics).VariableNames);
    }
}
