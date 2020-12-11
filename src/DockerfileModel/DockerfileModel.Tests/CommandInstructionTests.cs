using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class CommandInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(CommandInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                CommandInstruction result = CommandInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => CommandInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            CommandInstruction result;
            if (scenario.Command != null)
            {
                result = new CommandInstruction(scenario.Command);
            }
            else
            {
                result = new CommandInstruction(scenario.Commands);
            }

            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            CommandInstructionParseTestScenario[] testInputs = new CommandInstructionParseTestScenario[]
            {
                new CommandInstructionParseTestScenario
                {
                    Text = "CMD echo hello",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "CMD"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                            token => ValidateLiteral(token, "echo hello"))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("CMD", result.InstructionName);
                        Assert.Equal(CommandType.ShellForm, result.Command.CommandType);
                        Assert.Equal("echo hello", result.Command.ToString());
                        Assert.IsType<ShellFormCommand>(result.Command);
                        ShellFormCommand cmd = (ShellFormCommand)result.Command;
                        Assert.Equal("echo hello", cmd.Value);
                    }
                },
                new CommandInstructionParseTestScenario
                {
                    Text = "CMD $TEST",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "CMD"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormCommand>(token, "$TEST",
                            token => ValidateLiteral(token, "$TEST"))
                    }
                },
                new CommandInstructionParseTestScenario
                {
                    Text = "CMD echo $TEST",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "CMD"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormCommand>(token, "echo $TEST",
                            token => ValidateLiteral(token, "echo $TEST"))
                    }
                },
                new CommandInstructionParseTestScenario
                {
                    Text = "CMD T\\$EST",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "CMD"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormCommand>(token, "T\\$EST",
                            token => ValidateLiteral(token, "T\\$EST"))
                    }
                },
                new CommandInstructionParseTestScenario
                {
                    Text = "CMD echo `\n#test comment\nhello",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "CMD"),
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
                        Assert.Equal("CMD", result.InstructionName);
                        Assert.Equal(CommandType.ShellForm, result.Command.CommandType);
                        Assert.Equal("echo `\n#test comment\nhello", result.Command.ToString());
                        Assert.IsType<ShellFormCommand>(result.Command);
                        ShellFormCommand cmd = (ShellFormCommand)result.Command;
                        Assert.Equal("echo hello", cmd.Value);
                    }
                },
                new CommandInstructionParseTestScenario
                {
                    Text = "CMD [\"/bin/bash\", \"-c\", \"echo hello\"]",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "CMD"),
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
                        Assert.Equal("CMD", result.InstructionName);
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
                        token => ValidateKeyword(token, "CMD"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                            token => ValidateLiteral(token, "echo hello"))
                    }
                },
                new CreateTestScenario
                {
                    Commands = new string[]
                    {
                        "/bin/bash",
                        "-c",
                        "echo hello"
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "CMD"),
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

        public class CommandInstructionParseTestScenario : ParseTestScenario<CommandInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<CommandInstruction>
        {
            public string Command { get; set; }
            public IEnumerable<string> Commands { get; set; }
        }
    }
}
