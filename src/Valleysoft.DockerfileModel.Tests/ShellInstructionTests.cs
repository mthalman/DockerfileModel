using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class ShellInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ShellInstructionParseTestScenario scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            ShellInstruction result = ShellInstruction.Parse(scenario.Text, scenario.EscapeChar);
            Assert.Equal(scenario.Text, result.ToString());
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }
        else
        {
            ParseException exception = Assert.Throws<ParseException>(
                () => ShellInstruction.Parse(scenario.Text, scenario.EscapeChar));
            Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
            Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
        }
    }

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        ShellInstruction result = new(scenario.Command, scenario.Args);

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ShellInstructionParseTestScenario[] testInputs = new ShellInstructionParseTestScenario[]
        {
            new ShellInstructionParseTestScenario
            {
                Text = "SHELL [\"echo\", \"hello\"]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "SHELL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"echo\", \"hello\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "echo", '\"'),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "hello", '\"'),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("SHELL", result.InstructionName);
                    Assert.Equal(CommandType.ExecForm, result.Command.CommandType);
                    Assert.Equal("[\"echo\", \"hello\"]", result.Command.ToString());
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Collection(cmd.Values, new Action<string>[]
                        {
                            val => Assert.Equal("echo", val),
                            val => Assert.Equal("hello", val)
                        });
                }
            },
            new ShellInstructionParseTestScenario
            {
                Text = "SHELL [\"echo\"]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "SHELL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"echo\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "echo", '\"'),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("SHELL", result.InstructionName);
                    Assert.Equal(CommandType.ExecForm, result.Command.CommandType);
                    Assert.Equal("[\"echo\"]", result.Command.ToString());
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Collection(cmd.Values, new Action<string>[]
                        {
                            val => Assert.Equal("echo", val),
                        });
                }
            },
            new ShellInstructionParseTestScenario
            {
                Text = "SHELL `\n[\"echo\"]",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "SHELL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"echo\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "echo", '\"'),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("SHELL", result.InstructionName);
                    Assert.Equal(CommandType.ExecForm, result.Command.CommandType);
                    Assert.Equal("[\"echo\"]", result.Command.ToString());
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Collection(cmd.Values, new Action<string>[]
                        {
                            val => Assert.Equal("echo", val),
                        });
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
                Command = "echo",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "SHELL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"echo\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "echo", '\"'),
                        token => ValidateSymbol(token, ']'))
                },
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
                    token => ValidateKeyword(token, "SHELL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"/bin/bash\", \"-c\", \"echo hello\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/bin/bash", '\"'),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", '\"'),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo hello", '\"'),
                        token => ValidateSymbol(token, ']'))
                },
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class ShellInstructionParseTestScenario : ParseTestScenario<ShellInstruction>
    {
        public char EscapeChar { get; set; }
    }

    public class CreateTestScenario : TestScenario<ShellInstruction>
    {
        public string Command { get; set; }
        public IEnumerable<string> Args { get; set; } = Enumerable.Empty<string>();
    }
}
