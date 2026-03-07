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
            changeOwner: new UserAccount("user"),
            link: true,
            escapeChar: Dockerfile.DefaultEscapeChar);
        Assert.True(instruction.Link);
        Assert.Equal("user", instruction.ChangeOwner.User);
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
                        token => ValidateAggregate<UserAccount>(token, "id",
                            token => ValidateLiteral(token, "id"))),
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
                    Assert.Equal("id", result.ChangeOwner.User);
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
                        token => ValidateAggregate<UserAccount>(token, "user:group",
                            token => ValidateLiteral(token, "user"),
                            token => ValidateSymbol(token, ':'),
                            token => ValidateLiteral(token, "group"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "src"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "dst")
                },
                Validate = result =>
                {
                    Assert.True(result.Link);
                    Assert.Equal("user", result.ChangeOwner.User);
                    Assert.Equal("group", result.ChangeOwner.Group);
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
                        token => ValidateAggregate<UserAccount>(token, "user",
                            token => ValidateLiteral(token, "user"))),
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
                    Assert.Equal("user", result.ChangeOwner.User);
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

    private static void ValidateFromFlag(Token token, string key, string value)
    {
        ValidateAggregate<FromFlag>(token, $"--{key}={value}",
            token => ValidateSymbol(token, '-'),
            token => ValidateSymbol(token, '-'),
            token => ValidateKeyword(token, key),
            token => ValidateSymbol(token, '='),
            token => ValidateLiteral(token, value));
    }

}
