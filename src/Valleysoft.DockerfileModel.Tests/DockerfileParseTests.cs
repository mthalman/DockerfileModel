namespace Valleysoft.DockerfileModel.Tests;

public class DockerfileParseTests
{
    [Fact]
    public void DefaultsAreStrictAndRejectUnknownInstructions()
    {
        DockerfileParseOptions options = new();
        Assert.Equal(DockerfileParseMode.Strict, options.Mode);
        Assert.Equal(UnknownInstructionBehavior.Error, options.UnknownInstructionBehavior);
        DockerfileParseResult result = Dockerfile.TryParse("FUTURE-COPY a b\nRUN echo after");
        Assert.False(result.Success);
        Assert.Null(result.Dockerfile);
        Assert.Equal("DFP002", Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData(DockerfileParseMode.Strict, UnknownInstructionBehavior.Error)]
    [InlineData(DockerfileParseMode.Recover, UnknownInstructionBehavior.Error)]
    [InlineData(DockerfileParseMode.Strict, UnknownInstructionBehavior.Preserve)]
    [InlineData(DockerfileParseMode.Recover, UnknownInstructionBehavior.Preserve)]
    public void UnknownInstructionModeMatrix(DockerfileParseMode mode, UnknownInstructionBehavior behavior)
    {
        const string text = "FROM scratch\nFUTURE-COPY $src 'unfinished\nRUN echo after";
        DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions
        {
            Mode = mode,
            UnknownInstructionBehavior = behavior
        });
        bool preserved = behavior == UnknownInstructionBehavior.Preserve;
        Assert.Equal(preserved, result.Success);
        DockerfileDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("DFP002", diagnostic.Code);
        Assert.Equal(preserved ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        Assert.Equal("FUTURE-COPY", text.Substring(diagnostic.SourceSpan.Start.Offset, diagnostic.SourceSpan.Length));
        if (!preserved && mode == DockerfileParseMode.Strict)
        {
            Assert.Null(result.Dockerfile);
            return;
        }

        Dockerfile model = Assert.IsType<Dockerfile>(result.Dockerfile);
        Assert.Equal(text, model.ToString());
        Assert.IsType<FromInstruction>(model.Items[0]);
        Assert.IsType<RunInstruction>(model.Items[2]);
        if (preserved)
        {
            Assert.IsAssignableFrom<GenericInstruction>(Assert.IsType<UnknownInstruction>(model.Items[1]));
        }
        else
        {
            Assert.IsType<MalformedConstruct>(model.Items[1]);
            Assert.Equal(ConstructType.Malformed, model.Items[1].Type);
            Assert.False(model.Items[1] is Instruction);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("\r\n\n \t\r\n")]
    [InlineData("# comment without final newline")]
    public void EmptyAndTriviaInputSucceedsInBothModes(string text)
    {
        foreach (DockerfileParseMode mode in Enum.GetValues<DockerfileParseMode>())
        {
            DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions { Mode = mode });
            Assert.True(result.Success);
            Assert.Empty(result.Diagnostics);
            Assert.Equal(text, Assert.IsType<Dockerfile>(result.Dockerfile).ToString());
        }
    }

    [Fact]
    public void NullAndInvalidOptionsAreProgrammerErrors()
    {
        Assert.Throws<ArgumentNullException>(() => Dockerfile.TryParse(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Dockerfile.TryParse("", new DockerfileParseOptions
        {
            Mode = (DockerfileParseMode)42
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Dockerfile.TryParse("", new DockerfileParseOptions
        {
            UnknownInstructionBehavior = (UnknownInstructionBehavior)42
        }));
        Assert.True(Dockerfile.TryParse("FROM scratch", null).Success);
    }

    [Theory]
    [InlineData("RUNNING echo text", "RUNNING")]
    [InlineData("FROMAGE scratch", "FROMAGE")]
    [InlineData("future-copy $src $dest", "future-copy")]
    public void UnknownKeywordsAreWholeIdentifiersAndOpaque(string text, string keyword)
    {
        DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions
        {
            UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
        });
        Assert.True(result.Success);
        Assert.Equal(text, Assert.IsType<UnknownInstruction>(Assert.Single(result.Dockerfile!.Items)).ToString());
        SourceSpan span = Assert.Single(result.Diagnostics).SourceSpan;
        Assert.Equal(keyword, text.Substring(span.Start.Offset, span.Length));
    }

    [Fact]
    public void StandaloneGenericParserStillRejectsUnknownName()
    {
        Assert.Throws<ParseException>(() => GenericInstruction.Parse("FUTURE-COPY $src $dest"));
    }

    [Theory]
    [InlineData("FROM\n")]
    [InlineData("ARG\n")]
    [InlineData("SHELL [\"unterminated\n")]
    [InlineData("FROM scratch extra tokens\n")]
    public void KnownSyntaxFailuresAreNeverPreservedAsUnknown(string malformed)
    {
        string text = malformed + "FROM scratch\nRUN echo after\n";
        DockerfileParseResult result = Dockerfile.TryParse(text, RecoverPreservingUnknown());
        Assert.False(result.Success);
        Assert.Equal("DFP001", Assert.Single(result.Diagnostics).Code);
        Dockerfile model = Assert.IsType<Dockerfile>(result.Dockerfile);
        Assert.Equal(text, model.ToString());
        Assert.IsType<MalformedConstruct>(model.Items[0]);
        Assert.IsType<FromInstruction>(model.Items[1]);
        Assert.IsType<RunInstruction>(model.Items[2]);
        Assert.Empty(model.Items.OfType<UnknownInstruction>());
    }

    [Theory]
    [InlineData("RUN")]
    [InlineData("CMD")]
    [InlineData("ENTRYPOINT")]
    [InlineData("HEALTHCHECK CMD")]
    public void BareCommandsProduceSyntaxDiagnosticsRatherThanArgumentExceptions(string command)
    {
        foreach (string ending in new[] { "", " ", "\n", "\r\n" })
        {
            string malformed = command + ending;
            DockerfileParseResult strict = Dockerfile.TryParse(malformed);
            Assert.False(strict.Success);
            Assert.Null(strict.Dockerfile);
            DockerfileDiagnostic diagnostic = Assert.Single(strict.Diagnostics);
            Assert.Equal("DFP001", diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

            DockerfileParseResult recovered = Dockerfile.TryParse(malformed, RecoverPreservingUnknown());
            Assert.False(recovered.Success);
            Assert.Equal("DFP001", Assert.Single(recovered.Diagnostics).Code);
            Assert.IsType<MalformedConstruct>(Assert.Single(recovered.Dockerfile!.Items));
            SourceSpanTests.AssertPartition(malformed, recovered.Dockerfile);
        }

        string text = command + "\nFROM scratch\nRUN echo after\n";
        DockerfileParseResult result = Dockerfile.TryParse(text, RecoverPreservingUnknown());
        Assert.False(result.Success);
        Assert.Equal("DFP001", Assert.Single(result.Diagnostics).Code);
        Assert.IsType<MalformedConstruct>(result.Dockerfile!.Items[0]);
        Assert.IsType<FromInstruction>(result.Dockerfile.Items[1]);
        Assert.IsType<RunInstruction>(result.Dockerfile.Items[2]);
        SourceSpanTests.AssertPartition(text, result.Dockerfile);
    }

    [Fact]
    public void StrictStopsAtFirstErrorAndRetainsEarlierWarnings()
    {
        const string text = "FUTURE a\nFROM\nARG\nRUN echo after\n";
        DockerfileParseResult strict = Dockerfile.TryParse(text, new DockerfileParseOptions
        {
            UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
        });
        Assert.False(strict.Success);
        Assert.Null(strict.Dockerfile);
        Assert.Equal(new[] { "DFP002", "DFP001" }, strict.Diagnostics.Select(d => d.Code));
        Assert.Equal(new[] { DiagnosticSeverity.Warning, DiagnosticSeverity.Error }, strict.Diagnostics.Select(d => d.Severity));

        DockerfileParseResult recovered = Dockerfile.TryParse(text, RecoverPreservingUnknown());
        Assert.False(recovered.Success);
        Assert.Equal(3, recovered.Diagnostics.Count);
        Assert.Equal(2, recovered.Dockerfile!.Items.OfType<MalformedConstruct>().Count());
        Assert.Single(recovered.Dockerfile.Items.OfType<RunInstruction>());
        Assert.Equal(text, recovered.Dockerfile.ToString());
    }

    [Theory]
    [InlineData("# escape=\n")]
    [InlineData("# escape= \r\n")]
    public void EmptyDirectiveReportsDiagnosticWithoutChangingEscape(string directive)
    {
        string text = directive + "FROM \\\nscratch\nRUN echo after\n";
        DockerfileParseResult strict = Dockerfile.TryParse(text);
        Assert.Null(strict.Dockerfile);
        Assert.Equal("DFP004", Assert.Single(strict.Diagnostics).Code);

        DockerfileParseResult result = Dockerfile.TryParse(text, RecoverPreservingUnknown());
        Assert.False(result.Success);
        Assert.Equal("DFP004", Assert.Single(result.Diagnostics).Code);
        Assert.Equal('\\', result.Dockerfile!.EscapeChar);
        Assert.IsType<MalformedConstruct>(result.Dockerfile.Items[0]);
        Assert.Single(result.Dockerfile.Items.OfType<FromInstruction>());
        Assert.Single(result.Dockerfile.Items.OfType<RunInstruction>());
        Assert.Equal(text, result.Dockerfile.ToString());
    }

    [Theory]
    [InlineData("\\", "")]
    [InlineData("`", "# escape=`\r\n")]
    public void ContinuedUnknownAndMalformedRegionsKeepCommentsAndBoundaries(string escape, string directive)
    {
        string unknown = $"FUTURE-COPY a {escape}\r\n# embedded comment\r\n\r\n b\r\n";
        string malformed = $"FR{escape}\r\nOM {escape}\r\n# continued comment\r\n\r\n scratch extra tokens\r\n";
        string text = directive + unknown + malformed + "RUN echo after\r\n";
        DockerfileParseResult result = Dockerfile.TryParse(text, RecoverPreservingUnknown());
        Assert.False(result.Success);
        Assert.Equal(text, result.Dockerfile!.ToString());
        Assert.Equal(unknown, Assert.Single(result.Dockerfile.Items.OfType<UnknownInstruction>()).ToString());
        Assert.Single(result.Dockerfile.Items.OfType<MalformedConstruct>());
        Assert.Single(result.Dockerfile.Items.OfType<RunInstruction>());
    }

    [Theory]
    [InlineData("RUN <<EOF\nFROM not-a-stage\nRUN not-an-instruction\n")]
    [InlineData("COPY <<'EOF' /target\nFROM not-a-stage\n")]
    [InlineData("ADD <<-EOF /target\n\tbody\n")]
    [InlineData("ONBUILD RUN <<EOF\nFROM not-a-stage\n")]
    public void UnterminatedHeredocRetainsThroughEof(string heredoc)
    {
        string text = "FROM scratch\n" + heredoc;
        DockerfileParseResult strict = Dockerfile.TryParse(text);
        Assert.False(strict.Success);
        Assert.Null(strict.Dockerfile);
        Assert.Equal("DFP003", Assert.Single(strict.Diagnostics).Code);

        DockerfileParseResult result = Dockerfile.TryParse(text, RecoverPreservingUnknown());
        Assert.False(result.Success);
        DockerfileDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("DFP003", diagnostic.Code);
        Assert.Equal(text.Length, diagnostic.SourceSpan.End.Offset);
        Assert.Equal(heredoc, Assert.Single(result.Dockerfile!.Items.OfType<MalformedConstruct>()).ToString());
        Assert.Single(result.Dockerfile.Items.OfType<FromInstruction>());
        Assert.Equal(text, result.Dockerfile.ToString());
    }

    [Fact]
    public void LegacyStillAcceptsUnterminatedRunHeredoc()
    {
        const string text = "RUN <<EOF\nbody\nFROM part-of-body\n";
        Dockerfile legacy = Dockerfile.Parse(text);
        Assert.IsType<RunInstruction>(Assert.Single(legacy.Items));
        Assert.Equal(text, legacy.ToString());
        Assert.Equal("DFP003", Assert.Single(Dockerfile.TryParse(text).Diagnostics).Code);
    }

    [Theory]
    [InlineData("RUN <<'ONE' <<-\"TWO\"\nFROM body\nONE\n\tRUN body\n\tTWO\n")]
    [InlineData("COPY <<'EOF' /target\nRUN body\nEOF\n")]
    [InlineData("ADD <<-EOF /target\n\tFROM body\n\tEOF\n")]
    [InlineData("ONBUILD RUN <<EOF\nFROM body\nEOF\n")]
    public void CompletedHeredocsDoNotSplitInstructionLikeBodies(string heredoc)
    {
        string text = heredoc + "FROM scratch";
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(text, result.Dockerfile!.ToString());
        Assert.Equal(2, result.Dockerfile.Items.Count);
        Assert.Equal(heredoc, result.Dockerfile.Items[0].ToString());
        Assert.IsType<FromInstruction>(result.Dockerfile.Items[1]);
    }

    [Fact]
    public void HeredocTerminatorAtEofIsComplete()
    {
        const string text = "RUN <<EOF\nbody\nEOF";
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
        Assert.IsType<RunInstruction>(Assert.Single(result.Dockerfile!.Items));
        Assert.Equal(text, result.Dockerfile.ToString());
    }

    [Theory]
    [InlineData("RUN echo \"<<EOF\"\n")]
    [InlineData("RUN echo '<<EOF'\n")]
    [InlineData("RUN [\"echo\", \"<<EOF\"]\n")]
    [InlineData("# RUN <<EOF\n")]
    public void QuotedAndCommentMarkersAreNotHeredocs(string firstLine)
    {
        string text = firstLine + "FROM scratch\n";
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.True(result.Success);
        Assert.Equal(text, result.Dockerfile!.ToString());
        Assert.IsType<FromInstruction>(result.Dockerfile.Items.Last());
        Assert.Equal(2, result.Dockerfile.Items.Count);
    }

    [Fact]
    public void UnknownFrontendHeredocSyntaxIsNotInferred()
    {
        const string text = "FUTURE <<EOF\nFROM scratch\nRUN echo after\n";
        DockerfileParseResult result = Dockerfile.TryParse(text, RecoverPreservingUnknown());
        Assert.True(result.Success);
        Assert.Equal("DFP002", Assert.Single(result.Diagnostics).Code);
        Assert.IsType<UnknownInstruction>(result.Dockerfile!.Items[0]);
        Assert.IsType<FromInstruction>(result.Dockerfile.Items[1]);
        Assert.IsType<RunInstruction>(result.Dockerfile.Items[2]);
        Assert.Equal(text, result.Dockerfile.ToString());
    }

    [Fact]
    public void MixedModelSupportsStagesAndNonMutatingResolution()
    {
        const string text = "ARG BASE=scratch\nARG\nFROM $BASE AS first\nARG VALUE=resolved\n" +
            "FUTURE $VALUE\nFROM scratch extra tokens\nLABEL key=$VALUE\nFROM scratch AS second\nRUN echo ready\n";
        DockerfileParseResult result = Dockerfile.TryParse(text, RecoverPreservingUnknown());
        Dockerfile model = Assert.IsType<Dockerfile>(result.Dockerfile);
        StagesView stages = new(model);
        Assert.Single(stages.GlobalArgs);
        Assert.Equal(new[] { "first", "second" }, stages.Stages.Select(s => s.Name));
        Assert.Equal(2, model.Items.OfType<MalformedConstruct>().Count());
        UnknownInstruction unknown = Assert.Single(model.Items.OfType<UnknownInstruction>());
        Assert.Equal("FUTURE $VALUE\n", model.ResolveVariables(unknown));
        Assert.Equal("LABEL key=resolved\n", model.ResolveVariables(Assert.Single(model.Items.OfType<LabelInstruction>())));
        model.ResolveVariables();
        Assert.Equal(text, model.ToString());
    }

    private static DockerfileParseOptions RecoverPreservingUnknown() => new()
    {
        Mode = DockerfileParseMode.Recover,
        UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
    };
}
