namespace Valleysoft.DockerfileModel.Tests;

public class DockerfileAnalysisOnBuildTests
{
    [Theory]
    [InlineData("COPY --from=to\\ols /tool /tool", DockerfileReferenceKind.CopySource)]
    [InlineData("COPY --from=to\\ols  /tool /tool", DockerfileReferenceKind.CopySource)]
    [InlineData("COPY --from='to\\ols' /tool /tool", DockerfileReferenceKind.CopySource)]
    [InlineData("COPY --from=to`\nols /tool /tool", DockerfileReferenceKind.CopySource)]
    [InlineData("RUN --mount=from=to\\ols,target=/src true", DockerfileReferenceKind.MountSource)]
    [InlineData("RUN --mount=from=to\\ols,target=/src\t\ttrue", DockerfileReferenceKind.MountSource)]
    [InlineData("RUN --mount=type=b\\ind,from=to\\ols,target=/src true", DockerfileReferenceKind.MountSource)]
    [InlineData("RUN --mount=source=.\\,from=tools,target=/src true", DockerfileReferenceKind.MountSource)]
    public void InheritedSelectorsUseDefaultBuilderEscapes(string trigger, DockerfileReferenceKind kind)
    {
        string text = "# escape=`\nFROM scratch AS tools\nFROM scratch AS parent\n" +
            $"ONBUILD {trigger}\nFROM parent AS child\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        OnBuildInstruction declaration = Assert.Single(dockerfile.Items.OfType<OnBuildInstruction>());

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference deferred = Assert.Single(analysis.References,
            reference => reference.Phase == DockerfileReferencePhase.DeferredOnBuild);
        DockerfileReference inherited = Assert.Single(analysis.Dependencies,
            reference => reference.Kind == kind);
        Assert.Equal(DockerfileReferenceClassification.Deferred, deferred.Classification);
        Assert.Equal(DockerfileReferencePhase.InheritedOnBuild, inherited.Phase);
        Assert.Equal("tools", inherited.ResolvedValue);
        Assert.Same(analysis.GetStage("tools"), inherited.TargetStage);
        Assert.Same(analysis.GetStage("child"), inherited.SourceStage);
        Assert.Same(analysis.GetStage("parent"), inherited.DeclaringStage);
        Assert.Same(declaration, inherited.OnBuildInstruction);
        Assert.Same(declaration.Instruction, inherited.Instruction);
        Assert.Same(deferred.OperandToken, inherited.OperandToken);
        Assert.Equal(deferred.OriginalText, inherited.OriginalText);
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Theory]
    [InlineData("COPY --from=to\\ols /tool /tool")]
    [InlineData("RUN --mount=from=to\\ols,target=/src true")]
    public void ImmediateSelectorsRetainCustomBuilderEscapes(string instruction)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"# escape=`\nFROM scratch AS tools\nFROM scratch AS child\n{instruction}\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            reference => reference.Kind != DockerfileReferenceKind.BaseStage);
        Assert.Equal(DockerfileReferencePhase.Immediate, reference.Phase);
        Assert.Equal(DockerfileReferenceClassification.Invalid, reference.Classification);
        Assert.Equal("to\\ols", reference.ResolvedValue);
        Assert.Equal(AnalysisDiagnosticCode.InvalidReference, Assert.Single(reference.Diagnostics).Code);
        Assert.Empty(analysis.Dependencies);
    }

    [Theory]
    [InlineData("b\\ind", DockerfileReferenceClassification.Stage)]
    [InlineData("'\\$KIND'", DockerfileReferenceClassification.Stage)]
    [InlineData("b`ind", DockerfileReferenceClassification.Stage)]
    [InlineData("b\\`ind", DockerfileReferenceClassification.Stage)]
    [InlineData("s\\ecret", DockerfileReferenceClassification.Invalid)]
    [InlineData("'s\\ecret'", DockerfileReferenceClassification.Invalid)]
    public void InheritedMountTypesSeparateBuilderAndExpressionEscapes(
        string type, DockerfileReferenceClassification expected)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "# escape=`\nFROM scratch AS tools\nFROM scratch AS parent\nARG KIND=bind\n" +
            $"ONBUILD RUN --mount=type={type},from=tools,target=/src true\nFROM parent AS child\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            reference => reference.Phase == DockerfileReferencePhase.InheritedOnBuild);
        Assert.Equal(expected, reference.Classification);
        if (expected == DockerfileReferenceClassification.Stage)
        {
            Assert.Same(analysis.GetStage("tools"), reference.TargetStage);
            Assert.Empty(analysis.Diagnostics);
        }
        else
        {
            Assert.Null(reference.TargetStage);
            Assert.Equal(AnalysisDiagnosticCode.InvalidMountSource, Assert.Single(reference.Diagnostics).Code);
        }
    }

    [Fact]
    public void InheritedCopyExpansionGuardRetainsCustomExpressionEscape()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "# escape=`\nFROM scratch AS tools\nFROM scratch AS parent\n" +
            "ONBUILD COPY --from=to`ols /tool /tool\nFROM parent AS child\n").Analyze();

        DockerfileReference reference = Assert.Single(analysis.References,
            reference => reference.Phase == DockerfileReferencePhase.InheritedOnBuild);
        Assert.Equal(DockerfileReferenceClassification.Invalid, reference.Classification);
        Assert.Equal(AnalysisDiagnosticCode.UnsupportedVariableExpansion, Assert.Single(reference.Diagnostics).Code);
        Assert.Null(reference.TargetStage);
    }

    [Theory]
    [InlineData("COPY <<EOF /tool\n--from=tools\nEOF", false)]
    [InlineData("RUN <<EOF\n--mount=from=tools true\nEOF", false)]
    [InlineData("RUN --mount=from=to\\ols,target=/src <<EOF\ntrue\nEOF", true)]
    public void ReparsedHeredocsPreserveFlagReferencesWithoutInspectingBodies(string trigger, bool hasMount)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "# escape=`\nFROM scratch AS tools\nFROM scratch AS parent\nONBUILD RUN true\nFROM parent AS child\n");
        // Whole-Dockerfile parsing does not collect ONBUILD heredoc bodies.
        Assert.Single(dockerfile.Items.OfType<OnBuildInstruction>()).Instruction =
            Instruction.CreateInstruction(trigger + "\n", '`');
        string text = dockerfile.ToString();

        DockerfileAnalysis analysis = dockerfile.Analyze();

        if (hasMount)
        {
            DockerfileReference reference = Assert.Single(analysis.Dependencies,
                reference => reference.Kind == DockerfileReferenceKind.MountSource);
            Assert.Same(analysis.GetStage("tools"), reference.TargetStage);
            Assert.Equal("to\\ols", reference.OriginalText);
        }
        else
        {
            Assert.All(analysis.References, reference => Assert.Equal(DockerfileReferenceKind.BaseStage, reference.Kind));
        }
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Theory]
    [InlineData("COPY --from=tools\\ /tool /tool")]
    [InlineData("COPY --from=tools\\  /tool /tool")]
    [InlineData("COPY --from=tools\\   /tool /tool")]
    [InlineData("COPY --from=tools\\\t /tool /tool")]
    [InlineData("COPY --chown=user\\ --from=tools /tool /tool")]
    [InlineData("RUN --mount=from=tools\\  true")]
    [InlineData("RUN --mount=from=tools\\   true")]
    [InlineData("RUN --mount=from=tools\\\t true")]
    [InlineData("RUN --mount=from=tools\\ --mount=from=tools,target=/src true")]
    [InlineData("RUN --network=none\\ --mount=from=tools,target=/src true")]
    [InlineData("RUN --mount=fr\\om=to\\ols,target=/src true")]
    [InlineData("RUN --mount=ty\\pe=b\\ind,fr\\om=to\\ols,target=/src true")]
    public void UnmappedInheritedFlagBoundariesDoNotProduceGuessedDependencies(string trigger)
    {
        string text = "# escape=`\nFROM scratch AS tools\nFROM scratch AS parent\n" +
            $"ONBUILD {trigger}\nFROM parent AS child\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        OnBuildInstruction declaration = Assert.Single(dockerfile.Items.OfType<OnBuildInstruction>());

        DockerfileAnalysis analysis = dockerfile.Analyze();

        Assert.Equal(DockerfileReferenceKind.BaseStage, Assert.Single(analysis.Dependencies).Kind);
        DockerfileReference reference = Assert.Single(analysis.UnresolvedReferences);
        Assert.Equal(DockerfileReferencePhase.InheritedOnBuild, reference.Phase);
        Assert.Equal(AnalysisDiagnosticCode.UnsupportedEvaluation, Assert.Single(reference.Diagnostics).Code);
        Assert.Same(declaration, reference.OnBuildInstruction);
        Assert.Same(declaration.Instruction, reference.Instruction);
        Assert.Same(analysis.GetStage("parent"), reference.DeclaringStage);
        Assert.Same(analysis.GetStage("child"), reference.SourceStage);
        Assert.Null(reference.TargetStage);
        Assert.Null(reference.ResolvedValue);
        Assert.Same(declaration.Instruction, reference.OperandToken);
        DockerfileReference deferred = Assert.Single(analysis.References,
            candidate => candidate.Phase == DockerfileReferencePhase.DeferredOnBuild);
        Assert.Same(deferred.OperandToken, reference.OperandToken);
        Assert.Equal(DockerfileReferenceClassification.Deferred, deferred.Classification);
        Assert.Empty(deferred.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void ReferenceDeclarationsRemainDeferredWithoutLocalConsumers()
    {
        const string text = "FROM alpine AS parent\n" +
            "ONBUILD COPY --from=tools /tool /tool\n" +
            "ONBUILD RUN --mount=from=busybox,target=/bin true\nFROM scratch AS tools\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        OnBuildInstruction[] declarations = dockerfile.Items.OfType<OnBuildInstruction>().ToArray();

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference[] references = analysis.References
            .Where(reference => reference.Phase == DockerfileReferencePhase.DeferredOnBuild).ToArray();
        Assert.Equal(2, references.Length);
        Assert.Equal(new[] { DockerfileReferenceKind.CopySource, DockerfileReferenceKind.MountSource },
            references.Select(reference => reference.Kind));
        for (int i = 0; i < references.Length; i++)
        {
            Assert.Equal(DockerfileReferenceClassification.Deferred, references[i].Classification);
            Assert.Null(references[i].ResolvedValue);
            Assert.Null(references[i].SourceStage);
            Assert.Null(references[i].TargetStage);
            Assert.Same(analysis.GetStage(0), references[i].DeclaringStage);
            Assert.Same(declarations[i], references[i].OnBuildInstruction);
            Assert.Same(declarations[i].Instruction, references[i].Instruction);
        }
        Assert.Empty(analysis.Dependencies);
        Assert.Equal("alpine", Assert.Single(analysis.ExternalImages).ResolvedValue);
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void CopyAndMountTriggersRunForEachChildWithOriginalDeclarationProvenance()
    {
        const string text = "FROM busybox AS tools\nFROM alpine AS parent\n" +
            "ONBUILD COPY --from=TOOLS /tool /tool\n" +
            "ONBUILD RUN --mount=from=tools,target=/one --mount=from=debian,target=/two true\n" +
            "FROM parent AS first\nFROM parent AS second\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        DockerfileAnalysis analysis = dockerfile.Analyze();

        DockerfileReference[] deferred = analysis.References
            .Where(reference => reference.Phase == DockerfileReferencePhase.DeferredOnBuild).ToArray();
        DockerfileReference[] inherited = analysis.References
            .Where(reference => reference.Phase == DockerfileReferencePhase.InheritedOnBuild).ToArray();
        Assert.Equal(3, deferred.Length);
        Assert.Equal(6, inherited.Length);
        foreach (int childIndex in new[] { 2, 3 })
        {
            DockerfileReference[] childReferences = inherited
                .Where(reference => reference.SourceStage!.Index == childIndex).ToArray();
            Assert.Equal(3, childReferences.Length);
            for (int i = 0; i < deferred.Length; i++)
            {
                Assert.Same(deferred[i].Instruction, childReferences[i].Instruction);
                Assert.Same(deferred[i].OperandToken, childReferences[i].OperandToken);
                Assert.Same(deferred[i].OnBuildInstruction, childReferences[i].OnBuildInstruction);
                Assert.Same(analysis.GetStage(1), childReferences[i].DeclaringStage);
                Assert.Same(analysis.GetStage(childIndex), childReferences[i].SourceStage);
                Assert.Equal(deferred[i].OriginalText, childReferences[i].OriginalText);
            }
            Assert.Same(analysis.GetStage(0), childReferences[0].TargetStage);
            Assert.Same(analysis.GetStage(0), childReferences[1].TargetStage);
            Assert.Equal(DockerfileReferenceClassification.ExternalImage, childReferences[2].Classification);
            Assert.Equal("debian", childReferences[2].ResolvedValue);
        }
        Assert.Equal(6, analysis.Dependencies.Count);
        Assert.Equal(new[] { "busybox", "alpine", "debian", "debian" },
            analysis.ExternalImages.Select(reference => reference.ResolvedValue));
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void ConsumedTriggersDoNotReplayButChildDeclarationsPropagateToGrandchild()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM busybox AS tools\nFROM alpine AS parent\nONBUILD COPY --from=tools /one /one\n" +
            "FROM parent AS child\nONBUILD RUN --mount=from=tools,target=/two true\n" +
            "FROM child AS grandchild\nFROM grandchild AS greatgrandchild\n").Analyze();

        DockerfileReference[] inherited = analysis.References
            .Where(reference => reference.Phase == DockerfileReferencePhase.InheritedOnBuild).ToArray();
        Assert.Equal(2, inherited.Length);
        DockerfileReference copy = Assert.Single(inherited, reference => reference.Kind == DockerfileReferenceKind.CopySource);
        DockerfileReference mount = Assert.Single(inherited, reference => reference.Kind == DockerfileReferenceKind.MountSource);
        Assert.Same(analysis.GetStage(1), copy.DeclaringStage);
        Assert.Same(analysis.GetStage(2), copy.SourceStage);
        Assert.Same(analysis.GetStage(2), mount.DeclaringStage);
        Assert.Same(analysis.GetStage(3), mount.SourceStage);
        Assert.DoesNotContain(inherited, reference => reference.SourceStage!.Index == 4);
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("COPY --from=child /app /app", DockerfileReferenceKind.CopySource)]
    [InlineData("RUN --mount=from=child,target=/src true", DockerfileReferenceKind.MountSource)]
    public void InheritedSelfReferencesBelongToChildAndAreCyclic(
        string trigger, DockerfileReferenceKind kind)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM alpine AS parent\nONBUILD {trigger}\nFROM parent AS child\n").Analyze();

        DockerfileReference inherited = Assert.Single(analysis.References,
            reference => reference.Phase == DockerfileReferencePhase.InheritedOnBuild);
        Assert.Equal(kind, inherited.Kind);
        Assert.Same(analysis.GetStage(1), inherited.SourceStage);
        Assert.Same(inherited.SourceStage, inherited.TargetStage);
        Assert.Contains(inherited.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.CircularStageDependency);
        DockerfileReference deferred = Assert.Single(analysis.References,
            reference => reference.Phase == DockerfileReferencePhase.DeferredOnBuild);
        Assert.Null(deferred.TargetStage);
        Assert.Empty(deferred.Diagnostics);
    }

    [Theory]
    [InlineData("COPY --from=later /app /app")]
    [InlineData("RUN --mount=from=later,target=/src true")]
    public void InheritedForwardReferenceCanFormCycleWithLaterBase(string trigger)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            $"FROM alpine AS parent\nONBUILD {trigger}\nFROM parent AS child\nFROM child AS later\n").Analyze();

        DockerfileReference inherited = Assert.Single(analysis.References,
            reference => reference.Phase == DockerfileReferencePhase.InheritedOnBuild);
        Assert.Same(analysis.GetStage(1), inherited.SourceStage);
        Assert.Same(analysis.GetStage(2), inherited.TargetStage);
        Assert.Contains(inherited.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.ForwardStageReference);
        Assert.Contains(inherited.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.CircularStageDependency);
        DockerfileReference laterBase = Assert.Single(analysis.Dependencies,
            reference => reference.Kind == DockerfileReferenceKind.BaseStage && reference.SourceStage!.Index == 2);
        Assert.Contains(laterBase.Diagnostics, diagnostic => diagnostic.Code == AnalysisDiagnosticCode.CircularStageDependency);
        Assert.Same(inherited.Instruction, Assert.Single(inherited.Diagnostics,
            diagnostic => diagnostic.Code == AnalysisDiagnosticCode.ForwardStageReference).Instruction);
    }

    [Fact]
    public void TriggerReferencingDeclaringStageDoesNotCreateAFalseParentSelfCycle()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine AS parent\nONBUILD COPY --from=parent /app /app\nFROM parent AS child\n").Analyze();

        DockerfileReference inherited = Assert.Single(analysis.References,
            reference => reference.Phase == DockerfileReferencePhase.InheritedOnBuild);
        Assert.Same(analysis.GetStage(1), inherited.SourceStage);
        Assert.Same(analysis.GetStage(0), inherited.TargetStage);
        Assert.Equal(2, analysis.Dependencies.Count);
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void ExternalBaseDoesNotInventUnknownImageMetadataTriggers()
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "FROM alpine AS parent\nONBUILD COPY --from=tools /tool /tool\n" +
            "FROM busybox AS external\n").Analyze();

        Assert.DoesNotContain(analysis.References,
            reference => reference.Phase == DockerfileReferencePhase.InheritedOnBuild);
        Assert.Single(analysis.References, reference => reference.Phase == DockerfileReferencePhase.DeferredOnBuild);
        Assert.Equal(new[] { "alpine", "busybox" }, analysis.ExternalImages.Select(reference => reference.ResolvedValue));
        Assert.Empty(analysis.Dependencies);
        Assert.Empty(analysis.Diagnostics);
    }

    [Theory]
    [InlineData("COPY --from=$SOURCE /app /app")]
    [InlineData("RUN --mount=from=$SOURCE,target=/src true")]
    public void InheritedSourceSelectorsStillDoNotInterpolateVariables(string trigger)
    {
        DockerfileAnalysis analysis = Dockerfile.Parse(
            "ARG SOURCE=tools\nFROM busybox AS tools\nFROM alpine AS parent\n" +
            $"ONBUILD {trigger}\nFROM parent AS child\nARG SOURCE\n").Analyze(
                new Dictionary<string, string?> { ["SOURCE"] = "tools" });

        DockerfileReference inherited = Assert.Single(analysis.References,
            reference => reference.Phase == DockerfileReferencePhase.InheritedOnBuild);
        Assert.Same(analysis.GetStage(2), inherited.SourceStage);
        Assert.Null(inherited.TargetStage);
        Assert.DoesNotContain(inherited, analysis.ExternalImages);
        Assert.Contains(inherited.Diagnostics, diagnostic =>
            diagnostic.Code == AnalysisDiagnosticCode.UnsupportedVariableExpansion);
    }
}
