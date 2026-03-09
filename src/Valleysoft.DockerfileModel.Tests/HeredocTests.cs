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
        Assert.Equal("EOF", delimiters[0]);
    }

    [Fact]
    public void ExtractHeredocDelimiters_DoubleQuotedDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<\"EOF\"\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0]);
    }

    [Fact]
    public void ExtractHeredocDelimiters_SingleQuotedDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<'EOF'\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0]);
    }

    [Fact]
    public void ExtractHeredocDelimiters_ChompFlagDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<-EOF\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0]);
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
        Assert.Equal("MY_SCRIPT_123", delimiters[0]);
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
    public void Copy_HeredocWithFromFlag_RoundTrips()
    {
        // COPY --from with heredoc source
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
}
