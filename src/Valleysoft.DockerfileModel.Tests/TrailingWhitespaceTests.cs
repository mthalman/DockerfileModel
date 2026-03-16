using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Tests verifying that trailing whitespace at the end of instruction argument lines
/// is preserved correctly. Each test covers one or more of:
///   1. Round-trip fidelity: ToString() == original text
///   2. Semantic correctness: value properties (ImageName, etc.) do not include trailing spaces
///   3. Token structure: verified via TokenValidators matching the exact token tree shape
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
    public void From_TrailingWhitespace_LastTokenIsLiteralToken(string input)
    {
        // Trailing whitespace is absorbed into the final literal token rather than
        // emitted as a standalone instruction-level WhitespaceToken.
        FromInstruction instr = FromInstruction.Parse(input);
        Token last = instr.Tokens.Last();
        LiteralToken literal = Assert.IsType<LiteralToken>(last);
        ValidateLiteralWithTrailingWhitespace(literal, "alpine", input.Substring("FROM alpine".Length));
    }

    [Fact]
    public void From_TrailingSpace_TokenValidators()
    {
        // Verify the exact token tree for "FROM alpine " (single trailing space).
        // Token 0: KeywordToken("FROM")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("alpine ") containing StringToken("alpine") and WhitespaceToken(" ")
        FromInstruction instr = FromInstruction.Parse("FROM alpine ");
        Assert.Equal("FROM alpine ", instr.ToString());
        Assert.Equal("alpine", instr.ImageName);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "FROM"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "alpine", " "));
    }

    [Fact]
    public void From_TrailingTab_TokenValidators()
    {
        FromInstruction instr = FromInstruction.Parse("FROM alpine\t");
        Assert.Equal("FROM alpine\t", instr.ToString());
        Assert.Equal("alpine", instr.ImageName);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "FROM"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "alpine", "\t"));
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
    public void From_TrailingWhitespaceBeforeNewline_TokenValidators()
    {
        // "FROM alpine  \n" — the trailing spaces are absorbed into the literal and the
        // newline remains a sibling token.
        // Token 0: KeywordToken("FROM")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("alpine  ")
        // Token 3: NewLineToken("\n")
        FromInstruction instr = FromInstruction.Parse("FROM alpine  \n");
        Assert.Equal("FROM alpine  \n", instr.ToString());
        Assert.Equal("alpine", instr.ImageName);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "FROM"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "alpine", "  "),
            token => ValidateNewLine(token, "\n"));
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

    [Fact]
    public void Env_TrailingSpace_TokenValidators()
    {
        // "ENV FOO=bar " — the trailing whitespace is absorbed into the key-value token.
        // Token 0: KeywordToken("ENV")
        // Token 1: WhitespaceToken(" ")
        // Token 2: KeyValueToken<Variable, LiteralToken>("FOO=bar ")
        //            Variable("FOO") + SymbolToken('=') + LiteralToken("bar") + WhitespaceToken(" ")
        EnvInstruction instr = EnvInstruction.Parse("ENV FOO=bar ");
        Assert.Equal("ENV FOO=bar ", instr.ToString());
        Assert.Equal("bar", instr.Variables[0].Value);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "ENV"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregateWithTrailingWhitespace<KeyValueToken<Variable, LiteralToken>>(token, "FOO=bar", " ",
                token => ValidateIdentifier<Variable>(token, "FOO"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "bar")));
    }

    [Fact]
    public void Env_TrailingTab_TokenValidators()
    {
        EnvInstruction instr = EnvInstruction.Parse("ENV FOO=bar\t");
        Assert.Equal("ENV FOO=bar\t", instr.ToString());
        Assert.Equal("bar", instr.Variables[0].Value);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "ENV"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregateWithTrailingWhitespace<KeyValueToken<Variable, LiteralToken>>(token, "FOO=bar", "\t",
                token => ValidateIdentifier<Variable>(token, "FOO"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "bar")));
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

    [Fact]
    public void Label_TrailingSpace_TokenValidators()
    {
        // "LABEL foo=bar " — the trailing whitespace is absorbed into the key-value token.
        // Token 0: KeywordToken("LABEL")
        // Token 1: WhitespaceToken(" ")
        // Token 2: KeyValueToken<LabelKeyToken, LiteralToken>("foo=bar ")
        //            LabelKeyToken("foo") + SymbolToken('=') + LiteralToken("bar") + WhitespaceToken(" ")
        LabelInstruction instr = LabelInstruction.Parse("LABEL foo=bar ");
        Assert.Equal("LABEL foo=bar ", instr.ToString());
        Assert.Equal("bar", instr.Labels[0].Value);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "LABEL"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregateWithTrailingWhitespace<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "foo=bar", " ",
                token => ValidateIdentifier<LabelKeyToken>(token, "foo"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "bar")));
    }

    // -----------------------------------------------------------------------
    // ARG instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("ARG MYARG ")]
    [InlineData("ARG MYARG\t")]
    [InlineData("ARG MYARG   ")]
    public void Arg_TrailingWhitespace_RoundTrip(string input)
    {
        ArgInstruction instr = ArgInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    [Fact]
    public void Arg_TrailingSpace_TokenValidators()
    {
        // "ARG MYARG " — the trailing whitespace is absorbed into the argument declaration.
        // Token 0: KeywordToken("ARG")
        // Token 1: WhitespaceToken(" ")
        // Token 2: ArgDeclaration("MYARG ") containing Variable("MYARG") and WhitespaceToken(" ")
        ArgInstruction instr = ArgInstruction.Parse("ARG MYARG ");
        Assert.Equal("ARG MYARG ", instr.ToString());
        Assert.Equal("MYARG", instr.Args[0].Key);
        Assert.Null(instr.Args[0].Value);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "ARG"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregateWithTrailingWhitespace<ArgDeclaration>(token, "MYARG", " ",
                token => ValidateIdentifier<Variable>(token, "MYARG")));
    }

    [Fact]
    public void Arg_WithDefaultValue_TrailingSpace_TokenValidators()
    {
        // "ARG FOO=bar " — the trailing whitespace is absorbed into the argument declaration.
        // Token 0: KeywordToken("ARG")
        // Token 1: WhitespaceToken(" ")
        // Token 2: ArgDeclaration("FOO=bar ") containing Variable("FOO"), '=', LiteralToken("bar"), WhitespaceToken(" ")
        ArgInstruction instr = ArgInstruction.Parse("ARG FOO=bar ");
        Assert.Equal("ARG FOO=bar ", instr.ToString());
        Assert.Equal("FOO", instr.Args[0].Key);
        Assert.Equal("bar", instr.Args[0].Value);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "ARG"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregateWithTrailingWhitespace<ArgDeclaration>(token, "FOO=bar", " ",
                token => ValidateIdentifier<Variable>(token, "FOO"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "bar")));
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

    [Fact]
    public void Copy_TrailingSpace_TokenValidators()
    {
        // "COPY src dst " — the trailing whitespace is absorbed into the destination token.
        // Token 0: KeywordToken("COPY")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("src")
        // Token 3: WhitespaceToken(" ")
        // Token 4: LiteralToken("dst ") containing StringToken("dst") and WhitespaceToken(" ")
        CopyInstruction instr = CopyInstruction.Parse("COPY src dst ");
        Assert.Equal("COPY src dst ", instr.ToString());
        Assert.Equal(new string[] { "src" }, instr.Sources.ToArray());
        Assert.Equal("dst", instr.Destination);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "COPY"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteral(token, "src"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "dst", " "));
    }

    [Fact]
    public void Copy_TrailingTab_TokenValidators()
    {
        CopyInstruction instr = CopyInstruction.Parse("COPY src dst\t");
        Assert.Equal("COPY src dst\t", instr.ToString());
        Assert.Equal("dst", instr.Destination);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "COPY"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteral(token, "src"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "dst", "\t"));
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

    [Fact]
    public void Add_TrailingSpace_TokenValidators()
    {
        // "ADD src dst " — the trailing whitespace is absorbed into the destination token.
        // Token 0: KeywordToken("ADD")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("src")
        // Token 3: WhitespaceToken(" ")
        // Token 4: LiteralToken("dst ") containing StringToken("dst") and WhitespaceToken(" ")
        AddInstruction instr = AddInstruction.Parse("ADD src dst ");
        Assert.Equal("ADD src dst ", instr.ToString());
        Assert.Equal(new string[] { "src" }, instr.Sources.ToArray());
        Assert.Equal("dst", instr.Destination);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "ADD"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteral(token, "src"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "dst", " "));
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

    [Fact]
    public void Expose_TrailingSpace_TokenValidators()
    {
        // "EXPOSE 80 " — the trailing whitespace is absorbed into the port token.
        // Token 0: KeywordToken("EXPOSE")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("80 ") containing StringToken("80") and WhitespaceToken(" ")
        ExposeInstruction instr = ExposeInstruction.Parse("EXPOSE 80 ");
        Assert.Equal("EXPOSE 80 ", instr.ToString());
        Assert.Equal("80", instr.Ports[0]);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "EXPOSE"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "80", " "));
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

    [Fact]
    public void Workdir_TrailingSpace_TokenValidators()
    {
        // "WORKDIR /app " — trailing whitespace is absorbed into the PathToken's LiteralToken
        // as a child WhitespaceToken, preserving round-trip fidelity.
        // Token 0: KeywordToken("WORKDIR")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("/app ") containing StringToken("/app") and WhitespaceToken(" ")
        WorkdirInstruction instr = WorkdirInstruction.Parse("WORKDIR /app ");
        Assert.Equal("WORKDIR /app ", instr.ToString());
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "WORKDIR"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<LiteralToken>(token, "/app ",
                token => ValidateString(token, "/app"),
                token => ValidateWhitespace(token, " ")));
    }

    // -----------------------------------------------------------------------
    // USER instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("USER name ")]
    [InlineData("USER name\t")]
    [InlineData("USER name   ")]
    public void User_TrailingWhitespace_RoundTrip(string input)
    {
        UserInstruction instr = UserInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    [Fact]
    public void User_TrailingSpace_TokenValidators()
    {
        // "USER name " — the trailing whitespace is absorbed into the user token.
        // Token 0: KeywordToken("USER")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("name ") containing StringToken("name") and WhitespaceToken(" ")
        UserInstruction instr = UserInstruction.Parse("USER name ");
        Assert.Equal("USER name ", instr.ToString());
        Assert.Equal("name", instr.User);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "USER"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "name", " "));
    }

    // -----------------------------------------------------------------------
    // STOPSIGNAL instruction
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("STOPSIGNAL SIGTERM ")]
    [InlineData("STOPSIGNAL SIGTERM\t")]
    [InlineData("STOPSIGNAL SIGTERM   ")]
    public void StopSignal_TrailingWhitespace_RoundTrip(string input)
    {
        StopSignalInstruction instr = StopSignalInstruction.Parse(input);
        Assert.Equal(input, instr.ToString());
    }

    [Fact]
    public void StopSignal_TrailingSpace_TokenValidators()
    {
        // "STOPSIGNAL SIGTERM " — the trailing whitespace is absorbed into the signal token.
        // Token 0: KeywordToken("STOPSIGNAL")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("SIGTERM ") containing StringToken("SIGTERM") and WhitespaceToken(" ")
        StopSignalInstruction instr = StopSignalInstruction.Parse("STOPSIGNAL SIGTERM ");
        Assert.Equal("STOPSIGNAL SIGTERM ", instr.ToString());
        Assert.Equal("SIGTERM", instr.Signal);
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "STOPSIGNAL"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "SIGTERM", " "));
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

    [Fact]
    public void Run_ShellForm_TrailingSpace_TokenValidators()
    {
        // "RUN echo hello " — trailing whitespace is absorbed into the ShellFormCommand's
        // LiteralToken as part of its string value, preserving round-trip fidelity.
        // Token 0: KeywordToken("RUN")
        // Token 1: WhitespaceToken(" ")
        // Token 2: ShellFormCommand("echo hello ") containing LiteralToken("echo hello ")
        //            LiteralToken contains StringToken("echo hello ")
        RunInstruction instr = RunInstruction.Parse("RUN echo hello ");
        Assert.Equal("RUN echo hello ", instr.ToString());
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "RUN"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<ShellFormCommand>(token, "echo hello ",
                token => ValidateLiteral(token, "echo hello ")));
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

    [Fact]
    public void Generic_TrailingSpace_TokenValidators()
    {
        // "run echo hello " — the trailing whitespace is absorbed into the final literal token.
        // Token 0: KeywordToken("run")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("echo hello ") containing StringToken("echo hello") and WhitespaceToken(" ")
        GenericInstruction instr = GenericInstruction.Parse("run echo hello ");
        Assert.Equal("run echo hello ", instr.ToString());
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "run"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "echo hello", " "));
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
    public void Env_TrailingSpace_VariableRef_TokenValidators()
    {
        // "ENV FOO=$BAR " — the trailing whitespace is absorbed into the key-value token.
        // Token 0: KeywordToken("ENV")
        // Token 1: WhitespaceToken(" ")
        // Token 2: KeyValueToken<Variable, LiteralToken>("FOO=$BAR ")
        //            Variable("FOO") + SymbolToken('=') + LiteralToken("$BAR") + WhitespaceToken(" ")
        //              LiteralToken contains VariableRefToken("$BAR")
        EnvInstruction instr = EnvInstruction.Parse("ENV FOO=$BAR ");
        Assert.Equal("ENV FOO=$BAR ", instr.ToString());
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "ENV"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregateWithTrailingWhitespace<KeyValueToken<Variable, LiteralToken>>(token, "FOO=$BAR", " ",
                token => ValidateIdentifier<Variable>(token, "FOO"),
                token => ValidateSymbol(token, '='),
                token => ValidateAggregate<LiteralToken>(token, "$BAR",
                    token => ValidateAggregate<VariableRefToken>(token, "$BAR",
                        token => ValidateString(token, "BAR")))));
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

    [Fact]
    public void From_TrailingSpace_VariableRef_TokenValidators()
    {
        // "FROM $BASE " — the trailing whitespace is absorbed into the literal token.
        // Token 0: KeywordToken("FROM")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("$BASE ") containing VariableRefToken("$BASE") and WhitespaceToken(" ")
        FromInstruction instr = FromInstruction.Parse("FROM $BASE ");
        Assert.Equal("FROM $BASE ", instr.ToString());
        Assert.Collection(instr.Tokens,
            token => ValidateKeyword(token, "FROM"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregateWithTrailingWhitespace<LiteralToken>(token, "$BASE", " ",
                token => ValidateAggregate<VariableRefToken>(token, "$BASE",
                    token => ValidateString(token, "BASE"))));
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

    [Fact]
    public void Dockerfile_FromWithTrailingWhitespace_TokenValidators()
    {
        // Within a full Dockerfile parse, "FROM alpine   " followed by newline
        // should absorb the trailing spaces into the literal and keep the newline separate.
        // Token 0: KeywordToken("FROM")
        // Token 1: WhitespaceToken(" ")
        // Token 2: LiteralToken("alpine   ")
        // Token 3: NewLineToken("\n")
        string text = "FROM alpine   \nRUN echo hello\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        FromInstruction from = dockerfile.Items.OfType<FromInstruction>().First();
        Assert.Equal("FROM alpine   \n", from.ToString());
        Assert.Equal("alpine", from.ImageName);
        Assert.Collection(from.Tokens,
            token => ValidateKeyword(token, "FROM"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteralWithTrailingWhitespace(token, "alpine", "   "),
            token => ValidateNewLine(token, "\n"));
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
