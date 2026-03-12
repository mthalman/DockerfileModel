using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Tests verifying that trailing whitespace at the end of instruction argument lines
/// is dropped from the token list, matching BuildKit behavior of trimming trailing
/// whitespace from instructions.
/// </summary>
public class TrailingWhitespaceTests
{
    // -----------------------------------------------------------------------
    // FROM instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("FROM alpine ")]
    [InlineData("FROM alpine\t")]
    [InlineData("FROM alpine   ")]
    [InlineData("FROM alpine \t ")]
    public void From_TrailingWhitespace_NoStandaloneWhitespaceToken(string input)
    {
        FromInstruction instr = FromInstruction.Parse(input);
        AssertNoTrailingWhitespaceToken(instr, input);
    }

    [Theory]
    [InlineData("FROM alpine ", "FROM alpine")]
    [InlineData("FROM alpine\t", "FROM alpine")]
    [InlineData("FROM alpine   ", "FROM alpine")]
    public void From_TrailingWhitespace_Trimmed(string input, string expected)
    {
        FromInstruction instr = FromInstruction.Parse(input);
        Assert.Equal(expected, instr.ToString());
    }

    [Fact]
    public void From_NoTrailingWhitespace_Unchanged()
    {
        FromInstruction instr = FromInstruction.Parse("FROM alpine");
        AssertNoTrailingWhitespaceToken(instr, "FROM alpine");
        Assert.Equal("FROM alpine", instr.ToString());
    }

    [Fact]
    public void From_TrailingNewline_PreservesNewlineToken()
    {
        FromInstruction instr = FromInstruction.Parse("FROM alpine\n");
        Assert.Equal("FROM alpine\n", instr.ToString());
        // The last token should be a NewLineToken, not a plain WhitespaceToken
        Token last = instr.Tokens.Last();
        Assert.IsType<NewLineToken>(last);
    }

    [Fact]
    public void From_TrailingWhitespaceBeforeNewline_NoStandaloneWhitespaceToken()
    {
        FromInstruction instr = FromInstruction.Parse("FROM alpine  \n");
        AssertNoTrailingWhitespaceToken(instr, "FROM alpine  \n");
        Assert.Equal("FROM alpine\n", instr.ToString());
    }

    // -----------------------------------------------------------------------
    // ENV instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("ENV FOO=bar ")]
    [InlineData("ENV FOO=bar\t")]
    [InlineData("ENV FOO=bar   ")]
    public void Env_TrailingWhitespace_NoStandaloneWhitespaceToken(string input)
    {
        EnvInstruction instr = EnvInstruction.Parse(input);
        AssertNoTrailingWhitespaceToken(instr, input);
    }

