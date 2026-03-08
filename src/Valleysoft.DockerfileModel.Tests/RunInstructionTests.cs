using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class RunInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<RunInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, RunInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        RunInstruction result;
        if (scenario.Args is null)
        {
            result = new RunInstruction(scenario.Command, scenario.Mounts ?? Enumerable.Empty<Mount>(),
                network: scenario.Network, security: scenario.Security);
        }
        else
        {
            result = new RunInstruction(scenario.Command, scenario.Args, scenario.Mounts ?? Enumerable.Empty<Mount>(),
                network: scenario.Network, security: scenario.Security);
        }

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Mounts()
    {
        RunInstruction instruction = new("echo hello", new Mount[] { Mount.Parse("type=secret,id=id") });
        Assert.Single(instruction.Mounts);
        Assert.Equal("RUN --mount=type=secret,id=id echo hello", instruction.ToString());

        instruction.Mounts[0] = Mount.Parse("type=secret,id=id2");
        Assert.Equal("RUN --mount=type=secret,id=id2 echo hello", instruction.ToString());

        instruction.Mounts[0] = Mount.Parse("type=secret,id=id3");
        Assert.Equal("RUN --mount=type=secret,id=id3 echo hello", instruction.ToString());
    }

    [Fact]
    public void Network()
    {
        RunInstruction instruction = new("echo hello", Enumerable.Empty<Mount>(), network: "host");
        Assert.Equal("host", instruction.Network);
        Assert.Equal("host", instruction.NetworkToken!.Value);
        Assert.Equal("RUN --network=host echo hello", instruction.ToString());

        instruction.Network = "none";
        Assert.Equal("none", instruction.Network);
        Assert.Equal("none", instruction.NetworkToken!.Value);
        Assert.Equal("RUN --network=none echo hello", instruction.ToString());

        instruction.Network = null;
        Assert.Null(instruction.Network);
        Assert.Null(instruction.NetworkToken);
        Assert.Equal("RUN echo hello", instruction.ToString());

        instruction.NetworkToken = new LiteralToken("default");
        Assert.Equal("default", instruction.Network);
        Assert.Equal("default", instruction.NetworkToken.Value);
        Assert.Equal("RUN --network=default echo hello", instruction.ToString());

        instruction.NetworkToken.Value = "host";
        Assert.Equal("host", instruction.Network);
        Assert.Equal("host", instruction.NetworkToken.Value);
        Assert.Equal("RUN --network=host echo hello", instruction.ToString());

        instruction.NetworkToken = null;
        Assert.Null(instruction.Network);
        Assert.Null(instruction.NetworkToken);
        Assert.Equal("RUN echo hello", instruction.ToString());
    }

    [Fact]
    public void NetworkWithVariables()
    {
        RunInstruction instruction = new("echo hello", Enumerable.Empty<Mount>(), network: "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => instruction.NetworkToken!, token => instruction.NetworkToken = token, val => instruction.Network = val, "var", canContainVariables: true);
    }

    [Fact]
    public void Security()
    {
        RunInstruction instruction = new("echo hello", Enumerable.Empty<Mount>(), security: "insecure");
        Assert.Equal("insecure", instruction.Security);
        Assert.Equal("insecure", instruction.SecurityToken!.Value);
        Assert.Equal("RUN --security=insecure echo hello", instruction.ToString());

        instruction.Security = "sandbox";
        Assert.Equal("sandbox", instruction.Security);
        Assert.Equal("sandbox", instruction.SecurityToken!.Value);
        Assert.Equal("RUN --security=sandbox echo hello", instruction.ToString());

        instruction.Security = null;
        Assert.Null(instruction.Security);
        Assert.Null(instruction.SecurityToken);
        Assert.Equal("RUN echo hello", instruction.ToString());

        instruction.SecurityToken = new LiteralToken("insecure");
        Assert.Equal("insecure", instruction.Security);
        Assert.Equal("insecure", instruction.SecurityToken.Value);
        Assert.Equal("RUN --security=insecure echo hello", instruction.ToString());

        instruction.SecurityToken.Value = "sandbox";
        Assert.Equal("sandbox", instruction.Security);
        Assert.Equal("sandbox", instruction.SecurityToken.Value);
        Assert.Equal("RUN --security=sandbox echo hello", instruction.ToString());

        instruction.SecurityToken = null;
        Assert.Null(instruction.Security);
        Assert.Null(instruction.SecurityToken);
        Assert.Equal("RUN echo hello", instruction.ToString());
    }

    [Fact]
    public void SecurityWithVariables()
    {
        RunInstruction instruction = new("echo hello", Enumerable.Empty<Mount>(), security: "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => instruction.SecurityToken!, token => instruction.SecurityToken = token, val => instruction.Security = val, "var", canContainVariables: true);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<RunInstruction>[] testInputs = new ParseTestScenario<RunInstruction>[]
        {
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Equal(CommandType.ShellForm, result.Command.CommandType);
                    Assert.Equal("echo hello", result.Command.ToString());
                    Assert.IsType<ShellFormCommand>(result.Command);
                    Assert.Empty(result.Mounts);
                    ShellFormCommand cmd = (ShellFormCommand)result.Command;
                    Assert.Equal("echo hello", cmd.Value);
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN $TEST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "$TEST",
                        token => ValidateLiteral(token, "$TEST"))
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN echo $TEST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo $TEST",
                        token => ValidateLiteral(token, "echo $TEST"))
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN [PowerShellType]::Type.Method",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "[PowerShellType]::Type.Method",
                        token => ValidateLiteral(token, "[PowerShellType]::Type.Method"))
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN T\\$EST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "T\\$EST",
                        token => ValidateLiteral(token, "T\\$EST"))
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN `\n`\necho hello",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN echo `\n#test comment\nhello",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo `\n#test comment\nhello",
                        token => ValidateQuotableAggregate<LiteralToken>(token, "echo `\n#test comment\nhello", null,
                            token => ValidateString(token, "echo "),
                            token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                token => ValidateSymbol(token, '`'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateAggregate<CommentToken>(token, "#test comment\n",
                                token => ValidateSymbol(token, '#'),
                                token => ValidateString(token, "test comment"),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "hello")))
                },
                Validate = result =>
                {
                    Assert.Single(result.Comments);
                    Assert.Equal("test comment", result.Comments.First());
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Equal(CommandType.ShellForm, result.Command.CommandType);
                    Assert.Equal("echo `\n#test comment\nhello", result.Command.ToString());
                    Assert.IsType<ShellFormCommand>(result.Command);
                    ShellFormCommand cmd = (ShellFormCommand)result.Command;
                    Assert.Equal("echo hello", cmd.Value);
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN [\"/bin/bash\", \"-c\", \"echo hello\"]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"/bin/bash\", \"-c\", \"echo hello\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Equal(CommandType.ExecForm, result.Command.CommandType);
                    Assert.Equal("[\"/bin/bash\", \"-c\", \"echo hello\"]", result.Command.ToString());
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Equal(
                        new string[]
                        {
                            "/bin/bash",
                            "-c",
                            "echo hello"
                        },
                        cmd.Values.ToArray());
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN `\n[ \"/bi`\nn/bash\", `\n \"-c\" , \"echo he`\"llo\"]",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                        token => ValidateSymbol(token, '`'),
                        token => ValidateNewLine(token, "\n")),
                    token => ValidateAggregate<ExecFormCommand>(token, "[ \"/bi`\nn/bash\", `\n \"-c\" , \"echo he`\"llo\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateQuotableAggregate<LiteralToken>(token, "/bi`\nn/bash", ParseHelper.DoubleQuote,
                            token => ValidateString(token, "/bi"),
                            token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                token => ValidateSymbol(token, '`'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "n/bash")),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo he`\"llo", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Equal(CommandType.ExecForm, result.Command.CommandType);
                    Assert.Equal("[ \"/bi`\nn/bash\", `\n \"-c\" , \"echo he`\"llo\"]", result.Command.ToString());
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Equal(
                        new string[]
                        {
                            "/bin/bash",
                            "-c",
                            "echo he`\"llo"
                        },
                        cmd.Values.ToArray());
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN ec`\nho `test",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "ec`\nho `test",
                        token => ValidateAggregate<LiteralToken>(token, "ec`\nho `test",
                            token => ValidateString(token, "ec"),
                            token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                token => ValidateSymbol(token, '`'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "ho `test")))
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN \"ec`\nh`\"o `test\"",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "\"ec`\nh`\"o `test\"",
                        token => ValidateQuotableAggregate<LiteralToken>(token, "\"ec`\nh`\"o `test\"", null,
                            token => ValidateString(token, "\"ec"),
                            token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                token => ValidateSymbol(token, '`'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "h`\"o `test\"")))
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --mount=type=secret,id=id echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=secret,id=id",
                            token => ValidateKeyValue(token, "type", "secret"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "id", "id"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Equal(CommandType.ShellForm, result.Command.CommandType);
                    Assert.Equal("echo hello", result.Command.ToString());
                    Assert.IsType<ShellFormCommand>(result.Command);
                    ShellFormCommand cmd = (ShellFormCommand)result.Command;
                    Assert.Equal("echo hello", cmd.Value);

                    Assert.Single(result.Mounts);
                    Assert.IsType<Mount>(result.Mounts.First());
                    Assert.Equal("type=secret,id=id", result.Mounts.First().ToString());
                }
            },
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN `\n --mount=type=secret,id=id `\n echo hello",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=secret,id=id",
                            token => ValidateKeyValue(token, "type", "secret"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "id", "id"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Equal(CommandType.ShellForm, result.Command.CommandType);
                    Assert.Equal("echo hello", result.Command.ToString());
                    Assert.IsType<ShellFormCommand>(result.Command);
                    ShellFormCommand cmd = (ShellFormCommand)result.Command;
                    Assert.Equal("echo hello", cmd.Value);

                    Assert.Single(result.Mounts);
                    Assert.IsType<Mount>(result.Mounts.First());
                    Assert.Equal("type=secret,id=id", result.Mounts.First().ToString());
                }
            },
            // --mount with type=cache
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --mount=type=cache,target=/var/cache echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=cache,target=/var/cache",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=cache,target=/var/cache",
                            token => ValidateKeyValue(token, "type", "cache"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "target", "/var/cache"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Single(result.Mounts);
                    Assert.IsType<Mount>(result.Mounts.First());
                    Assert.Equal("cache", result.Mounts.First().Type);
                    Assert.Equal("type=cache,target=/var/cache", result.Mounts.First().ToString());
                }
            },
            // --mount with type=tmpfs
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --mount=type=tmpfs,target=/tmp echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=tmpfs,target=/tmp",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=tmpfs,target=/tmp",
                            token => ValidateKeyValue(token, "type", "tmpfs"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "target", "/tmp"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Single(result.Mounts);
                    Assert.IsType<Mount>(result.Mounts.First());
                    Assert.Equal("tmpfs", result.Mounts.First().Type);
                }
            },
            // --mount with type=bind
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --mount=type=bind,source=/src,target=/tgt echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=bind,source=/src,target=/tgt",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=bind,source=/src,target=/tgt",
                            token => ValidateKeyValue(token, "type", "bind"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "source", "/src"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "target", "/tgt"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Single(result.Mounts);
                    Assert.IsType<Mount>(result.Mounts.First());
                    Assert.Equal("bind", result.Mounts.First().Type);
                }
            },
            // --mount with type=cache + --network flag
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --mount=type=cache,target=/path --network=host echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=cache,target=/path",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=cache,target=/path",
                            token => ValidateKeyValue(token, "type", "cache"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "target", "/path"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<NetworkFlag>(token, "network", "host"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Single(result.Mounts);
                    Assert.IsType<Mount>(result.Mounts.First());
                    Assert.Equal("host", result.Network);
                }
            },
            // --network flag with shell form command
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --network=host echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<NetworkFlag>(token, "network", "host"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Equal(CommandType.ShellForm, result.Command.CommandType);
                    Assert.Equal("echo hello", result.Command.ToString());
                    Assert.Equal("host", result.Network);
                    Assert.Empty(result.Mounts);
                    Assert.Null(result.Security);
                }
            },
            // --security flag with shell form command
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --security=insecure echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<SecurityFlag>(token, "security", "insecure"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Equal(CommandType.ShellForm, result.Command.CommandType);
                    Assert.Equal("echo hello", result.Command.ToString());
                    Assert.Equal("insecure", result.Security);
                    Assert.Empty(result.Mounts);
                    Assert.Null(result.Network);
                }
            },
            // Both --network and --security flags
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --network=host --security=insecure echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<NetworkFlag>(token, "network", "host"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<SecurityFlag>(token, "security", "insecure"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Equal("host", result.Network);
                    Assert.Equal("insecure", result.Security);
                    Assert.Empty(result.Mounts);
                }
            },
            // --mount + --network flags together
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --mount=type=secret,id=id --network=host echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=secret,id=id",
                            token => ValidateKeyValue(token, "type", "secret"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "id", "id"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<NetworkFlag>(token, "network", "host"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Single(result.Mounts);
                    Assert.IsType<Mount>(result.Mounts.First());
                    Assert.Equal("host", result.Network);
                    Assert.Null(result.Security);
                }
            },
            // All flags in mixed order: --network, --mount, --security
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --network=host --mount=type=secret,id=id --security=insecure echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<NetworkFlag>(token, "network", "host"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=secret,id=id",
                            token => ValidateKeyValue(token, "type", "secret"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "id", "id"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<SecurityFlag>(token, "security", "insecure"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Equal("host", result.Network);
                    Assert.Equal("insecure", result.Security);
                    Assert.Single(result.Mounts);
                    Assert.IsType<Mount>(result.Mounts.First());
                }
            },
            // --network flag with exec form command
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN --network=host [\"/bin/bash\", \"-c\", \"echo hello\"]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<NetworkFlag>(token, "network", "host"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"/bin/bash\", \"-c\", \"echo hello\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Equal("host", result.Network);
                    Assert.Equal(CommandType.ExecForm, result.Command.CommandType);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Equal(
                        new string[]
                        {
                            "/bin/bash",
                            "-c",
                            "echo hello"
                        },
                        cmd.Values.ToArray());
                }
            },
            // Empty exec form array with no whitespace
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN []",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Equal(CommandType.ExecForm, result.Command.CommandType);
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Empty(cmd.Values);
                    Assert.Empty(result.Mounts);
                }
            },
            // Empty exec form array with interior whitespace
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN [ ]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[ ]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Equal(CommandType.ExecForm, result.Command.CommandType);
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Empty(cmd.Values);
                    Assert.Empty(result.Mounts);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public static IEnumerable<object[]> CreateTestInput()
    {
        CreateTestScenario[] testInputs = new CreateTestScenario[]
        {
            new CreateTestScenario
            {
                Command = "echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                }
            },
            new CreateTestScenario
            {
                Command = "/bin/bash",
                Args = new string[]
                {
                    "-c",
                    "echo hello"
                },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"/bin/bash\", \"-c\", \"echo hello\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']'))
                }
            },
            new CreateTestScenario
            {
                Command = "echo hello",
                Mounts = new Mount[]
                {
                    Mount.Parse("type=secret,id=id")
                },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=secret,id=id",
                            token => ValidateKeyValue(token, "type", "secret"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "id", "id"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                }
            },
            new CreateTestScenario
            {
                Command = "echo hello",
                Mounts = new Mount[]
                {
                    Mount.Parse("type=secret,id=id"),
                    Mount.Parse("type=secret,id=id2")
                },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=secret,id=id",
                            token => ValidateKeyValue(token, "type", "secret"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "id", "id"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id2",
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyword(token, "mount"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<Mount>(token, "type=secret,id=id2",
                            token => ValidateKeyValue(token, "type", "secret"),
                            token => ValidateSymbol(token, ','),
                            token => ValidateKeyValue(token, "id", "id2"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                }
            },
            // Create with network flag
            new CreateTestScenario
            {
                Command = "echo hello",
                Network = "host",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<NetworkFlag>(token, "network", "host"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Equal("host", result.Network);
                    Assert.Null(result.Security);
                    Assert.Empty(result.Mounts);
                }
            },
            // Create with security flag
            new CreateTestScenario
            {
                Command = "echo hello",
                Security = "insecure",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<SecurityFlag>(token, "security", "insecure"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Null(result.Network);
                    Assert.Equal("insecure", result.Security);
                    Assert.Empty(result.Mounts);
                }
            },
            // Create with both network and security flags
            new CreateTestScenario
            {
                Command = "echo hello",
                Network = "host",
                Security = "insecure",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<NetworkFlag>(token, "network", "host"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<SecurityFlag>(token, "security", "insecure"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Equal("host", result.Network);
                    Assert.Equal("insecure", result.Security);
                    Assert.Empty(result.Mounts);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<RunInstruction>
    {
        public string Command { get; set; }
        public IEnumerable<string> Args { get; set; }
        public IEnumerable<Mount> Mounts { get; set; }
        public string Network { get; set; }
        public string Security { get; set; }
    }
}
