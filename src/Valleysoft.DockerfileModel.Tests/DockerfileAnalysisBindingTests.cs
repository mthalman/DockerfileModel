using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class DockerfileAnalysisBindingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("# syntax=docker/dockerfile:1\n# comment\nARG BASE=alpine\n\n")]
    public void EmptyAndArgOnlyDocumentsHaveNoStagesOrReferences(string text)
    {
        Dockerfile dockerfile = Dockerfile.Parse(text);

        DockerfileAnalysis analysis = dockerfile.Analyze();

        Assert.Empty(analysis.Stages);
        Assert.Empty(analysis.References);
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void FromUsesLatestPreviousDuplicateWhileCopyAndMountUseFinalAlias()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine AS build\nFROM build AS first\n" +
            "COPY --from=BUILD /app /app\nRUN --mount=from=BUILD,target=/src true\n" +
            "FROM busybox AS BUILD\nFROM build AS last\n").Analyze();

        DockerfileReference firstBase = Assert.Single(analysis.Dependencies, reference =>
            reference.Kind == DockerfileReferenceKind.BaseStage && reference.SourceStage!.Index == 1);
        DockerfileReference lastBase = Assert.Single(analysis.Dependencies, reference =>
            reference.Kind == DockerfileReferenceKind.BaseStage && reference.SourceStage!.Index == 3);
        Assert.Same(analysis.GetStage(0), firstBase.TargetStage);
        Assert.Same(analysis.GetStage(2), lastBase.TargetStage);
        foreach (DockerfileReferenceKind kind in new[]
        {
            DockerfileReferenceKind.CopySource, DockerfileReferenceKind.MountSource
        })
        {
            DockerfileReference source = Assert.Single(analysis.Dependencies, reference => reference.Kind == kind);
            Assert.Same(analysis.GetStage(2), source.TargetStage);
            Assert.Contains(source.Diagnostics, diagnostic =>
                diagnostic.Code == AnalysisDiagnosticCode.ForwardStageReference);
        }
        Assert.Contains(analysis.Diagnostics, diagnostic =>
            diagnostic.Code == AnalysisDiagnosticCode.DuplicateStageName);
        Assert.Equal(new[] { 0, 2 }, analysis.FindStages("BuIlD").Select(stage => stage.Index));
        Assert.Throws<InvalidOperationException>(() => analysis.GetStage("build"));
    }

    [Fact]
    public void FromAliasLookupDoesNotLowercaseItsOperand()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine AS Build\nFROM build AS lower\nFROM Build AS mixed\n").Analyze();

        DockerfileReference bound = Assert.Single(analysis.Dependencies);
        Assert.Same(analysis.GetStage(0), bound.TargetStage);
        Assert.Same(analysis.GetStage(1), bound.SourceStage);
        DockerfileReference mixed = Assert.Single(analysis.References, reference => reference.SourceStage!.Index == 2);
        Assert.Null(mixed.TargetStage);
        Assert.Equal(DockerfileReferenceClassification.Invalid, mixed.Classification);
        Assert.Contains(mixed.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.InvalidReference);
        Assert.Same(analysis.GetStage(0), analysis.GetStage("BUILD"));
    }

    [Theory]
    [InlineData("FROM later AS first\nFROM alpine AS later\n", "later")]
    [InlineData("FROM self AS self\n", "self")]
    [InlineData("FROM alpine AS build\nFROM 0 AS final\n", "0")]
    public void FromDoesNotBindLaterSelfOrNumericSelectors(string text, string image)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(text).Analyze();

        DockerfileReference reference = Assert.Single(analysis.ExternalImages,
            candidate => candidate.ResolvedValue == image);
        Assert.Null(reference.TargetStage);
        Assert.Empty(analysis.Dependencies);
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("BUILD")]
    [InlineData("BuIlD")]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("+0")]
    [InlineData("+000")]
    [InlineData("-0")]
    [InlineData("-000")]
    public void CopyBindsCaseInsensitiveNamesAndSignedZeroPaddedIndices(string selector)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            $"FROM alpine AS Build\nFROM scratch\nCOPY --from={selector} /app /app\n");

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference reference = Assert.Single(analysis.Dependencies);
        CopyInstruction copy = Assert.Single(dockerfile.Items.OfType<CopyInstruction>());
        Assert.Equal(DockerfileReferenceKind.CopySource, reference.Kind);
        Assert.Equal(DockerfileReferencePhase.Immediate, reference.Phase);
        Assert.Same(analysis.GetStage(0), reference.TargetStage);
        Assert.Same(analysis.GetStage(1), reference.SourceStage);
        Assert.Same(reference.SourceStage, reference.DeclaringStage);
        Assert.Null(reference.OnBuildInstruction);
        Assert.Same(copy, reference.Instruction);
        Assert.Same(copy.FromStageNameToken, reference.OperandToken);
        Assert.Equal(selector, reference.OriginalText);
        Assert.Equal(selector, reference.ResolvedValue);
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("\"BUILD\"", "BUILD", DockerfileReferenceClassification.Stage)]
    [InlineData("'build'", "build", DockerfileReferenceClassification.Stage)]
    [InlineData("\"0\"", "0", DockerfileReferenceClassification.Stage)]
    [InlineData("\"busybox\"", "busybox", DockerfileReferenceClassification.ExternalImage)]
    public void QuotedCopySelectorsRetainSpellingAndBindTheirSemanticValue(
        string selector, string value, DockerfileReferenceClassification classification)
    {
        string text = $"FROM alpine AS build\nFROM scratch\nCOPY --from={selector} /app /app\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            candidate => candidate.Kind == DockerfileReferenceKind.CopySource);
        Assert.Equal(selector, reference.OriginalText);
        Assert.Equal(value, reference.ResolvedValue);
        Assert.Equal(classification, reference.Classification);
        if (classification == DockerfileReferenceClassification.Stage)
        {
            Assert.Same(analysis.GetStage(0), reference.TargetStage);
        }
        else
        {
            Assert.Null(reference.TargetStage);
            Assert.Contains(reference, analysis.ExternalImages);
        }
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-01")]
    [InlineData("2")]
    [InlineData("+002")]
    [InlineData("2147483648")]
    [InlineData("9223372036854775807")]
    public void CopyInvalidDeclaredStageIndicesDoNotBecomeImages(string selector)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM alpine AS build\nFROM scratch\nCOPY --from={selector} /app /app\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            candidate => candidate.Kind == DockerfileReferenceKind.CopySource);
        Assert.Equal(DockerfileReferenceClassification.Invalid, reference.Classification);
        Assert.Null(reference.TargetStage);
        Assert.DoesNotContain(reference, analysis.ExternalImages);
        Assert.Contains(reference.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.InvalidStageIndex);
    }

    [Theory]
    [InlineData("9223372036854775808", DockerfileReferenceClassification.ExternalImage)]
    [InlineData("999999999999999999999999999999999999", DockerfileReferenceClassification.ExternalImage)]
    [InlineData("-9223372036854775809", DockerfileReferenceClassification.Invalid)]
    [InlineData("+9223372036854775808", DockerfileReferenceClassification.Invalid)]
    public void CopyIntegerOverflowFallsBackToImageValidation(
        string selector, DockerfileReferenceClassification classification)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM scratch\nCOPY --from={selector} /app /app\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            candidate => candidate.Kind == DockerfileReferenceKind.CopySource);
        Assert.Equal(classification, reference.Classification);
        Assert.Null(reference.TargetStage);
        Assert.DoesNotContain(reference.Diagnostics, diagnostic =>
            diagnostic.Code == AnalysisDiagnosticCode.InvalidStageIndex);
        if (classification == DockerfileReferenceClassification.ExternalImage)
        {
            Assert.Same(reference, Assert.Single(analysis.ExternalImages));
            Assert.Equal(selector, reference.ResolvedValue);
            Assert.Empty(analysis.Diagnostics);
        }
        else
        {
            Assert.Contains(reference.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.InvalidReference);
        }
    }

    [Theory]
    [InlineData("COPY --from=build /app /app")]
    [InlineData("COPY --from=0 /app /app")]
    [InlineData("RUN --mount=from=build,target=/src true")]
    public void SelfDependenciesAreRetainedAndDiagnosed(string instruction)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse($"FROM alpine AS build\n{instruction}\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.Dependencies);
        Assert.Same(reference.SourceStage, reference.TargetStage);
        Assert.Contains(reference.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.CircularStageDependency);
    }

    [Theory]
    [InlineData("COPY --from=second /app /app")]
    [InlineData("COPY --from=1 /app /app")]
    [InlineData("RUN --mount=from=SECOND,target=/src true")]
    public void ForwardDependenciesRemainVisible(string instruction)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM alpine AS first\n{instruction}\nFROM busybox AS second\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.Dependencies);
        Assert.Same(analysis.GetStage(0), reference.SourceStage);
        Assert.Same(analysis.GetStage(1), reference.TargetStage);
        Assert.Contains(reference.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.ForwardStageReference);
        Assert.DoesNotContain(analysis.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.CircularStageDependency);
    }

    [Fact]
    public void MixedBaseCopyAndMountCycleIsDiagnosedWithoutLosingEdges()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine AS first\nCOPY --from=third /app /app\n" +
            "FROM first AS second\nFROM busybox AS third\nRUN --mount=from=second,target=/src true\n").Analyze();

        Assert.Equal(3, analysis.Dependencies.Count);
        Assert.All(analysis.Dependencies, reference =>
            Assert.Contains(reference.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.CircularStageDependency));
        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.ForwardStageReference);
    }

    [Fact]
    public void CycleDiagnosticsIdentifyTheEntireComponentAndExcludeDisconnectedEdges()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine AS first\nCOPY --from=second /one /one\nCOPY --from=second /two /two\n" +
            "FROM busybox AS second\nRUN --mount=from=third,target=/src true\nFROM first AS third\n" +
            "FROM scratch AS disconnected\nCOPY --from=last /app /app\nFROM debian AS last\n").Analyze();

        Assert.Equal(5, analysis.Dependencies.Count);
        DockerfileReference[] cyclicEdges = analysis.Dependencies
            .Where(reference => reference.SourceStage!.Index < 3).ToArray();
        Assert.Equal(4, cyclicEdges.Length);
        Assert.All(cyclicEdges, reference =>
        {
            AnalysisDiagnostic diagnostic = Assert.Single(reference.Diagnostics,
                candidate => candidate.Code == AnalysisDiagnosticCode.CircularStageDependency);
            Assert.Equal(new[] { 0, 1, 2 }, diagnostic.RelatedStageIndices.OrderBy(index => index));
            Assert.Same(reference.Instruction, diagnostic.Instruction);
            Assert.Same(reference.OperandToken, diagnostic.OperandToken);
            Assert.Contains(diagnostic, analysis.Diagnostics);
        });
        DockerfileReference disconnected = Assert.Single(analysis.Dependencies,
            reference => reference.SourceStage!.Index == 3);
        Assert.Same(analysis.GetStage(4), disconnected.TargetStage);
        Assert.DoesNotContain(disconnected.Diagnostics,
            diagnostic => diagnostic.Code == AnalysisDiagnosticCode.CircularStageDependency);
        Assert.Contains(disconnected.Diagnostics,
            diagnostic => diagnostic.Code == AnalysisDiagnosticCode.ForwardStageReference);
    }

    [Fact]
    public void LargeAcyclicStageChainRetainsEveryDirectDependency()
    {
        const int stageCount = 1024;
        string text = "FROM scratch AS stage0\n" + string.Concat(Enumerable.Range(1, stageCount - 1)
            .Select(index => $"FROM stage{index - 1} AS stage{index}\n"));
        Dockerfile dockerfile = Dockerfile.Parse(text);

        DockerfileAnalysis analysis = dockerfile.Analyze();

        Assert.Equal(stageCount, analysis.Stages.Count);
        Assert.Equal(stageCount - 1, analysis.Dependencies.Count);
        for (int i = 1; i < stageCount; i++)
        {
            DockerfileReference reference = analysis.Dependencies[i - 1];
            Assert.Same(analysis.GetStage(i), reference.SourceStage);
            Assert.Same(analysis.GetStage(i - 1), reference.TargetStage);
            Assert.Equal(DockerfileReferenceKind.BaseStage, reference.Kind);
        }
        Assert.Empty(analysis.Diagnostics);
        Assert.Empty(analysis.ExternalImages);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Theory]
    [InlineData("from=BuIlD,target=/src")]
    [InlineData("from=BUILD,type=bind,target=/src")]
    [InlineData("type=bind,from=build,target=/src")]
    [InlineData("from=build,target=/src,type=cache")]
    [InlineData("FROM=BUILD,TARGET=/src")]
    public void MountUsesNamedLookupWithOmittedReorderedAndCaseInsensitiveKeys(string mount)
    {
        string text = $"FROM alpine AS build\nFROM scratch\nRUN --mount={mount} true\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference reference = Assert.Single(analysis.Dependencies);
        Assert.Equal(DockerfileReferenceKind.MountSource, reference.Kind);
        Assert.Same(analysis.GetStage(0), reference.TargetStage);
        Assert.Same(analysis.GetStage(1), reference.SourceStage);
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("2147483648")]
    public void NumericMountSourcesAreImagesRatherThanStageIndices(string selector)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM alpine AS build\nFROM scratch\nRUN --mount=from={selector},target=/src true\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.ExternalImages,
            candidate => candidate.Kind == DockerfileReferenceKind.MountSource);
        Assert.Equal(selector, reference.ResolvedValue);
        Assert.Null(reference.TargetStage);
        Assert.Empty(analysis.Dependencies);
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("from=missing,from=build,target=/src", "build", true)]
    [InlineData("from=build,from=busybox,target=/src", "busybox", false)]
    public void RepeatedMountFromKeysProduceOnlyTheLastEffectiveOccurrence(
        string mount, string value, bool isStage)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            $"FROM alpine AS build\nFROM scratch\nRUN --mount={mount} true\n");
        RunInstruction run = Assert.Single(dockerfile.Items.OfType<RunInstruction>());
        KeyValueToken<KeywordToken, LiteralToken>[] entries = run.Mounts[0].Tokens
            .OfType<KeyValueToken<KeywordToken, LiteralToken>>().Where(entry => entry.Key == "from").ToArray();

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            candidate => candidate.Kind == DockerfileReferenceKind.MountSource);
        Assert.Equal(value, reference.ResolvedValue);
        Assert.Same(entries[1].ValueToken, reference.OperandToken);
        Assert.Equal(isStage ? DockerfileReferenceClassification.Stage : DockerfileReferenceClassification.ExternalImage,
            reference.Classification);
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void OverriddenVariableMountSourceRetainsEarlyValidationDiagnosticAndEffectiveBinding()
    {
        const string text = "FROM alpine AS build\nFROM scratch\n" +
            "RUN --mount=from=$IGNORED,from=build,target=/src true\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        RunInstruction run = Assert.Single(dockerfile.Items.OfType<RunInstruction>());
        KeyValueToken<KeywordToken, LiteralToken>[] entries = run.Mounts[0].Tokens
            .OfType<KeyValueToken<KeywordToken, LiteralToken>>().Where(entry => entry.Key == "from").ToArray();

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference[] references = analysis.References
            .Where(reference => reference.Kind == DockerfileReferenceKind.MountSource).ToArray();
        Assert.Equal(2, references.Length);
        DockerfileReference invalid = Assert.Single(references,
            reference => reference.Classification == DockerfileReferenceClassification.Invalid);
        Assert.Equal("$IGNORED", invalid.OriginalText);
        Assert.Same(run, invalid.Instruction);
        Assert.Same(entries[0].ValueToken, invalid.OperandToken);
        Assert.Null(invalid.TargetStage);
        AnalysisDiagnostic diagnostic = Assert.Single(invalid.Diagnostics,
            candidate => candidate.Code == AnalysisDiagnosticCode.UnsupportedVariableExpansion);
        Assert.Same(run, diagnostic.Instruction);
        Assert.Same(entries[0].ValueToken, diagnostic.OperandToken);
        Assert.Contains(diagnostic, analysis.Diagnostics);
        DockerfileReference effective = Assert.Single(analysis.Dependencies);
        Assert.Same(effective, Assert.Single(references,
            reference => reference.Classification == DockerfileReferenceClassification.Stage));
        Assert.Equal("build", effective.ResolvedValue);
        Assert.Same(entries[1].ValueToken, effective.OperandToken);
        Assert.Same(analysis.GetStage(0), effective.TargetStage);
        Assert.Same(analysis.GetStage(1), effective.SourceStage);
        Assert.Equal("alpine", Assert.Single(analysis.ExternalImages).ResolvedValue);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void MultipleMountFlagsKeepDistinctOperandIdentityAndRepeatedEdges()
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "FROM alpine AS build\nFROM scratch\n" +
            "RUN --mount=from=build,target=/one --mount=type=bind,from=build,target=/two " +
            "--mount=from=busybox,target=/three true\n");
        RunInstruction run = Assert.Single(dockerfile.Items.OfType<RunInstruction>());

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference[] mounts = analysis.References
            .Where(reference => reference.Kind == DockerfileReferenceKind.MountSource).ToArray();
        Assert.Equal(3, mounts.Length);
        Assert.Equal(2, analysis.Dependencies.Count);
        for (int i = 0; i < mounts.Length; i++)
        {
            Assert.Same(run, mounts[i].Instruction);
            Assert.Same(run.Mounts[i].Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>()
                .Single(entry => entry.Key == "from").ValueToken, mounts[i].OperandToken);
        }
        Assert.NotSame(mounts[0].OperandToken, mounts[1].OperandToken);
        Assert.Equal(new[] { "alpine", "busybox" }, analysis.ExternalImages.Select(reference => reference.ResolvedValue));
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("type=secret,from=build,target=/secret")]
    [InlineData("from=busybox,type=secret,target=/secret")]
    public void SecretMountWithFromIsInvalidAndNotAnEffectiveDependencyOrImage(string mount)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM alpine AS build\nFROM scratch\nRUN --mount={mount} true\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            candidate => candidate.Kind == DockerfileReferenceKind.MountSource);
        Assert.Equal(DockerfileReferenceClassification.Invalid, reference.Classification);
        Assert.DoesNotContain(reference, analysis.Dependencies);
        Assert.DoesNotContain(reference, analysis.ExternalImages);
        Assert.Contains(reference.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.InvalidMountSource);
    }

    [Theory]
    [InlineData("build", DockerfileReferenceClassification.Stage)]
    [InlineData("busybox", DockerfileReferenceClassification.ExternalImage)]
    public void SshMountFromParticipatesInSourceDiscovery(
        string source, DockerfileReferenceClassification classification)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM alpine AS build\nFROM scratch\nRUN --mount=type=ssh,from={source},target=/ssh true\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            candidate => candidate.Kind == DockerfileReferenceKind.MountSource);
        Assert.Equal(classification, reference.Classification);
        Assert.Equal(source, reference.ResolvedValue);
        if (classification == DockerfileReferenceClassification.Stage)
        {
            Assert.Same(analysis.GetStage(0), reference.TargetStage);
            Assert.Contains(reference, analysis.Dependencies);
        }
        else
        {
            Assert.Null(reference.TargetStage);
            Assert.Contains(reference, analysis.ExternalImages);
        }
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("COPY --from=busybox /app /app", DockerfileReferenceKind.CopySource)]
    [InlineData("RUN --mount=from=busybox,target=/src true", DockerfileReferenceKind.MountSource)]
    public void UnknownNamesFallBackToExternalImagesWithoutStageDiagnostics(
        string instruction, DockerfileReferenceKind kind)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse($"FROM scratch\n{instruction}\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.ExternalImages);
        Assert.Equal(kind, reference.Kind);
        Assert.Equal("busybox", reference.ResolvedValue);
        Assert.Null(reference.TargetStage);
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("COPY --from=$SOURCE /app /app")]
    [InlineData("COPY --from=${SOURCE} /app /app")]
    [InlineData("COPY --from=\\$SOURCE /app /app")]
    [InlineData("RUN --mount=from=$SOURCE,target=/src true")]
    [InlineData("RUN --mount=from=${SOURCE},target=/src true")]
    public void CopyAndMountSourceVariablesAreNotInterpolatedEvenWithOverrides(string instruction)
    {
        string text = $"ARG SOURCE=build\nFROM alpine AS build\nFROM scratch\nARG SOURCE\nENV SOURCE=build\n{instruction}\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        DockerfileAnalysis analysis = dockerfile.Analyze(new Dictionary<string, string?> { ["SOURCE"] = "build" });

        DockerfileReference reference = Assert.Single(analysis.References,
            candidate => candidate.Kind != DockerfileReferenceKind.BaseStage);
        Assert.Null(reference.TargetStage);
        Assert.DoesNotContain(reference, analysis.Dependencies);
        Assert.DoesNotContain(reference, analysis.ExternalImages);
        Assert.Contains(reference.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.UnsupportedVariableExpansion);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void ScratchImplicitContextsAndCommandTextDoNotInventExternalImages()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM scratch\nCOPY app /app\nADD https://example.com/app /app\n" +
            "RUN --mount=target=/src echo busybox\nCMD alpine\nENTRYPOINT busybox\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.References);
        Assert.Equal(DockerfileReferenceClassification.Scratch, reference.Classification);
        Assert.Empty(analysis.ExternalImages);
        Assert.Empty(analysis.Dependencies);
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("from=,target=/src")]
    [InlineData("from=build,from=,target=/src")]
    [InlineData("type=secret,from=,target=/src")]
    public void ExplicitEmptyMountSourceSelectsBuildContext(string mount)
    {
        string text = $"FROM alpine AS build\nFROM scratch\nRUN --mount={mount} true\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        RunInstruction run = Assert.Single(dockerfile.Items.OfType<RunInstruction>());

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            candidate => candidate.Kind == DockerfileReferenceKind.MountSource);
        Assert.Equal(DockerfileReferenceClassification.BuildContext, reference.Classification);
        Assert.Equal("", reference.OriginalText);
        Assert.Equal("", reference.ResolvedValue);
        Assert.Same(run.Mounts[0].Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>()
            .Last(entry => entry.Key == "from").ValueToken, reference.OperandToken);
        Assert.Null(reference.TargetStage);
        Assert.DoesNotContain(reference, analysis.ExternalImages);
        Assert.Empty(analysis.Dependencies);
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void ExplicitEmptyCopySourceSelectsBuildContext()
    {
        Dockerfile dockerfile = Dockerfile.Parse("FROM scratch\nCOPY --from=build /app /app\n");
        CopyInstruction copy = Assert.Single(dockerfile.Items.OfType<CopyInstruction>());
        copy.FromStageNameToken!.Value = "";
        string text = dockerfile.ToString();

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            candidate => candidate.Kind == DockerfileReferenceKind.CopySource);
        Assert.Equal(DockerfileReferenceClassification.BuildContext, reference.Classification);
        Assert.Equal("", reference.OriginalText);
        Assert.Equal("", reference.ResolvedValue);
        Assert.Same(copy.FromStageNameToken, reference.OperandToken);
        Assert.Null(reference.TargetStage);
        Assert.Empty(analysis.ExternalImages);
        Assert.Empty(analysis.Dependencies);
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void CapturedNamesTextAndBindingsRemainStableAfterModelEdits()
    {
        string text = "FROM alpine AS build\nFROM scratch AS final\nCOPY --from=build /app /app\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        FromInstruction from = dockerfile.Items.OfType<FromInstruction>().First();
        CopyInstruction copy = Assert.Single(dockerfile.Items.OfType<CopyInstruction>());
        Token operand = copy.FromStageNameToken!;

        DockerfileAnalysis before = dockerfile.Analyze();

        Assert.Equal(text, dockerfile.ToString());
        Assert.Same(from, before.GetStage(0).FromInstruction);
        Assert.Same(from, before.GetStage(0).Source.FromInstruction);
        Assert.Same(copy, Assert.Single(before.GetStage(1).Source.Items.OfType<CopyInstruction>()));
        DockerfileReference dependency = Assert.Single(before.Dependencies);
        Assert.Same(operand, dependency.OperandToken);
        Assert.Same(dependency, Assert.Single(before.References,
            reference => reference.Kind == DockerfileReferenceKind.CopySource));

        from.StageName = "renamed";
        from.ImageName = "busybox";
        copy.FromStageName = "busybox";

        Assert.Equal("build", before.GetStage(0).Name);
        Assert.Same(before.GetStage(0), before.GetStage("build"));
        Assert.Empty(before.FindStages("renamed"));
        Assert.Equal("build", dependency.OriginalText);
        Assert.Equal("build", dependency.ResolvedValue);
        Assert.Same(before.GetStage(0), dependency.TargetStage);
        Assert.Equal("alpine", Assert.Single(before.ExternalImages).ResolvedValue);
        Assert.Equal("busybox", copy.FromStageName);
        Assert.Empty(before.Diagnostics);

        DockerfileAnalysis after = dockerfile.Analyze();
        Assert.Equal("renamed", after.GetStage(0).Name);
        Assert.Empty(after.FindStages("build"));
        Assert.Empty(after.Dependencies);
        Assert.Equal(new[] { "busybox", "busybox" }, after.ExternalImages.Select(reference => reference.ResolvedValue));
        Assert.NotSame(before.GetStage(0), after.GetStage(0));
        Assert.Same(from, after.GetStage(0).FromInstruction);
    }

    [Fact]
    public void OverridesAndTheirLaterChangesDoNotMutateSnapshotsOrSource()
    {
        const string text = "ARG BASE=alpine\nFROM $BASE AS build\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        var overrides = new Dictionary<string, string?> { ["BASE"] = "busybox" };

        DockerfileAnalysis before = dockerfile.Analyze(overrides);

        Assert.Equal("busybox", overrides["BASE"]);
        Assert.Equal(text, dockerfile.ToString());
        overrides["BASE"] = "debian";
        Assert.Equal("busybox", Assert.Single(before.ExternalImages).ResolvedValue);
        Assert.Equal("$BASE", Assert.Single(before.ExternalImages).OriginalText);
        Assert.Equal("debian", Assert.Single(dockerfile.Analyze(overrides).ExternalImages).ResolvedValue);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void DiagnosticSnapshotsRetainOriginalAttributionAfterAnEditFixesTheProblem()
    {
        Dockerfile dockerfile = Dockerfile.Parse("FROM alpine AS build\nCOPY --from=build /app /app\n");
        CopyInstruction copy = Assert.Single(dockerfile.Items.OfType<CopyInstruction>());

        DockerfileAnalysis before = dockerfile.Analyze();

        DockerfileReference dependency = Assert.Single(before.Dependencies);
        AnalysisDiagnostic diagnostic = Assert.Single(dependency.Diagnostics,
            candidate => candidate.Code == AnalysisDiagnosticCode.CircularStageDependency);
        Assert.Same(copy, diagnostic.Instruction);
        Assert.Same(copy.FromStageNameToken, diagnostic.OperandToken);
        Assert.Same(before.GetStage(0), diagnostic.Stage);
        Assert.Contains(0, diagnostic.RelatedStageIndices);
        Assert.Contains(diagnostic, before.Diagnostics);
        string message = diagnostic.Message;

        copy.FromStageName = "busybox";
        dockerfile.Items.Add(new FromInstruction("debian", "added"));

        Assert.Single(before.Stages);
        Assert.Same(dependency, Assert.Single(before.Dependencies));
        Assert.Equal("build", dependency.OriginalText);
        Assert.Equal(message, diagnostic.Message);
        Assert.Contains(diagnostic, before.Diagnostics);
        DockerfileAnalysis after = dockerfile.Analyze();
        Assert.Equal(2, after.Stages.Count);
        Assert.Empty(after.Dependencies);
        Assert.Empty(after.Diagnostics);
    }

    [Fact]
    public void AllReportCollectionsAndDiagnosticDetailsRejectMutation()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "ARG BASE\nFROM alpine AS build\nCOPY --from=build /app /app\n" +
            "FROM $BASE AS BUILD\n").Analyze();

        AssertReadOnly(analysis.Stages);
        AssertReadOnly(analysis.References);
        AssertReadOnly(analysis.Dependencies);
        AssertReadOnly(analysis.ExternalImages);
        AssertReadOnly(analysis.UnresolvedReferences);
        AssertReadOnly(analysis.Diagnostics);
        AssertReadOnly(analysis.FindStages("build"));
        AssertReadOnly(analysis.FindStages("missing"));
        Assert.All(analysis.References, reference => AssertReadOnly(reference.Diagnostics));
        Assert.All(analysis.Diagnostics, diagnostic =>
        {
            AssertReadOnly(diagnostic.RelatedStageIndices);
            AssertReadOnly(diagnostic.VariableNames);
        });
    }

    [Fact]
    public void StageLookupsValidateIndicesNamesAndAmbiguity()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine\nFROM busybox AS build\nFROM scratch AS BUILD\n").Analyze();

        Assert.Null(analysis.GetStage(0).Name);
        Assert.Equal(new[] { 0, 1, 2 }, analysis.Stages.Select(stage => stage.Index));
        Assert.Equal(new[] { 1, 2 }, analysis.FindStages("BuIlD").Select(stage => stage.Index));
        Assert.Throws<ArgumentOutOfRangeException>(() => analysis.GetStage(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => analysis.GetStage(3));
        Assert.Throws<ArgumentNullException>(() => analysis.GetStage(null!));
        Assert.Throws<ArgumentNullException>(() => analysis.FindStages(null!));
        Assert.Throws<KeyNotFoundException>(() => analysis.GetStage("missing"));
        Assert.Throws<InvalidOperationException>(() => analysis.GetStage("BUILD"));
        Assert.Empty(analysis.FindStages("missing"));
    }

    [Theory]
    [InlineData("RUN echo invalid")]
    [InlineData("COPY --from=busybox /app /app")]
    [InlineData("ENV BASE=alpine")]
    [InlineData("ONBUILD COPY --from=busybox /app /app")]
    public void InstructionsBeforeFromAreDiagnosedWithOriginalInstruction(string instruction)
    {
        Dockerfile dockerfile = Dockerfile.Parse($"# comment\nARG BASE=alpine\n{instruction}\nFROM $BASE\n");
        Instruction invalid = dockerfile.Items.OfType<Instruction>().First(item => item is not ArgInstruction);

        DockerfileAnalysis analysis = dockerfile.Analyze();

        AnalysisDiagnostic diagnostic = Assert.Single(analysis.Diagnostics,
            candidate => candidate.Code == AnalysisDiagnosticCode.InstructionBeforeFrom);
        Assert.Same(invalid, diagnostic.Instruction);
        Assert.Equal(AnalysisDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Single(analysis.Stages);
        Assert.Equal("alpine", Assert.Single(analysis.ExternalImages).ResolvedValue);
    }

    [Fact]
    public void MultipleInvalidPreambleInstructionsAccumulateWithoutAnyFrom()
    {
        Dockerfile dockerfile = Dockerfile.Parse("ARG BASE=alpine\nRUN echo invalid\nCOPY --from=busybox /app /app\n");
        Instruction[] invalid = dockerfile.Items.OfType<Instruction>()
            .Where(instruction => instruction is not ArgInstruction).ToArray();

        DockerfileAnalysis analysis = dockerfile.Analyze();

        Assert.Empty(analysis.Stages);
        Assert.Empty(analysis.References);
        Assert.Equal(2, analysis.Diagnostics.Count);
        Assert.Equal(invalid, analysis.Diagnostics.Select(diagnostic => diagnostic.Instruction));
        Assert.All(analysis.Diagnostics, diagnostic =>
        {
            Assert.Equal(AnalysisDiagnosticCode.InstructionBeforeFrom, diagnostic.Code);
            Assert.Equal(AnalysisDiagnosticSeverity.Error, diagnostic.Severity);
        });
    }

    private static void AssertReadOnly<T>(IReadOnlyList<T> values)
    {
        ICollection<T> collection = Assert.IsAssignableFrom<ICollection<T>>(values);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(default!));
        if (values.Count > 0)
        {
            IList<T> list = Assert.IsAssignableFrom<IList<T>>(values);
            Assert.Throws<NotSupportedException>(() => list[0] = default!);
        }
    }
}
