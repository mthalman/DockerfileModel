using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class HeredocTests
{
    // ==============================
    // RUN instruction heredoc tests
    // ==============================

    [Fact]
    public void Run_SimpleHeredoc_RoundTrips()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_SimpleHeredoc_HasHeredocToken()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Null(result.Command);
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_SimpleHeredoc_TokenStructure()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());

        // Check instruction-level tokens: keyword, whitespace, heredoc
        Assert.Equal("RUN", result.InstructionName);
        Assert.Single(result.Heredocs);

        HeredocToken heredoc = result.Heredocs.First();
        Assert.Equal("<<EOF\necho hello\nEOF\n", heredoc.ToString());
    }

    [Fact]
    public void Run_HeredocWithChompFlag_RoundTrips()
    {
        string text = "RUN <<-EOF\n\techo hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocWithDoubleQuotedDelimiter_RoundTrips()
    {
        string text = "RUN <<\"EOF\"\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocWithSingleQuotedDelimiter_RoundTrips()
    {
        string text = "RUN <<'EOF'\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocMultipleBodyLines_RoundTrips()
    {
        string text = "RUN <<EOF\nline 1\nline 2\nline 3\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocEmptyBody_RoundTrips()
    {
        string text = "RUN <<EOF\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocWithCustomDelimiter_RoundTrips()
    {
        string text = "RUN <<SCRIPT\n#!/bin/bash\necho hello\nSCRIPT\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocNoTrailingNewline_RoundTrips()
    {
        string text = "RUN <<EOF\necho hello\nEOF";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocChompWithDoubleQuote_RoundTrips()
    {
        string text = "RUN <<-\"EOF\"\n\techo hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocWithShebangAndMultipleCommands_RoundTrips()
    {
        string text = "RUN <<EOF\n#!/bin/bash\nset -e\napt-get update\napt-get install -y curl\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocWithMountFlag_RoundTrips()
    {
        string text = "RUN --mount=type=secret,id=id <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
        Assert.Single(result.Mounts);
    }

    // ==============================
    // COPY instruction heredoc tests
    // ==============================

    [Fact]
    public void Copy_SimpleHeredoc_RoundTrips()
    {
        string text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Copy_SimpleHeredoc_HasHeredocToken()
    {
        string text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Copy_HeredocWithQuotedDelimiter_RoundTrips()
    {
        string text = "COPY <<\"EOF\" /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Copy_HeredocMultipleBodyLines_RoundTrips()
    {
        string text = "COPY <<EOF /app/config.txt\nline 1\nline 2\nline 3\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    // ==============================
    // ADD instruction heredoc tests
    // ==============================

    [Fact]
    public void Add_SimpleHeredoc_RoundTrips()
    {
        string text = "ADD <<EOF /app/script.sh\necho hello\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Add_SimpleHeredoc_HasHeredocToken()
    {
        string text = "ADD <<EOF /app/script.sh\necho hello\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Add_HeredocWithQuotedDelimiter_RoundTrips()
    {
        string text = "ADD <<\"EOF\" /app/script.sh\necho hello\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    // ==============================
    // Dockerfile-level heredoc tests
    // ==============================

    [Fact]
    public void Dockerfile_WithHeredocRun_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<EOF\necho hello\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_WithHeredocCopy_RoundTrips()
    {
        string text = "FROM ubuntu\nCOPY <<EOF /app/script.sh\necho hello\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_HeredocFollowedByInstruction_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<EOF\necho hello\nEOF\nRUN echo world\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_MultipleHeredocInstructions_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<EOF\necho hello\nEOF\nRUN <<SCRIPT\necho world\nSCRIPT\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    // ==============================
    // Round-trip fidelity edge cases
    // ==============================

    [Fact]
    public void Run_HeredocPreservesExactWhitespace()
    {
        string text = "RUN <<EOF\n  indented line\n    double indented\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocPreservesEmptyLines()
    {
        string text = "RUN <<EOF\nline 1\n\nline 3\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocPreservesSpecialCharacters()
    {
        string text = "RUN <<EOF\n$HOME=/root\necho \"hello world\"\necho 'single'\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocDelimiterWithNumbers_RoundTrips()
    {
        string text = "RUN <<EOF123\necho hello\nEOF123\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocDelimiterWithUnderscore_RoundTrips()
    {
        string text = "RUN <<MY_EOF\necho hello\nMY_EOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Run_HeredocQuotedDelimiterWithHyphen_RoundTrips()
    {
        // <<'MY-DELIM' - hyphen is valid in quoted delimiters and must be parsed by
        // HeredocParseImpl (IsHeredocDelimiterChar) consistently with HeredocMarkerRegex.
        string text = "RUN <<'MY-DELIM'\necho hello\nMY-DELIM\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void ExtractHeredocDelimiters_QuotedHyphenatedDelimiter()
    {
        // Verify that ExtractHeredocDelimiters extracts a hyphenated delimiter name
        // from a quoted marker, consistent with the expanded IsHeredocDelimiterChar.
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<'MY-DELIM'\n");
        Assert.Single(delimiters);
        Assert.Equal("MY-DELIM", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void Run_HeredocChildTokens_AreCorrect()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocToken heredoc = result.Heredocs.First();

        // The heredoc token's children should preserve all text:
        // "<<EOF" (StringToken), "\n" (NewLineToken), "echo hello\n" (StringToken), "EOF" (StringToken), "\n" (NewLineToken)
        var children = heredoc.Tokens.ToList();
        Assert.True(children.Count >= 4, $"Expected at least 4 children, got {children.Count}");

        // First child: marker "<<EOF"
        Assert.IsType<StringToken>(children[0]);
        Assert.Equal("<<EOF", ((StringToken)children[0]).Value);

        // Second child: newline
        Assert.IsType<NewLineToken>(children[1]);

        // Concatenation of all children must equal the heredoc text
        Assert.Equal("<<EOF\necho hello\nEOF\n", heredoc.ToString());
    }

    [Fact]
    public void Run_HeredocWithChompFlag_ChildTokens()
    {
        string text = "RUN <<-EOF\n\techo hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocToken heredoc = result.Heredocs.First();

        var children = heredoc.Tokens.ToList();
        // First child should be "<<-EOF"
        Assert.IsType<StringToken>(children[0]);
        Assert.Equal("<<-EOF", ((StringToken)children[0]).Value);

        Assert.Equal("<<-EOF\n\techo hello\nEOF\n", heredoc.ToString());
    }

    [Fact]
    public void Run_HeredocQuotedDelimiter_ChildTokens()
    {
        string text = "RUN <<\"EOF\"\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocToken heredoc = result.Heredocs.First();

        var children = heredoc.Tokens.ToList();
        // First child should be "<<\"EOF\""
        Assert.IsType<StringToken>(children[0]);
        Assert.Equal("<<\"EOF\"", ((StringToken)children[0]).Value);

        Assert.Equal("<<\"EOF\"\necho hello\nEOF\n", heredoc.ToString());
    }

    // ==============================
    // DockerfileParser heredoc delimiter extraction tests
    // ==============================

    [Fact]
    public void ExtractHeredocDelimiters_UnquotedDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<EOF\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_DoubleQuotedDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<\"EOF\"\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_SingleQuotedDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<'EOF'\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_ChompFlagDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<-EOF\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
        Assert.True(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_NoHeredoc()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN echo hello\n");
        Assert.Empty(delimiters);
    }

    [Fact]
    public void ExtractHeredocDelimiters_CustomDelimiterName()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<MY_SCRIPT_123\n");
        Assert.Single(delimiters);
        Assert.Equal("MY_SCRIPT_123", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    // ==============================
    // Dockerfile round-trip with heredoc in complex structures
    // ==============================

    [Fact]
    public void Dockerfile_HeredocBetweenInstructions_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN echo before\nRUN <<EOF\necho hello\nEOF\nRUN echo after\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(4, result.Items.Count);
    }

    [Fact]
    public void Dockerfile_HeredocWithMultipleStages_RoundTrips()
    {
        string text = "FROM ubuntu AS builder\nRUN <<EOF\necho building\nEOF\nFROM alpine\nRUN echo done\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_AddHeredocBetweenInstructions_RoundTrips()
    {
        string text = "FROM ubuntu\nADD <<EOF /app/config.txt\nkey=value\nEOF\nRUN echo done\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    // ==============================
    // RUN instruction ResolveVariables with heredoc
    // ==============================

    [Fact]
    public void Run_HeredocResolveVariables_ReturnsOriginalText()
    {
        // RUN instructions don't resolve variables (shell handles them)
        string text = "RUN <<EOF\necho $HOME\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        string? resolved = result.ResolveVariables('\\');
        Assert.Equal(text, resolved);
    }

    // ==============================
    // COPY/ADD with heredoc and flags
    // ==============================

    [Fact]
    public void Copy_HeredocWithDestination_RoundTrips()
    {
        // COPY with a heredoc source and a destination path on the marker line
        string text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Add_HeredocMultipleBodyLines_RoundTrips()
    {
        string text = "ADD <<EOF /app/config\nline1\nline2\nline3\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    // ==============================
    // Heredoc with CRLF line endings (Windows)
    // ==============================

    [Fact]
    public void Run_HeredocWithCRLF_RoundTrips()
    {
        string text = "RUN <<EOF\r\necho hello\r\nEOF\r\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    // ==============================
    // Fix #1: Non-chomp heredoc with tab-indented delimiter-matching body line
    // ==============================

    [Fact]
    public void Dockerfile_NonChompHeredoc_TabIndentedDelimiterLineIsBodyNotClose()
    {
        // With non-chomp <<EOF, a body line "\tEOF" should NOT close the heredoc.
        // Only an exact "EOF" line closes it.
        string text = "FROM ubuntu\nRUN <<EOF\n\tEOF\nreal body\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());

        // Should parse as two constructs: FROM + RUN (the heredoc stays open past \tEOF)
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public void Run_NonChompHeredoc_TabIndentedDelimiterIsBody()
    {
        // <<EOF (no chomp): "\tEOF" is body text, not a closing delimiter
        string text = "RUN <<EOF\n\tEOF\nactual content\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);

        // The heredoc body should include "\tEOF\n" as body content
        HeredocToken heredoc = result.Heredocs.First();
        Assert.Equal("<<EOF\n\tEOF\nactual content\nEOF\n", heredoc.ToString());
    }

    [Fact]
    public void Run_ChompHeredoc_TabIndentedDelimiterClosesHeredoc()
    {
        // <<-EOF (chomp): "\tEOF" SHOULD close the heredoc
        string text = "RUN <<-EOF\n\techo hello\n\tEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    // ==============================
    // Fix #2: Single heredoc per instruction (multi-heredoc limitation)
    // ==============================

    [Fact]
    public void Run_SingleHeredoc_WorksCorrectly()
    {
        // Verify that a single heredoc per instruction works correctly
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void Copy_SingleHeredoc_WithDestination_WorksCorrectly()
    {
        // Verify single heredoc with destination on the same line works
        string text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    // ==============================
    // Fix #3: Sources/Destination behavior on heredoc COPY/ADD
    // ==============================

    [Fact]
    public void Copy_Heredoc_DestinationIsNull()
    {
        // Known limitation: the destination path "/app/script.sh" is absorbed into the
        // heredoc token's rest-of-line StringToken by HeredocParseImpl, so no separate
        // LiteralToken is produced and Destination returns null. This is inconsistent with
        // the documented COPY heredoc syntax (COPY <<EOF /dest) and with the existing
        // Destination/Sources API. Fixing it requires stopping the heredoc token at the
        // delimiter and re-tokenizing the remainder, which is deferred to avoid parser risk.
        string text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Null(result.Destination);
    }

    [Fact]
    public void Add_Heredoc_DestinationIsNull()
    {
        // Known limitation: same as Copy_Heredoc_DestinationIsNull — the destination is
        // absorbed into the heredoc token and is not accessible via the Destination property.
        string text = "ADD <<EOF /app/script.sh\necho hello\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Null(result.Destination);
    }

    [Fact]
    public void Copy_Heredoc_SourcesIsEmpty()
    {
        // Known limitation: Sources is empty because the heredoc parser absorbs the
        // marker-line remainder (which would contain the destination) and does not produce
        // any LiteralToken children. See Copy_Heredoc_DestinationIsNull for full context.
        string text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void Copy_Heredoc_SetDestination_Throws()
    {
        // Because Destination is null (known limitation), the setter throws rather than
        // overwriting a non-existent token.
        string text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Throws<InvalidOperationException>(() => result.Destination = "/new/path");
    }

    // ==============================
    // Fix #5: RunInstruction.Command setter throws on heredoc
    // ==============================

    [Fact]
    public void Run_Heredoc_SetCommand_ThrowsInvalidOperation()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Null(result.Command);

        // Setting Command on a heredoc instruction should throw
        Assert.Throws<InvalidOperationException>(() =>
            result.Command = ShellFormCommand.Parse("echo world"));
    }

    // ==============================
    // ExtractHeredocDelimiters chomp flag tests
    // ==============================

    [Fact]
    public void ExtractHeredocDelimiters_ChompQuotedDelimiter_HasChompTrue()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<-\"EOF\"\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
        Assert.True(delimiters[0].HasChomp);
    }

    // ==============================
    // Comment 4: delimiter grammar alignment between ExtractHeredocDelimiters and HeredocParseImpl
    // ==============================

    [Fact]
    public void ExtractHeredocDelimiters_QuotedDelimiterCharset_MatchesParser()
    {
        // The HeredocMarkerRegex now uses [A-Za-z0-9_.\-]+ for quoted delimiters
        // (previously [^"]+), matching the expanded IsHeredocDelimiterChar.
        // A quoted delimiter with a hyphen must be detected AND parseable.
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<\"MY-DELIM\"\n");
        Assert.Single(delimiters);
        Assert.Equal("MY-DELIM", delimiters[0].Delimiter);

        // Verify the same input is also parseable end-to-end
        string text = "RUN <<\"MY-DELIM\"\necho hello\nMY-DELIM\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.Heredocs);
    }

    [Fact]
    public void ExtractHeredocDelimiters_ArbitraryQuotedCharsNotMatched()
    {
        // Characters outside [A-Za-z0-9_.\-] in a quoted delimiter are no longer
        // matched by the tightened regex, so lines like <<'EOF SPACE' are not treated
        // as heredoc markers (which aligns with what the parser would accept).
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<'EOF SPACE'\n");
        Assert.Empty(delimiters);
    }

    // ==============================
    // Fix: heredoc marker in trailing comment must not trigger heredoc mode
    // ==============================

    [Fact]
    public void ExtractHeredocDelimiters_MarkerInTrailingComment_ReturnsEmpty()
    {
        // A <<DELIM sequence that appears after an unquoted '#' is part of a shell
        // comment and must not be treated as a heredoc marker.
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN echo hi # <<EOF\n");
        Assert.Empty(delimiters);
    }

    [Fact]
    public void Dockerfile_RunWithHeredocMarkerInComment_DoesNotEnterHeredocMode()
    {
        // Regression test: "RUN echo hi # <<EOF" must parse as a single RUN instruction
        // that does NOT enter heredoc mode, so the next line is a new construct rather
        // than being consumed as heredoc body text.
        string text = "FROM ubuntu\nRUN echo hi # <<EOF\nRUN echo world\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
        // Three constructs: FROM, RUN echo hi # <<EOF, RUN echo world
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public void StripTrailingComment_NoComment_ReturnsOriginal()
    {
        string line = "RUN echo hello\n";
        Assert.Equal(line, DockerfileParser.StripTrailingComment(line));
    }

    [Fact]
    public void StripTrailingComment_WithComment_ReturnsBeforeHash()
    {
        Assert.Equal("RUN echo hi ", DockerfileParser.StripTrailingComment("RUN echo hi # <<EOF\n"));
    }

    [Fact]
    public void StripTrailingComment_HashInsideSingleQuotes_NotTreatedAsComment()
    {
        string line = "RUN echo '#notacomment' world";
        Assert.Equal(line, DockerfileParser.StripTrailingComment(line));
    }

    [Fact]
    public void StripTrailingComment_HashInsideDoubleQuotes_NotTreatedAsComment()
    {
        string line = "RUN echo \"#notacomment\" world";
        Assert.Equal(line, DockerfileParser.StripTrailingComment(line));
    }

    [Fact]
    public void StripTrailingComment_HashAfterClosingQuote_TreatedAsComment()
    {
        Assert.Equal("RUN echo 'hello' ", DockerfileParser.StripTrailingComment("RUN echo 'hello' # comment <<EOF"));
    }

    // ==============================
    // Dockerfile-level heredoc scenarios (DockerfileParser.ParseContent coverage)
    // ==============================

    /// <summary>
    /// Scenario 1: Full Dockerfile with RUN heredoc — multi-instruction Dockerfile where one
    /// RUN uses a heredoc. Blank lines produce Whitespace constructs so we get 5 items total.
    /// </summary>
    [Fact]
    public void Dockerfile_RunHeredoc_FullDockerfile_CorrectConstructsAndRoundTrip()
    {
        string text = "FROM alpine\n\nRUN <<EOF\necho hello\necho world\nEOF\n\nRUN echo done";
        Dockerfile result = Dockerfile.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // 5 constructs: FROM, Whitespace, RUN heredoc, Whitespace, RUN
        Assert.Equal(5, result.Items.Count);

        Assert.IsType<FromInstruction>(result.Items[0]);
        Assert.IsType<Whitespace>(result.Items[1]);

        RunInstruction runHeredoc = Assert.IsType<RunInstruction>(result.Items[2]);
        Assert.Single(runHeredoc.Heredocs);
        Assert.Null(runHeredoc.Command);

        Assert.IsType<Whitespace>(result.Items[3]);

        RunInstruction runDone = Assert.IsType<RunInstruction>(result.Items[4]);
        Assert.NotNull(runDone.Command); // shell form — Command is non-null
        Assert.Empty(runDone.Heredocs);
    }

    /// <summary>
    /// Scenario 2: Dockerfile with COPY heredoc — COPY using heredoc with destination.
    /// </summary>
    [Fact]
    public void Dockerfile_CopyHeredoc_CorrectConstructsAndRoundTrip()
    {
        string text = "FROM alpine\nCOPY <<EOF /app/config.txt\nkey=value\nEOF";
        Dockerfile result = Dockerfile.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // 2 constructs: FROM, COPY heredoc
        Assert.Equal(2, result.Items.Count);

        Assert.IsType<FromInstruction>(result.Items[0]);

        CopyInstruction copyHeredoc = Assert.IsType<CopyInstruction>(result.Items[1]);
        Assert.Single(copyHeredoc.Heredocs);
    }

    /// <summary>
    /// Scenario 3: Heredoc after line continuation — heredoc marker appears on the continued line.
    /// </summary>
    [Fact]
    public void Dockerfile_HeredocAfterLineContinuation_CorrectConstructsAndRoundTrip()
    {
        string text = "FROM alpine\nRUN \\\n<<EOF\nscript content\nEOF";
        Dockerfile result = Dockerfile.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // 2 constructs: FROM, RUN (assembled from continuation + heredoc body)
        Assert.Equal(2, result.Items.Count);

        Assert.IsType<FromInstruction>(result.Items[0]);

        RunInstruction runInstruction = Assert.IsType<RunInstruction>(result.Items[1]);
        Assert.Single(runInstruction.Heredocs);
    }

    /// <summary>
    /// Scenario 4: Heredoc with trailing comment on other lines — verifies that
    /// StripTrailingComment prevents a comment-embedded &lt;&lt;EOF from triggering heredoc mode.
    /// </summary>
    [Fact]
    public void Dockerfile_HeredocMarkerInComment_DoesNotAbsorbNextInstruction()
    {
        string text = "FROM alpine\nRUN echo hi # not a heredoc <<EOF\nRUN <<EOF\nreal heredoc\nEOF";
        Dockerfile result = Dockerfile.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // 3 constructs: FROM, RUN (comment line — no heredoc), RUN (real heredoc)
        Assert.Equal(3, result.Items.Count);

        Assert.IsType<FromInstruction>(result.Items[0]);

        // First RUN: ordinary shell form — the comment-embedded <<EOF is NOT a heredoc
        RunInstruction runComment = Assert.IsType<RunInstruction>(result.Items[1]);
        Assert.Empty(runComment.Heredocs);

        // Second RUN: real heredoc
        RunInstruction runReal = Assert.IsType<RunInstruction>(result.Items[2]);
        Assert.Single(runReal.Heredocs);
    }

    /// <summary>
    /// Scenario 5: Multiple RUN instructions with heredocs — two separate heredoc instructions,
    /// each with their own delimiter.
    /// </summary>
    [Fact]
    public void Dockerfile_MultipleHeredocRuns_EachHasOwnHeredoc()
    {
        string text = "FROM alpine\nRUN <<EOF\nfirst script\nEOF\nRUN <<SCRIPT\nsecond script\nSCRIPT";
        Dockerfile result = Dockerfile.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // 3 constructs: FROM, first RUN heredoc, second RUN heredoc
        Assert.Equal(3, result.Items.Count);

        Assert.IsType<FromInstruction>(result.Items[0]);

        RunInstruction firstRun = Assert.IsType<RunInstruction>(result.Items[1]);
        Assert.Single(firstRun.Heredocs);
        Assert.Equal("<<EOF\nfirst script\nEOF\n", firstRun.Heredocs.First().ToString());

        RunInstruction secondRun = Assert.IsType<RunInstruction>(result.Items[2]);
        Assert.Single(secondRun.Heredocs);
        Assert.Equal("<<SCRIPT\nsecond script\nSCRIPT", secondRun.Heredocs.First().ToString());
    }

    /// <summary>
    /// Scenario 6: Heredoc with quoted delimiter — &lt;&lt;"EOF" suppresses variable expansion.
    /// </summary>
    [Fact]
    public void Dockerfile_HeredocQuotedDelimiter_RoundTrip()
    {
        string text = "FROM alpine\nRUN <<\"EOF\"\necho $HOME\nEOF";
        Dockerfile result = Dockerfile.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // 2 constructs: FROM, RUN heredoc
        Assert.Equal(2, result.Items.Count);

        Assert.IsType<FromInstruction>(result.Items[0]);

        RunInstruction runHeredoc = Assert.IsType<RunInstruction>(result.Items[1]);
        Assert.Single(runHeredoc.Heredocs);
        Assert.Equal("<<\"EOF\"\necho $HOME\nEOF", runHeredoc.Heredocs.First().ToString());
    }

    /// <summary>
    /// Scenario 7: Heredoc with chomp flag — &lt;&lt;-EOF strips leading tabs.
    /// </summary>
    [Fact]
    public void Dockerfile_HeredocChompFlag_RoundTrip()
    {
        string text = "FROM alpine\nRUN <<-EOF\n\techo hello\n\techo world\nEOF";
        Dockerfile result = Dockerfile.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // 2 constructs: FROM, RUN heredoc
        Assert.Equal(2, result.Items.Count);

        Assert.IsType<FromInstruction>(result.Items[0]);

        RunInstruction runHeredoc = Assert.IsType<RunInstruction>(result.Items[1]);
        Assert.Single(runHeredoc.Heredocs);
        Assert.Equal("<<-EOF\n\techo hello\n\techo world\nEOF", runHeredoc.Heredocs.First().ToString());
    }

    /// <summary>
    /// Scenario 8: Empty heredoc — delimiter immediately follows the marker with no body lines.
    /// </summary>
    [Fact]
    public void Dockerfile_EmptyHeredoc_ValidParseAndRoundTrip()
    {
        string text = "FROM alpine\nRUN <<EOF\nEOF";
        Dockerfile result = Dockerfile.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // 2 constructs: FROM, RUN heredoc
        Assert.Equal(2, result.Items.Count);

        Assert.IsType<FromInstruction>(result.Items[0]);

        RunInstruction runHeredoc = Assert.IsType<RunInstruction>(result.Items[1]);
        Assert.Single(runHeredoc.Heredocs);
        Assert.Equal("<<EOF\nEOF", runHeredoc.Heredocs.First().ToString());
    }

    /// <summary>
    /// Scenario 9: Heredoc interleaved with comments — a Dockerfile-level comment between
    /// two heredoc instructions must be its own construct and not be absorbed into any heredoc.
    /// </summary>
    [Fact]
    public void Dockerfile_HeredocInterleavedWithComment_CommentIsSeparateConstruct()
    {
        string text = "FROM alpine\nRUN <<EOF\nscript\nEOF\n# A comment\nRUN echo done";
        Dockerfile result = Dockerfile.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // 4 constructs: FROM, RUN heredoc, Comment, RUN
        Assert.Equal(4, result.Items.Count);

        Assert.IsType<FromInstruction>(result.Items[0]);

        RunInstruction runHeredoc = Assert.IsType<RunInstruction>(result.Items[1]);
        Assert.Single(runHeredoc.Heredocs);

        Assert.IsType<Comment>(result.Items[2]);

        RunInstruction runDone = Assert.IsType<RunInstruction>(result.Items[3]);
        Assert.Empty(runDone.Heredocs);
    }
}
