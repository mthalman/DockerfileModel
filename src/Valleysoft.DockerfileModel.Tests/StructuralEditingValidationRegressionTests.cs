using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>Guards prospective editing against legacy and diagnostic heredoc interpretation differences.</summary>
public class StructuralEditingValidationRegressionTests
{
    /// <summary>Marker-looking source text cannot silently create an unterminated heredoc.</summary>
    /// <param name="instructionName">The file-transfer instruction being edited.</param>
    /// <param name="source">A source value whose unquoted spelling denotes a heredoc.</param>
    [Theory]
    [InlineData("COPY", "<<EOF")]
    [InlineData("ADD", "<<EOF")]
    [InlineData("COPY", "<<-EOF")]
    [InlineData("COPY", "<<'EOF'")]
    public void MarkerLookingSourceIsRejectedWithoutMutation(string instructionName, string source)
    {
        FileTransferInstruction instruction = instructionName == "COPY"
            ? CopyInstruction.Parse("COPY a /dest")
            : AddInstruction.Parse("ADD a /dest");
        string before = instruction.ToString();
        Token[] tokens = instruction.Tokens.ToArray();

        Exception? failure = Record.Exception(() => instruction.Sources.Add(source));
        Assert.True(failure is ArgumentException or InvalidOperationException);

        Assert.Equal(before, instruction.ToString());
        Assert.Equal(tokens, instruction.Tokens);
        Assert.Empty(instruction.Heredocs);
        Assert.True(Dockerfile.TryParse(instruction.ToString()).Success);
    }

    /// <summary>Typed source adoption cannot bypass whole-instruction heredoc validation.</summary>
    [Fact]
    public void TypedMarkerLookingSourceIsRejectedWithoutMutation()
    {
        CopyInstruction instruction = CopyInstruction.Parse("COPY a /dest");
        LiteralToken destination = instruction.DestinationToken!;

        Assert.Throws<InvalidOperationException>(() => instruction.SourceTokens.Add(new LiteralToken("<<EOF")));

        Assert.Equal("COPY a /dest", instruction.ToString());
        Assert.Same(destination, instruction.DestinationToken);
    }

    /// <summary>Marker-like text remains valid when operand quoting keeps it an ordinary source.</summary>
    [Fact]
    public void QuotedMarkerLikeSourceRetainsItsOrdinaryRole()
    {
        CopyInstruction instruction = CopyInstruction.Parse("COPY a /dest");

        instruction.Sources.Add("<<EOF literal");

        CopyInstruction reparsed = Parse<CopyInstruction>(instruction.ToString(), '\\');
        Assert.Equal(new[] { "a", "<<EOF literal" }, reparsed.Sources);
        Assert.Empty(reparsed.Heredocs);
    }

    /// <summary>Generic argument lines must not introduce an incomplete heredoc accepted only by a legacy parser.</summary>
    /// <param name="escape">The effective escape character of the instruction.</param>
    [Theory]
    [InlineData('\\')]
    [InlineData('`')]
    public void GenericLineCannotIntroduceUnterminatedHeredoc(char escape)
    {
        GenericInstruction instruction = GenericInstruction.Parse("RUN echo ok", escape);
        Token[] tokens = instruction.Tokens.ToArray();

        Assert.Throws<InvalidOperationException>(() => instruction.ArgLines.Add("cat <<EOF"));

        Assert.Equal("RUN echo ok", instruction.ToString());
        Assert.Equal(tokens, instruction.Tokens);
    }

    /// <summary>Even complete heredoc text cannot be silently promoted from opaque generic arguments to modeled pairs.</summary>
    [Fact]
    public void GenericCommentsRejectUnmodeledCompleteHeredoc()
    {
        GenericInstruction instruction = GenericInstruction.Parse("RUN cat <<EOF\nbody\nEOF\n");
        string before = instruction.ToString();
        Assert.True(Dockerfile.TryParse(before).Success);

        Assert.Throws<InvalidOperationException>(() => instruction.Comments.Add("would reframe"));

        Assert.Equal(before, instruction.ToString());
        Assert.Empty(instruction.Comments);
    }

