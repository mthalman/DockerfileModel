using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class CopyInstructionTests : FileTransferInstructionTests<CopyInstruction>
{
    public CopyInstructionTests()
        : base("COPY", CopyInstruction.Parse,
                (sources, destination, changeOwner, permissions, escapeChar) =>
                new CopyInstruction(sources, destination, changeOwner: changeOwner, permissions: permissions, escapeChar: escapeChar))
    {
    }

    [Theory]
    [MemberData(nameof(ParseTestInputBase))]
    public void ParseBase(ParseTestScenario<CopyInstruction> scenario) => RunParseTest(scenario);

    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<CopyInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, CopyInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInputBase))]
    public void CreateBase(CreateTestScenario scenario) => RunCreateTest(scenario);

    [Fact]
    public void FromStageName()
    {
        static void Validate(CopyInstruction instruction, string stage)
        {
            Assert.Equal(stage, instruction.FromStageName);
            Assert.Equal(stage, instruction.FromStageNameToken.Value);
            Assert.Equal($"COPY --from={stage} src dst", instruction.ToString());
        }

        CopyInstruction instruction = new(new string[] { "src" }, "dst", "test", escapeChar: Dockerfile.DefaultEscapeChar);
        Validate(instruction, "test");

        instruction.FromStageName = "test2";
        Validate(instruction, "test2");

        instruction.FromStageName = null;
        Assert.Null(instruction.FromStageName);
        Assert.Null(instruction.FromStageNameToken);
        Assert.Equal($"COPY src dst", instruction.ToString());

        instruction = CopyInstruction.Parse($"COPY`\n src dst", '`');
        instruction.FromStageName = "test3";
        Assert.Equal("test3", instruction.FromStageName);
        Assert.Equal($"COPY --from=test3`\n src dst", instruction.ToString());

        instruction = CopyInstruction.Parse($"COPY`\n --from=stage`\n src dst", '`');
        instruction.FromStageName = null;
        Assert.Null(instruction.FromStageName);
        Assert.Null(instruction.FromStageNameToken);
        Assert.Equal($"COPY`\n`\n src dst", instruction.ToString());
    }

    [Theory]
    [InlineData("COPY --from=$VAR src dst", "$VAR")]
    [InlineData("COPY --from=${VAR} src dst", "${VAR}")]
    public void FromFlag_NoVariableExpansion(string text, string expectedFlagValue)
    {
        CopyInstruction instruction = CopyInstruction.Parse(text);

        // The --from flag should be parsed as a FromFlag
        FromFlag fromFlag = instruction.Tokens.OfType<FromFlag>().Single();

        // The flag value should be a LiteralToken
        LiteralToken valueToken = fromFlag.ValueToken;
        Assert.NotNull(valueToken);
        Assert.Equal(expectedFlagValue, valueToken.Value);

        // The literal should contain NO VariableRefToken children — $VAR is plain string text
        Assert.Empty(valueToken.Tokens.OfType<VariableRefToken>());

        // Round-trip fidelity
        Assert.Equal(text, instruction.ToString());
    }

    [Fact]
    public void Link()
    {
        // Verify Link is false by default when not specified
        CopyInstruction instruction = new(new string[] { "src" }, "dst", escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.False(instruction.Link);
        Assert.Equal("COPY src dst", instruction.ToString());

        // Set Link = true via property — instruction should gain the --link flag
        instruction.Link = true;
        Assert.True(instruction.Link);
        Assert.Equal("COPY --link src dst", instruction.ToString());

        // Set Link = false — flag should be removed
        instruction.Link = false;
        Assert.False(instruction.Link);
        Assert.Equal("COPY src dst", instruction.ToString());

        // Construct with link = true directly in the constructor
        instruction = new(new string[] { "src" }, "dst", link: true, escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Link);
        Assert.Equal("COPY --link src dst", instruction.ToString());

        // Toggle off again
        instruction.Link = false;
        Assert.False(instruction.Link);
        Assert.Equal("COPY src dst", instruction.ToString());

        // Parse from text with line-continuation escape and then set Link
        instruction = CopyInstruction.Parse($"COPY`\n src dst", '`');
        instruction.Link = true;
        Assert.True(instruction.Link);
        Assert.Equal($"COPY --link`\n src dst", instruction.ToString());

        // Parse with --link already present and remove it
        instruction = CopyInstruction.Parse($"COPY`\n --link`\n src dst", '`');
        instruction.Link = false;
        Assert.False(instruction.Link);
        Assert.Equal($"COPY`\n`\n src dst", instruction.ToString());
    }

    [Fact]
    public void Link_ExplicitTrue()
    {
        // Parse --link=true
        CopyInstruction instruction = CopyInstruction.Parse("COPY --link=true src dst");
        Assert.True(instruction.Link);
        Assert.Equal("COPY --link=true src dst", instruction.ToString());

        // The LinkFlagToken should have BoolValue = true
        Assert.NotNull(instruction.LinkFlagToken);
        Assert.True(instruction.LinkFlagToken!.BoolValue);
        Assert.Equal("true", instruction.LinkFlagToken.Value);
    }

    [Fact]
    public void Link_ExplicitFalse()
    {
        // Parse --link=false
        CopyInstruction instruction = CopyInstruction.Parse("COPY --link=false src dst");
        Assert.False(instruction.Link);
        Assert.Equal("COPY --link=false src dst", instruction.ToString());

        // The LinkFlagToken should have BoolValue = false
        Assert.NotNull(instruction.LinkFlagToken);
        Assert.False(instruction.LinkFlagToken!.BoolValue);
        Assert.Equal("false", instruction.LinkFlagToken.Value);

        // Setting Link = true should replace the =false flag with a bare flag
        instruction.Link = true;
        Assert.True(instruction.Link);
        Assert.Equal("COPY --link src dst", instruction.ToString());
    }

    [Theory]
    [InlineData("COPY --link=True src dst", true)]
    [InlineData("COPY --link=FALSE src dst", false)]
    public void Link_CaseInsensitive(string text, bool expectedValue)
    {
        CopyInstruction instruction = CopyInstruction.Parse(text);
        Assert.Equal(expectedValue, instruction.Link);
        Assert.Equal(text, instruction.ToString());
    }

    [Fact]
    public void Link_WithFromStageName()
    {
        // --link and --from together
        CopyInstruction instruction = new(new string[] { "src" }, "dst", fromStageName: "base", link: true, escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Link);
        Assert.Equal("base", instruction.FromStageName);
        Assert.Equal("COPY --from=base --link src dst", instruction.ToString());
    }

    [Fact]
    public void Link_WithChown()
    {
        // --link and --chown together
        CopyInstruction instruction = new(
            new string[] { "src" }, "dst",
            changeOwner: "user",
            link: true,
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Link);
        Assert.Equal("user", instruction.ChangeOwner);
        Assert.Equal("COPY --chown=user --link src dst", instruction.ToString());
    }

    [Fact]
    public void Link_WithChmod()
    {
        // --link and --chmod together
        CopyInstruction instruction = new(
            new string[] { "src" }, "dst",
            permissions: "755",
            link: true,
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Link);
        Assert.Equal("755", instruction.Permissions);
        Assert.Equal("COPY --chmod=755 --link src dst", instruction.ToString());
    }

    [Fact]
    public void Parents()
    {
        // Verify Parents is false by default when not specified
        CopyInstruction instruction = new(new string[] { "src" }, "dst", escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.False(instruction.Parents);
        Assert.Equal("COPY src dst", instruction.ToString());

        // Set Parents = true via property — instruction should gain the --parents flag
        instruction.Parents = true;
        Assert.True(instruction.Parents);
        Assert.Equal("COPY --parents src dst", instruction.ToString());

        // Set Parents = false — flag should be removed
        instruction.Parents = false;
        Assert.False(instruction.Parents);
        Assert.Equal("COPY src dst", instruction.ToString());

        // Construct with parents = true directly in the constructor
        instruction = new(new string[] { "src" }, "dst", parents: true, escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Parents);
        Assert.Equal("COPY --parents src dst", instruction.ToString());

        // Toggle off again
        instruction.Parents = false;
        Assert.False(instruction.Parents);
        Assert.Equal("COPY src dst", instruction.ToString());

        // Parse from text with line-continuation escape and then set Parents
        instruction = CopyInstruction.Parse($"COPY`\n src dst", '`');
        instruction.Parents = true;
        Assert.True(instruction.Parents);
        Assert.Equal($"COPY --parents`\n src dst", instruction.ToString());

        // Parse with --parents already present and remove it
        instruction = CopyInstruction.Parse($"COPY`\n --parents`\n src dst", '`');
        instruction.Parents = false;
        Assert.False(instruction.Parents);
        Assert.Equal($"COPY`\n`\n src dst", instruction.ToString());
    }

    [Fact]
    public void Parents_WithLink()
    {
        // --parents and --link together
        CopyInstruction instruction = new(new string[] { "src" }, "dst", link: true, parents: true, escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Parents);
        Assert.True(instruction.Link);
        Assert.Equal("COPY --link --parents src dst", instruction.ToString());
    }

    [Fact]
    public void Parents_WithFromStageName()
    {
        // --parents and --from together
        CopyInstruction instruction = new(new string[] { "src" }, "dst", fromStageName: "base", parents: true, escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Parents);
        Assert.Equal("base", instruction.FromStageName);
        Assert.Equal("COPY --from=base --parents src dst", instruction.ToString());
    }

    [Fact]
    public void Exclude_Single()
    {
        // Parse a single --exclude flag
        CopyInstruction instruction = CopyInstruction.Parse("COPY --exclude=*.txt src /app");
        Assert.Single(instruction.Excludes);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal("COPY --exclude=*.txt src /app", instruction.ToString());
    }

    [Fact]
    public void Exclude_Multiple()
    {
        // Parse multiple --exclude flags
        CopyInstruction instruction = CopyInstruction.Parse("COPY --exclude=*.txt --exclude=*.log src /app");
        Assert.Equal(2, instruction.Excludes.Count);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal("*.log", instruction.Excludes[1]);
        Assert.Equal("COPY --exclude=*.txt --exclude=*.log src /app", instruction.ToString());
    }

    [Fact]
    public void Exclude_WithOtherFlags()
    {
        // --exclude with --from, --link, and --parents
        CopyInstruction instruction = CopyInstruction.Parse("COPY --from=stage --link --parents --exclude=*.txt src /app");
        Assert.Equal("stage", instruction.FromStageName);
        Assert.True(instruction.Link);
        Assert.True(instruction.Parents);
        Assert.Single(instruction.Excludes);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal("COPY --from=stage --link --parents --exclude=*.txt src /app", instruction.ToString());
    }

    [Fact]
    public void Exclude_Constructor()
    {
        // Construct with excludes in constructor
        CopyInstruction instruction = new(
            new string[] { "src" }, "/app",
            excludes: new string[] { "*.txt", "*.log" },
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.Equal(2, instruction.Excludes.Count);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal("*.log", instruction.Excludes[1]);
        Assert.Equal("COPY --exclude=*.txt --exclude=*.log src /app", instruction.ToString());
    }

    [Fact]
    public void Exclude_RoundTrip_WithLineContinuation()
    {
        // Round-trip with line continuation
        string text = "COPY --exclude=*.txt `\n src /app";
        CopyInstruction instruction = CopyInstruction.Parse(text, '`');
        Assert.Single(instruction.Excludes);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal(text, instruction.ToString());
    }

    [Fact]
    public void Exclude_VariableValue()
    {
        // --exclude with a variable reference
        CopyInstruction instruction = CopyInstruction.Parse("COPY --exclude=$PATTERN src /app");
        Assert.Single(instruction.Excludes);
        Assert.Equal("$PATTERN", instruction.Excludes[0]);
        Assert.Equal("COPY --exclude=$PATTERN src /app", instruction.ToString());
    }

    [Fact]
    public void AllFlags_Together()
    {
        // All flags: --from, --chown, --chmod, --link, --parents, --exclude (multiple)
        CopyInstruction instruction = CopyInstruction.Parse(
            "COPY --from=stage --chown=user --chmod=755 --link --parents --exclude=*.txt --exclude=*.log src /app");
        Assert.Equal("stage", instruction.FromStageName);
        Assert.Equal("user", instruction.ChangeOwner);
        Assert.Equal("755", instruction.Permissions);
        Assert.True(instruction.Link);
        Assert.True(instruction.Parents);
        Assert.Equal(2, instruction.Excludes.Count);
        Assert.Equal("*.txt", instruction.Excludes[0]);
        Assert.Equal("*.log", instruction.Excludes[1]);
        Assert.Equal(
            "COPY --from=stage --chown=user --chmod=755 --link --parents --exclude=*.txt --exclude=*.log src /app",
            instruction.ToString());
    }

    [Fact]
    public void FromFlag_LineContinuationInValue()
    {
        // COPY --from=\<newline>builder src /app/ — line continuation inside the flag value
        string text = "COPY --from=\\\nbuilder src /app/";
        CopyInstruction instruction = CopyInstruction.Parse(text);

        // The --from flag should be parsed as a FromFlag (structured keyValue), not a literal fallback
        FromFlag fromFlag = instruction.Tokens.OfType<FromFlag>().Single();
        Assert.IsType<FromFlag>(fromFlag);

        // The flag value should be correctly extracted across the continuation
        Assert.Equal("builder", instruction.FromStageName);
        Assert.NotNull(instruction.FromStageNameToken);
        Assert.Equal("builder", instruction.FromStageNameToken!.Value);

        // The FromFlag token contains the line continuation inside it
        Assert.Collection(fromFlag.Tokens,
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, "from"),
            token => ValidateSymbol(token, '='),
            token => ValidateLineContinuation(token, '\\', "\n"),
            token => ValidateLiteral(token, "builder"));

        // Round-trip fidelity
        Assert.Equal(text, instruction.ToString());
    }

    [Fact]
    public void ChmodFlag_LineContinuationInValue()
    {
        // COPY --chmod=\<newline>755 src /app/ — line continuation inside the chmod flag value
        string text = "COPY --chmod=\\\n755 src /app/";
        CopyInstruction instruction = CopyInstruction.Parse(text);

        // The instruction should parse without error and extract the correct permission value
        Assert.Equal("755", instruction.Permissions);

        // The --chmod flag should be a structured ChangeModeFlag (keyValue token), not a literal fallback
        ChangeModeFlag chmodFlag = instruction.Tokens.OfType<ChangeModeFlag>().Single();
        Assert.IsType<ChangeModeFlag>(chmodFlag);

        // The ChangeModeFlag contains the line continuation inside it
        Assert.Collection(chmodFlag.Tokens,
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, "chmod"),
            token => ValidateSymbol(token, '='),
            token => ValidateLineContinuation(token, '\\', "\n"),
            token => ValidateLiteral(token, "755"));

        // Round-trip fidelity
        Assert.Equal(text, instruction.ToString());
    }

    public static IEnumerable<object[]> ParseTestInputBase() => ParseTestInput("COPY");

    public static IEnumerable<object[]> CreateTestInputBase() => CreateTestInput("COPY");

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<CopyInstruction>[] testInputs = new ParseTestScenario<CopyInstruction>[]
        {
            new ParseTestScenario<CopyInstruction>
            {
                Text = $"COPY --from=stage src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateFromFlag(token, "from", "stage"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("COPY", result.InstructionName);
                    Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                    Assert.Equal("dst", result.Destination);
                    Assert.Equal("stage", result.FromStageName);
                    Assert.False(result.Link);
                }
            },
            new ParseTestScenario<CopyInstruction>
            {
                Text = $"COPY --from=stage --chown=id src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateFromFlag(token, "from", "stage"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeOwnerFlag>(token, "--chown=id",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chown"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "id")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("COPY", result.InstructionName);
                    Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                    Assert.Equal("dst", result.Destination);
                    Assert.Equal("stage", result.FromStageName);
                    Assert.Equal("id", result.ChangeOwner);
                    Assert.False(result.Link);
                }
            },
            // --link flag alone
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --link src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("COPY", result.InstructionName);
                    Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                    Assert.Equal("dst", result.Destination);
                    Assert.True(result.Link);
                    Assert.Null(result.FromStageName);
                }
            },
            // --link with --from
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --from=stage --link src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateFromFlag(token, "from", "stage"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("COPY", result.InstructionName);
                    Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                    Assert.Equal("dst", result.Destination);
                    Assert.Equal("stage", result.FromStageName);
                    Assert.True(result.Link);
                }
            },
            // --link before --from
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --link --from=stage src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateFromFlag(token, "from", "stage"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("COPY", result.InstructionName);
                    Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                    Assert.Equal("dst", result.Destination);
                    Assert.Equal("stage", result.FromStageName);
                    Assert.True(result.Link);
                }
            },
            // --link with --chown
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --link --chown=user:group src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeOwnerFlag>(token, "--chown=user:group",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chown"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "user:group")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    Assert.Equal("user:group", result.ChangeOwner);
                }
            },
            // --link with --chmod
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --link --chmod=755 src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeModeFlag>(token, "--chmod=755",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chmod"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "755")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    Assert.Equal("755", result.Permissions);
                }
            },
            // --link with --from, --chown, and --chmod all together
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --from=stage --link --chown=user --chmod=755 src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateFromFlag(token, "from", "stage"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeOwnerFlag>(token, "--chown=user",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chown"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "user")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ChangeModeFlag>(token, "--chmod=755",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "chmod"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "755")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    Assert.Equal("stage", result.FromStageName);
                    Assert.Equal("user", result.ChangeOwner);
                    Assert.Equal("755", result.Permissions);
                }
            },
            // Round-trip: --link with line continuation and whitespace
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --link `\n src dst",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    // Round-trip: ToString must reproduce the original text exactly
                    Assert.Equal("COPY --link `\n src dst", result.ToString());
                }
            },
            // --link=true (explicit true)
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --link=true src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlagWithValue(token, "true"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    Assert.Equal("COPY --link=true src dst", result.ToString());
                }
            },
            // --link=false (explicit false)
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --link=false src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlagWithValue(token, "false"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.False(result.Link);
                    Assert.Equal("COPY --link=false src dst", result.ToString());
                }
            },
            // --link=True (case-insensitive)
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --link=True src dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlagWithValue(token, "True"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    Assert.Equal("COPY --link=True src dst", result.ToString());
                }
            },
            // Multiple sources with --link
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --link src1 src2 dst",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src1"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src2"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    Assert.Equal(new string[] { "src1", "src2" }, result.Sources.ToArray());
                    Assert.Equal("dst", result.Destination);
                }
            },
            // --parents flag alone
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --parents src /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateParentsFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("COPY", result.InstructionName);
                    Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                    Assert.Equal("/app", result.Destination);
                    Assert.True(result.Parents);
                    Assert.False(result.Link);
                    Assert.Null(result.FromStageName);
                }
            },
            // --parents with --from
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --from=stage --parents src /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateFromFlag(token, "from", "stage"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateParentsFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.True(result.Parents);
                    Assert.Equal("stage", result.FromStageName);
                }
            },
            // --parents=true (explicit true)
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --parents=true src /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateParentsFlagWithValue(token, "true"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.True(result.Parents);
                    Assert.Equal("COPY --parents=true src /app", result.ToString());
                }
            },
            // --parents=false (explicit false)
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --parents=false src /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateParentsFlagWithValue(token, "false"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.False(result.Parents);
                    Assert.Equal("COPY --parents=false src /app", result.ToString());
                }
            },
            // --exclude flag alone
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --exclude=*.txt src /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateExcludeFlag(token, "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("COPY", result.InstructionName);
                    Assert.Single(result.Excludes);
                    Assert.Equal("*.txt", result.Excludes[0]);
                    Assert.False(result.Link);
                    Assert.False(result.Parents);
                }
            },
            // Multiple --exclude flags
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --exclude=*.txt --exclude=*.log src /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateExcludeFlag(token, "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateExcludeFlag(token, "*.log"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.Equal(2, result.Excludes.Count);
                    Assert.Equal("*.txt", result.Excludes[0]);
                    Assert.Equal("*.log", result.Excludes[1]);
                }
            },
            // All new flags together: --parents, --exclude, --link
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY --link --parents --exclude=*.txt src /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLinkFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateParentsFlag(token),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateExcludeFlag(token, "*.txt"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    Assert.True(result.Parents);
                    Assert.Single(result.Excludes);
                    Assert.Equal("*.txt", result.Excludes[0]);
                }
            },
            // Empty exec-form array []
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY []",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateSymbol(token, '['),
                    token => ValidateSymbol(token, ']')
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("COPY", result.InstructionName);
                    Assert.Empty(result.Sources);
                    Assert.Equal("COPY []", result.ToString());
                }
            },
            // Empty exec-form array with whitespace [ ]
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY [ ]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateSymbol(token, '['),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateSymbol(token, ']')
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("COPY", result.InstructionName);
                    Assert.Empty(result.Sources);
                    Assert.Equal("COPY [ ]", result.ToString());
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    private static void ValidateLinkFlag(Token token)
    {
        ValidateAggregate<LinkFlag>(token, "--link",
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, "link"));
    }

    private static void ValidateLinkFlagWithValue(Token token, string value)
    {
        ValidateAggregate<LinkFlag>(token, $"--link={value}",
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, "link"),
            token => ValidateSymbol(token, '='),
            token => ValidateLiteral(token, value));
    }

    private static void ValidateParentsFlag(Token token)
    {
        ValidateAggregate<ParentsFlag>(token, "--parents",
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, "parents"));
    }

    private static void ValidateParentsFlagWithValue(Token token, string value)
    {
        ValidateAggregate<ParentsFlag>(token, $"--parents={value}",
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, "parents"),
            token => ValidateSymbol(token, '='),
            token => ValidateLiteral(token, value));
    }

    private static void ValidateExcludeFlag(Token token, string value)
    {
        ValidateAggregate<ExcludeFlag>(token, $"--exclude={value}",
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, "exclude"),
            token => ValidateSymbol(token, '='),
            token => ValidateLiteral(token, value));
    }

    private static void ValidateFromFlag(Token token, string key, string value)
    {
        ValidateAggregate<FromFlag>(token, $"--{key}={value}",
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, key),
            token => ValidateSymbol(token, '='),
            token => ValidateLiteral(token, value));
    }

    [Fact]
    public void CopyInstruction_FromByStageNumber_RoundTrips()
    {
        // COPY --from=0 references stage by index number
        string text = "COPY --from=0 /src /dst\n";
        CopyInstruction inst = CopyInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
        Assert.Equal("0", inst.FromStageName);
    }

    [Fact]
    public void CopyInstruction_FromByStageName_RoundTrips()
    {
        string text = "COPY --from=base /src /dst\n";
        CopyInstruction inst = CopyInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
        Assert.Equal("base", inst.FromStageName);
    }

    [Fact]
    public void CopyInstruction_FromWithVariable_RoundTrips()
    {
        string text = "COPY --from=$STAGE /src /dst\n";
        CopyInstruction inst = CopyInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

}