    [Theory]
    [InlineData("ENV FOO=bar ", "ENV FOO=bar")]
    [InlineData("ENV FOO=bar\t", "ENV FOO=bar")]
    [InlineData("ENV FOO=bar   ", "ENV FOO=bar")]
    public void Env_TrailingWhitespace_Trimmed(string input, string expected)
    {
        EnvInstruction instr = EnvInstruction.Parse(input);
        Assert.Equal(expected, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // EXPOSE instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("EXPOSE 80 ")]
    [InlineData("EXPOSE 80\t")]
    [InlineData("EXPOSE 80   ")]
    public void Expose_TrailingWhitespace_NoStandaloneWhitespaceToken(string input)
    {
        ExposeInstruction instr = ExposeInstruction.Parse(input);
        AssertNoTrailingWhitespaceToken(instr, input);
    }

    [Theory]
    [InlineData("EXPOSE 80 ", "EXPOSE 80")]
    [InlineData("EXPOSE 80\t", "EXPOSE 80")]
    [InlineData("EXPOSE 80   ", "EXPOSE 80")]
    public void Expose_TrailingWhitespace_Trimmed(string input, string expected)
    {
        ExposeInstruction instr = ExposeInstruction.Parse(input);
        Assert.Equal(expected, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // WORKDIR instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("WORKDIR /app ")]
    [InlineData("WORKDIR /app\t")]
    [InlineData("WORKDIR /app   ")]
    public void Workdir_TrailingWhitespace_NoStandaloneWhitespaceToken(string input)
    {
        WorkdirInstruction instr = WorkdirInstruction.Parse(input);
        AssertNoTrailingWhitespaceToken(instr, input);
    }

    [Theory]
    [InlineData("WORKDIR /app ")]
    [InlineData("WORKDIR /app\t")]
    [InlineData("WORKDIR /app   ")]
    public void Workdir_TrailingWhitespace_RoundTrip(string input)
    {
        // WORKDIR allows whitespace in path values (WhitespaceMode.Allowed), so trailing
        // whitespace is embedded inside the LiteralToken rather than as a top-level token.
        // AbsorbTrailingWhitespace only removes top-level WhitespaceTokens, so WORKDIR
        // round-trips with trailing whitespace preserved.
        WorkdirInstruction instr = WorkdirInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // LABEL instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("LABEL foo=bar ")]
    [InlineData("LABEL foo=bar\t")]
    [InlineData("LABEL foo=bar   ")]
    public void Label_TrailingWhitespace_NoStandaloneWhitespaceToken(string input)
    {
        LabelInstruction instr = LabelInstruction.Parse(input);
        AssertNoTrailingWhitespaceToken(instr, input);
    }

    [Theory]
    [InlineData("LABEL foo=bar ", "LABEL foo=bar")]
    [InlineData("LABEL foo=bar\t", "LABEL foo=bar")]
    [InlineData("LABEL foo=bar   ", "LABEL foo=bar")]
    public void Label_TrailingWhitespace_Trimmed(string input, string expected)
    {
        LabelInstruction instr = LabelInstruction.Parse(input);
        Assert.Equal(expected, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // COPY instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("COPY src dst ")]
    [InlineData("COPY src dst\t")]
    [InlineData("COPY src dst   ")]
    public void Copy_TrailingWhitespace_NoStandaloneWhitespaceToken(string input)
    {
        CopyInstruction instr = CopyInstruction.Parse(input);
        AssertNoTrailingWhitespaceToken(instr, input);
    }

    [Theory]
    [InlineData("COPY src dst ", "COPY src dst")]
    [InlineData("COPY src dst\t", "COPY src dst")]
    [InlineData("COPY src dst   ", "COPY src dst")]
    public void Copy_TrailingWhitespace_Trimmed(string input, string expected)
    {
        CopyInstruction instr = CopyInstruction.Parse(input);
        Assert.Equal(expected, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // ADD instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("ADD src dst ")]
    [InlineData("ADD src dst\t")]
    [InlineData("ADD src dst   ")]
    public void Add_TrailingWhitespace_NoStandaloneWhitespaceToken(string input)
    {
        AddInstruction instr = AddInstruction.Parse(input);
        AssertNoTrailingWhitespaceToken(instr, input);
    }

    [Theory]
    [InlineData("ADD src dst ", "ADD src dst")]
    [InlineData("ADD src dst\t", "ADD src dst")]
    [InlineData("ADD src dst   ", "ADD src dst")]
    public void Add_TrailingWhitespace_Trimmed(string input, string expected)
    {
        AddInstruction instr = AddInstruction.Parse(input);
        Assert.Equal(expected, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // RUN instruction (shell form)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("RUN echo hello ")]
    [InlineData("RUN echo hello\t")]
    [InlineData("RUN echo hello   ")]
    public void Run_ShellForm_TrailingWhitespace_NoStandaloneWhitespaceToken(string input)
    {
        RunInstruction instr = RunInstruction.Parse(input);
        AssertNoTrailingWhitespaceToken(instr, input);
    }

    [Theory]
    [InlineData("RUN echo hello ")]
    [InlineData("RUN echo hello\t")]
    [InlineData("RUN echo hello   ")]
    public void Run_ShellForm_TrailingWhitespace_RoundTrip(string input)
    {
        // Shell-form RUN commands use ArgumentListAsLiteral which embeds whitespace inside
        // the LiteralToken value. AbsorbTrailingWhitespace only removes top-level WhitespaceTokens,
        // so shell-form RUN round-trips with trailing whitespace preserved.
        RunInstruction instr = RunInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // GenericInstruction (uses known instruction keywords parsed generically)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("run echo hello ")]
    [InlineData("run echo hello\t")]
    [InlineData("run echo hello   ")]
    public void Generic_TrailingWhitespace_NoStandaloneWhitespaceToken(string input)
    {
        GenericInstruction instr = GenericInstruction.Parse(input);
        AssertNoTrailingWhitespaceToken(instr, input);
    }

    [Theory]
    [InlineData("run echo hello ", "run echo hello")]
    [InlineData("run echo hello\t", "run echo hello")]
    [InlineData("run echo hello   ", "run echo hello")]
    public void Generic_TrailingWhitespace_Trimmed(string input, string expected)
    {
        GenericInstruction instr = GenericInstruction.Parse(input);
        Assert.Equal(expected, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // Dockerfile-embedded parse (multi-line context)
    // -----------------------------------------------------------------------

    [Fact]
    public void Dockerfile_FromWithTrailingWhitespace_NoStandaloneWhitespaceToken()
    {
        string text = "FROM alpine   \nRUN echo hello\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        FromInstruction from = dockerfile.Items.OfType<FromInstruction>().First();
        AssertNoTrailingWhitespaceToken(from, "FROM alpine   \n");
        Assert.Equal("FROM alpine\nRUN echo hello\n", dockerfile.ToString());
    }

    [Fact]
    public void Dockerfile_MultipleInstructionsWithTrailingWhitespace_Trimmed()
    {
        string text = "FROM alpine \nCOPY src dst \nRUN echo ok \n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        // FROM and COPY have trailing whitespace as top-level instruction tokens (trimmed).
        // RUN shell-form embeds trailing whitespace inside the literal value (preserved).
        Assert.Equal("FROM alpine\nCOPY src dst\nRUN echo ok \n", dockerfile.ToString());
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Asserts that the instruction's final meaningful token is not a standalone
    /// non-newline WhitespaceToken.  Accepts an optional trailing NewLineToken.
    /// </summary>
    private static void AssertNoTrailingWhitespaceToken(Instruction instr, string inputForDiagnostics)
    {
        List<Token> tokens = instr.Tokens.ToList();

        // Skip trailing NewLineToken if present (that is always expected)
        int lastIndex = tokens.Count - 1;
        if (lastIndex >= 0 && tokens[lastIndex] is NewLineToken)
            lastIndex--;

        if (lastIndex >= 0)
        {
            Token lastMeaningful = tokens[lastIndex];
            Assert.False(
                lastMeaningful is WhitespaceToken && lastMeaningful is not NewLineToken,
                $"Expected no trailing WhitespaceToken at instruction level for input: {inputForDiagnostics.Replace("\n", "\\n").Replace("\t", "\\t")}. " +
                $"Last token is {lastMeaningful.GetType().Name}(\"{lastMeaningful}\").");
        }
    }
}