    /// <summary>Source and optional-flag edits preserve diagnostic mixed-source heredoc identities and raw body content.</summary>
    /// <param name="newline">The physical newline sequence to preserve.</param>
    /// <param name="escape">The effective parser escape character.</param>
    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '\\')]
    [InlineData("\n", '`')]
    [InlineData("\r\n", '`')]
    public void DiagnosticMixedSourcesRemainEditable(string newline, char escape)
    {
        CopyInstruction copy = Parse<CopyInstruction>(
            $"COPY regular <<'EOF' /dest{newline}raw $BODY{newline}EOF{newline}", escape);
        LiteralToken originalSource = copy.SourceTokens[0];
        LiteralToken destination = copy.DestinationToken!;
        Heredoc pair = copy.Heredocs.Single();
        string rawBody = pair.Body.ToString();

        copy.Sources[0] = "renamed";
        copy.Sources.Add("second");
        copy.Sources.Move(1, 0);
        copy.Sources.RemoveAt(1);
        copy.Excludes.Add("*.tmp");
        copy.Excludes.Clear();

        Assert.Equal("renamed", originalSource.Value);
        Assert.Equal(new[] { "second" }, copy.Sources);
        Assert.Same(destination, copy.DestinationToken);
        Assert.Same(pair, copy.Heredocs.Single());
        Assert.Same(pair.Marker, copy.HeredocMarkerTokens.Single());
        Assert.Same(pair.Body, copy.HeredocBodyTokens.Single());
        Assert.Equal(rawBody, pair.Body.ToString());
        CopyInstruction reparsed = Parse<CopyInstruction>(copy.ToString(), escape);
        Assert.Equal(copy.Sources, reparsed.Sources);
        Assert.Equal(pair.RawContent, reparsed.Heredocs.Single().RawContent);
        Assert.False(reparsed.Heredocs.Single().Expand);
    }

    /// <summary>Comment edits retain existing diagnostic heredoc definitions rather than treating their bodies as shell text.</summary>
    /// <param name="header">The diagnostic instruction header containing a heredoc marker.</param>
    /// <param name="newline">The physical newline sequence to preserve.</param>
    /// <param name="escape">The effective parser escape character.</param>
    [Theory]
    [InlineData("COPY regular <<EOF /dest", "\n", '\\')]
    [InlineData("COPY regular <<EOF /dest", "\r\n", '`')]
    [InlineData("RUN cat <<EOF", "\n", '\\')]
    [InlineData("RUN cat <<EOF", "\r\n", '`')]
    public void DiagnosticHeredocCommentsRemainEditable(string header, string newline, char escape)
    {
        Instruction instruction = Parse<Instruction>($"{header}{newline}raw $BODY{newline}EOF{newline}", escape);
        HeredocMarkerToken marker = instruction.Tokens.OfType<HeredocMarkerToken>().Single();
        HeredocBodyToken body = instruction.Tokens.OfType<HeredocBodyToken>().Single();
        string rawBody = body.ToString();

        instruction.Comments.Add("first");
        instruction.Comments[0] = "updated";
        instruction.Comments.Add("second");
        instruction.Comments.Move(1, 0);
        instruction.Comments.RemoveAt(1);

        Assert.Equal(new[] { "second" }, instruction.Comments);
        Assert.Same(marker, instruction.Tokens.OfType<HeredocMarkerToken>().Single());
        Assert.Same(body, instruction.Tokens.OfType<HeredocBodyToken>().Single());
        Assert.Equal(rawBody, body.ToString());
        Assert.Equal(instruction.ToString(), Parse<Instruction>(instruction.ToString(), escape).ToString());
    }

    /// <summary>Ordinary source insertion and clear preserve multiple quoted and chomped pair associations.</summary>
    [Fact]
    public void HeredocOnlyTransferCanGainAndLoseOrdinarySources()
    {
        CopyInstruction copy = Parse<CopyInstruction>(
            "COPY <<'FIRST' <<-SECOND /dest\none $VAR\nFIRST\n\ttwo\n\tSECOND\n", '\\');
        Heredoc[] pairs = copy.Heredocs.ToArray();
        string[] bodies = pairs.Select(pair => pair.Body.ToString()).ToArray();
        Assert.Empty(copy.Sources);

        copy.Sources.Add("ordinary");
        copy.Comments.Add("keep both");
        copy.Sources.Clear();

        Assert.Empty(copy.Sources);
        Assert.Equal(pairs, copy.Heredocs);
        Assert.Equal(bodies, copy.HeredocBodyTokens.Select(body => body.ToString()));
        CopyInstruction reparsed = Parse<CopyInstruction>(copy.ToString(), '\\');
        Assert.Equal(new[] { "FIRST", "SECOND" }, reparsed.Heredocs.Select(pair => pair.Name));
        Assert.False(reparsed.Heredocs[0].Expand);
        Assert.True(reparsed.Heredocs[1].Chomp);
    }

    /// <summary>Nested ONBUILD instructions use the same diagnostic framing and preserve nested pair identity.</summary>
    [Fact]
    public void OnBuildCommentsAndNestedSourcesPreserveHeredocs()
    {
        OnBuildInstruction trigger = Parse<OnBuildInstruction>(
            "ONBUILD COPY regular <<EOF /dest\nbody\nEOF\n", '\\');
        CopyInstruction copy = Assert.IsType<CopyInstruction>(trigger.Instruction);
        Heredoc pair = copy.Heredocs.Single();
        string body = pair.Body.ToString();

        trigger.Comments.Add("outer");
        copy.Sources[0] = "renamed";
        copy.Comments.Add("inner");

        Assert.Same(copy, trigger.Instruction);
        Assert.Same(pair, copy.Heredocs.Single());
        Assert.Equal(body, pair.Body.ToString());
        Assert.Equal(new[] { "outer", "inner" }, trigger.Comments);
        Assert.Equal(trigger.ToString(), Parse<OnBuildInstruction>(trigger.ToString(), '\\').ToString());
    }

    /// <summary>Valid instructions remain locally editable when their containing document has recoverable unrelated errors.</summary>
    [Fact]
    public void RecoveryDocumentDoesNotBlockValidInstructionEdits()
    {
        DockerfileParseResult result = Dockerfile.TryParse(
            "UNKNOWN opaque\nCOPY a /dest\nRUN cat <<EOF\nbody\nEOF\n",
            new DockerfileParseOptions { Mode = DockerfileParseMode.Recover });
        Dockerfile document = result.Dockerfile!;
        string opaque = document.Items[0].ToString();
        CopyInstruction copy = document.Items.OfType<CopyInstruction>().Single();
        RunInstruction run = document.Items.OfType<RunInstruction>().Single();

        copy.Sources.Add("b");
        run.Comments.Add("local");

        Assert.Equal(opaque, document.Items[0].ToString());
        Assert.Equal(new[] { "a", "b" }, copy.Sources);
        Assert.Equal("local", run.Comments.Single());
    }

    /// <summary>Repeated empty-list comment insertion reuses a bare header continuation without absorbing flags into arguments.</summary>
    /// <param name="instructionName">The flag-bearing instruction without heredocs.</param>
    /// <param name="newline">The physical line ending of its existing continuation.</param>
    /// <param name="escape">The effective instruction escape character.</param>
    [Theory]
    [InlineData("RUN", "\n", '\\')]
    [InlineData("RUN", "\r\n", '`')]
    [InlineData("COPY", "\n", '\\')]
    [InlineData("COPY", "\r\n", '`')]
    public void RepeatedCommentInsertionReusesBareHeaderContinuation(string instructionName, string newline, char escape)
    {
        string arguments = instructionName == "RUN" ? "--network=none echo hello" : "--from=base source /dest";
        string original = $"{instructionName} {escape}{newline}{arguments}{newline}";
        Instruction instruction = Parse<Instruction>(original, escape);
        Token[] tokens = instruction.Tokens.ToArray();
        LineContinuationToken continuation = Assert.Single(instruction.Tokens.OfType<LineContinuationToken>());

        for (int cycle = 0; cycle < 3; cycle++)
        {
            instruction.Comments.Add("new");

            Assert.Equal($"{instructionName} {escape}{newline}#new{newline}{arguments}{newline}", instruction.ToString());
            Assert.Same(continuation, Assert.Single(instruction.Tokens.OfType<LineContinuationToken>()));
            Instruction reparsed = Parse<Instruction>(instruction.ToString(), escape);
            if (instructionName == "RUN")
            {
                Assert.Equal("none", Assert.IsType<RunInstruction>(reparsed).Network);
                Assert.Equal("echo hello", Assert.IsType<ShellFormCommand>(Assert.IsType<RunInstruction>(reparsed).Command).Value);
            }
            else
            {
                CopyInstruction copy = Assert.IsType<CopyInstruction>(reparsed);
                Assert.Equal("base", copy.FromStageName);
                Assert.Equal(new[] { "source" }, copy.Sources);
                Assert.Equal("/dest", copy.Destination);
            }

            instruction.Comments.Clear();

            Assert.Equal(original, instruction.ToString());
            Assert.Equal(tokens, instruction.Tokens);
        }
    }

    /// <summary>Context-independent heredoc delimiters do not prevent adopting otherwise compatible instruction trees.</summary>
    /// <param name="header">The RUN or COPY header containing the modeled heredoc.</param>
    /// <param name="escape">The shared document and incoming instruction escape character.</param>
    /// <param name="replace">Whether to replace the existing instruction instead of appending.</param>
    [Theory]
    [InlineData("RUN <<EOF", '`', false)]
    [InlineData("RUN <<EOF", '`', true)]
    [InlineData("COPY <<EOF /dest", '`', false)]
    [InlineData("COPY <<EOF /dest", '`', true)]
    [InlineData("RUN <<EOF", '\\', false)]
    [InlineData("RUN <<EOF", '\\', true)]
    [InlineData("COPY <<EOF /dest", '\\', false)]
    [InlineData("COPY <<EOF /dest", '\\', true)]
    public void DocumentAdoptsMatchingContextHeredocs(string header, char escape, bool replace)
    {
        string prefix = escape == '`' ? "# escape=`\n" : "";
        Dockerfile document = Dockerfile.Parse(prefix + "FROM alpine\n");
        string text = $"{header}\nraw $BODY\nEOF\n";
        Instruction incoming = Parse<Instruction>(text, escape);
        Token[] tokens = incoming.Tokens.ToArray();
        HeredocMarkerToken marker = incoming.Tokens.OfType<HeredocMarkerToken>().Single();
        HeredocBodyToken body = incoming.Tokens.OfType<HeredocBodyToken>().Single();

        if (replace)
        {
            document.Items[document.Items.Count - 1] = incoming;
        }
        else
        {
            document.Items.Add(incoming);
        }

        Assert.Same(incoming, document.Items.Last());
        Assert.Equal(tokens, incoming.Tokens);
        Assert.Same(marker, incoming.Tokens.OfType<HeredocMarkerToken>().Single());
        Assert.Same(body, incoming.Tokens.OfType<HeredocBodyToken>().Single());
        Assert.Equal(text, incoming.ToString());
        Assert.Equal(prefix + (replace ? "" : "FROM alpine\n") + text, document.ToString());
        Assert.True(Dockerfile.TryParse(document.ToString()).Success);
    }

    /// <summary>Ignoring delimiter metadata does not permit incompatible context-bearing descendants within heredoc instructions.</summary>
    /// <param name="header">The RUN or COPY header containing the modeled heredoc.</param>
    /// <param name="escape">The shared document and incoming instruction root escape character.</param>
    /// <param name="replace">Whether adoption replaces an instruction instead of appending.</param>
    [Theory]
    [InlineData("RUN <<EOF", '`', false)]
    [InlineData("RUN <<EOF", '`', true)]
    [InlineData("COPY <<EOF /dest", '`', false)]
    [InlineData("COPY <<EOF /dest", '`', true)]
    [InlineData("RUN <<EOF", '\\', false)]
    [InlineData("RUN <<EOF", '\\', true)]
    [InlineData("COPY <<EOF /dest", '\\', false)]
    [InlineData("COPY <<EOF /dest", '\\', true)]
    public void DocumentRejectsMismatchedDescendantsInHeredocs(string header, char escape, bool replace)
    {
        string prefix = escape == '`' ? "# escape=`\n" : "";
        Dockerfile document = Dockerfile.Parse(prefix + "FROM alpine\n");
        Instruction incoming = Parse<Instruction>($"{header}\nbody\nEOF\n", escape);
        char incompatible = escape == '`' ? '\\' : '`';
        if (incoming is RunInstruction run)
        {
            run.Network = "host";
            LiteralToken network = new("host", escapeChar: incompatible);
            run.NetworkToken = network;
            Assert.Same(network, run.NetworkToken);
        }
        else
        {
            Assert.IsType<CopyInstruction>(incoming).DestinationToken = new LiteralToken("/dest", escapeChar: incompatible);
        }
        string before = document.ToString();
        DockerfileConstruct[] items = document.Items.ToArray();
        string incomingBefore = incoming.ToString();
        Token[] tokens = incoming.Tokens.ToArray();

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (replace)
            {
                document.Items[document.Items.Count - 1] = incoming;
            }
            else
            {
                document.Items.Add(incoming);
            }
        });

        Assert.Equal(before, document.ToString());
        Assert.Equal(items, document.Items);
        Assert.Equal(incomingBefore, incoming.ToString());
        Assert.Equal(tokens, incoming.Tokens);
    }

    /// <summary>Parses one complete instruction with its actual diagnostic escape context.</summary>
    /// <typeparam name="T">The expected instruction type, or its common instruction base.</typeparam>
    /// <param name="text">The complete instruction and any heredoc bodies.</param>
    /// <param name="escape">The effective escape character.</param>
    /// <returns>The sole parsed instruction without replacing any live model under test.</returns>
    private static T Parse<T>(string text, char escape) where T : Instruction
    {
        string prefix = escape == '`' ? "# escape=`\n" : "";
        DockerfileParseResult result = Dockerfile.TryParse(prefix + text);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return Assert.IsAssignableFrom<T>(result.Dockerfile!.Items.OfType<Instruction>().Single());
    }
}
