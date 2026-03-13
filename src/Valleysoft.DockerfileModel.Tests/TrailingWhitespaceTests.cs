using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Tests verifying that trailing whitespace at the end of instruction argument lines
/// is preserved as a standalone WhitespaceToken at instruction level (not embedded in
/// a content token's string value). Each test covers one or more of:
///   1. Round-trip fidelity: ToString() == original text
///   2. Semantic correctness: value properties (ImageName, etc.) do not include trailing spaces
///   3. Token structure: the final instruction-level token is a WhitespaceToken (not a content token)
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
    public void From_TrailingWhitespace_RoundTrip(string input)
    {
        FromInstruction instr = FromInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    [Theory]
    [InlineData("FROM alpine ")]
    [InlineData("FROM alpine\t")]
    [InlineData("FROM alpine   ")]
    public void From_TrailingWhitespace_ImageNameNotCorrupted(string input)
    {
        FromInstruction instr = FromInstruction.Parse(input);
        Assert.Equal("alpine", instr.ImageName);
    }

    [Theory]
    [InlineData("FROM alpine ")]
    [InlineData("FROM alpine\t")]
    [InlineData("FROM alpine   ")]
    public void From_TrailingWhitespace_LastTokenIsWhitespaceToken(string input)
    {
        // Trailing whitespace should be a standalone WhitespaceToken at instruction level,
        // not embedded in the preceding content token's string value.
        FromInstruction instr = FromInstruction.Parse(input);
        Token last = instr.Tokens.Last();
        Assert.IsType<WhitespaceToken>(last);
        Assert.IsNotType<NewLineToken>(last);
    }

    [Fact]
    public void From_NoTrailingWhitespace_Unchanged()
    {
        FromInstruction instr = FromInstruction.Parse("FROM alpine");
        Assert.Equal("FROM alpine", instr.ToString());
        Assert.Equal("alpine", instr.ImageName);
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
    public void From_TrailingWhitespaceBeforeNewline_RoundTrip()
    {
        FromInstruction instr = FromInstruction.Parse("FROM alpine  \n");
        Assert.Equal("FROM alpine  \n", instr.ToString());
        Assert.Equal("alpine", instr.ImageName);
    }

    // -----------------------------------------------------------------------
    // ENV instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("ENV FOO=bar ")]
    [InlineData("ENV FOO=bar\t")]
    [InlineData("ENV FOO=bar   ")]
    public void Env_TrailingWhitespace_RoundTrip(string input)
    {
        EnvInstruction instr = EnvInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // EXPOSE instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("EXPOSE 80 ")]
    [InlineData("EXPOSE 80\t")]
    [InlineData("EXPOSE 80   ")]
    public void Expose_TrailingWhitespace_RoundTrip(string input)
    {
        ExposeInstruction instr = ExposeInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // WORKDIR instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("WORKDIR /app ")]
    [InlineData("WORKDIR /app\t")]
    [InlineData("WORKDIR /app   ")]
    public void Workdir_TrailingWhitespace_RoundTrip(string input)
    {
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
    public void Label_TrailingWhitespace_RoundTrip(string input)
    {
        LabelInstruction instr = LabelInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // COPY instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("COPY src dst ")]
    [InlineData("COPY src dst\t")]
    [InlineData("COPY src dst   ")]
    public void Copy_TrailingWhitespace_RoundTrip(string input)
    {
        CopyInstruction instr = CopyInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // ADD instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("ADD src dst ")]
    [InlineData("ADD src dst\t")]
    [InlineData("ADD src dst   ")]
    public void Add_TrailingWhitespace_RoundTrip(string input)
    {
        AddInstruction instr = AddInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // RUN instruction (shell form)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("RUN echo hello ")]
    [InlineData("RUN echo hello\t")]
    [InlineData("RUN echo hello   ")]
    public void Run_ShellForm_TrailingWhitespace_RoundTrip(string input)
    {
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
    public void Generic_TrailingWhitespace_RoundTrip(string input)
    {
        GenericInstruction instr = GenericInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    // -----------------------------------------------------------------------
    // VariableRefToken — trailing whitespace must not corrupt the variable name
    // -----------------------------------------------------------------------

    [Fact]
    public void Env_TrailingWhitespace_VariableRef_RoundTrip()
    {
        EnvInstruction instr = EnvInstruction.Parse("ENV FOO=$BAR ");
        Assert.Equal("ENV FOO=$BAR ", instr.ToString());
    }

    [Fact]
    public void Env_TrailingWhitespace_VariableRef_VariableNameNotCorrupted()
    {
        EnvInstruction instr = EnvInstruction.Parse("ENV FOO=$BAR ");
        VariableRefToken? varRef = FindFirstVariableRefToken(instr.Tokens);
        Assert.NotNull(varRef);
        Assert.Equal("BAR", varRef.VariableName);
    }

    [Fact]
    public void From_TrailingWhitespace_VariableRef_RoundTrip()
    {
        // FROM with a variable as image name (e.g. FROM $BASE_IMAGE )
        FromInstruction instr = FromInstruction.Parse("FROM $BASE ");
        Assert.Equal("FROM $BASE ", instr.ToString());
    }

    [Fact]
    public void From_TrailingWhitespace_VariableRef_VariableNameNotCorrupted()
    {
        FromInstruction instr = FromInstruction.Parse("FROM $BASE ");
        VariableRefToken? varRef = FindFirstVariableRefToken(instr.Tokens);
        Assert.NotNull(varRef);
        Assert.Equal("BASE", varRef.VariableName);
    }

    // -----------------------------------------------------------------------
    // Dockerfile-embedded parse (multi-line context)
    // -----------------------------------------------------------------------

    [Fact]
    public void Dockerfile_FromWithTrailingWhitespace_RoundTrip()
    {
        string text = "FROM alpine   \nRUN echo hello\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        Assert.Equal(text, dockerfile.ToString());
        FromInstruction from = dockerfile.Items.OfType<FromInstruction>().First();
        Assert.Equal("alpine", from.ImageName);
    }

    [Fact]
    public void Dockerfile_MultipleInstructionsWithTrailingWhitespace_RoundTrip()
    {
        string text = "FROM alpine \nCOPY src dst \nRUN echo ok \n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        Assert.Equal(text, dockerfile.ToString());
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Recursively searches a token sequence (including children of AggregateTokens) and
    /// returns the first VariableRefToken found, or null if none is present.
    /// </summary>
    private static VariableRefToken? FindFirstVariableRefToken(IEnumerable<Token> tokens)
    {
        foreach (Token token in tokens)
        {
            if (token is VariableRefToken vr)
                return vr;
            if (token is AggregateToken agg)
            {
                VariableRefToken? found = FindFirstVariableRefToken(agg.Tokens);
                if (found is not null)
                    return found;
            }
        }
        return null;
    }
}
