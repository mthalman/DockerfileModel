using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class EntrypointInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<EntrypointInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, EntrypointInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        EntrypointInstruction result;
        if (scenario.Args is null)
        {
            result = new EntrypointInstruction(scenario.Command);
        }
        else
        {
            result = new EntrypointInstruction(scenario.Command, scenario.Args);
        }

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<EntrypointInstruction>[] testInputs = new ParseTestScenario<EntrypointInstruction>[]
        {
            new ParseTestScenario<EntrypointInstruction>
            {
                Text = "ENTRYPOINT echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENTRYPOINT"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateQuotableAggregate<LiteralToken>(token, "echo hello", null,
                            token => ValidateString(token, "echo"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateString(token, "hello")))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENTRYPOINT", result.InstructionName);
                    Assert.Equal(CommandType.ShellForm, result.Command.CommandType);
                    Assert.Equal("echo hello", result.Command.ToString());
                    Assert.IsType<ShellFormCommand>(result.Command);
                    ShellFormCommand cmd = (ShellFormCommand)result.Command;
                    Assert.Equal("echo hello", cmd.Value);
                }
            },
            new ParseTestScenario<EntrypointInstruction>
            {
                Text = "ENTRYPOINT $TEST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENTRYPOINT"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "$TEST",
                        token => ValidateLiteral(token, "$TEST"))
                }
            },
            new ParseTestScenario<EntrypointInstruction>
            {
                Text = "ENTRYPOINT echo $TEST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENTRYPOINT"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo $TEST",
                        token => ValidateQuotableAggregate<LiteralToken>(token, "echo $TEST", null,
                            token => ValidateString(token, "echo"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateString(token, "$TEST")))
                }
            },
            new ParseTestScenario<EntrypointInstruction>
            {
                Text = "ENTRYPOINT T\\$EST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENTRYPOINT"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "T\\$EST",
                        token => ValidateLiteral(token, "T\\$EST"))
                }
            },
            new ParseTestScenario<EntrypointInstruction>
            {
                Text = "ENTRYPOINT echo `\n#test comment\nhello",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENTRYPOINT"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo `\n#test comment\nhello",
                        token => ValidateQuotableAggregate<LiteralToken>(token, "echo `\n#test comment\nhello", null,
                            token => ValidateString(token, "echo"),
                            token => ValidateWhitespace(token, " "),
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
                    Assert.Equal("ENTRYPOINT", result.InstructionName);
                    Assert.Equal(CommandType.ShellForm, result.Command.CommandType);
                    Assert.Equal("echo `\n#test comment\nhello", result.Command.ToString());
                    Assert.IsType<ShellFormCommand>(result.Command);
                    ShellFormCommand cmd = (ShellFormCommand)result.Command;
                    Assert.Equal("echo hello", cmd.Value);
                }
            },
            new ParseTestScenario<EntrypointInstruction>
            {
                Text = "ENTRYPOINT [\"/bin/bash\", \"-c\", \"echo hello\"]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENTRYPOINT"),
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
                    Assert.Equal("ENTRYPOINT", result.InstructionName);
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
            }
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
                    token => ValidateKeyword(token, "ENTRYPOINT"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateQuotableAggregate<LiteralToken>(token, "echo hello", null,
                            token => ValidateString(token, "echo"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateString(token, "hello")))
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
                    token => ValidateKeyword(token, "ENTRYPOINT"),
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
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<EntrypointInstruction>
    {
        public string Command { get; set; }
        public IEnumerable<string> Args { get; set; }
    }
}
