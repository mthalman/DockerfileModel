using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class RunInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(RunInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                RunInstruction result = RunInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => RunInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            RunInstruction result;
            if (scenario.Command != null)
            {
                result = RunInstruction.Create(scenario.Command);
            }
            else
            {
                result = RunInstruction.Create(scenario.Commands);
            }

            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            RunInstructionParseTestScenario[] testInputs = new RunInstructionParseTestScenario[]
            {
                new RunInstructionParseTestScenario
                {
                    Text = "RUN echo hello",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormRunCommand>(token, "echo hello",
                            token => ValidateLiteral(token, "echo hello"))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("RUN", result.InstructionName);
                        Assert.Equal(RunCommandType.ShellForm, result.Command.CommandType);
                        Assert.Equal("echo hello", result.Command.ToString());
                        Assert.IsType<ShellFormRunCommand>(result.Command);
                        Assert.Empty(result.MountFlags);
                        ShellFormRunCommand cmd = (ShellFormRunCommand)result.Command;
                        Assert.Equal("echo hello", cmd.Value);
                    }
                },
                new RunInstructionParseTestScenario
                {
                    Text = "RUN echo `\n#test comment\nhello",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormRunCommand>(token, "echo `\n#test comment\nhello",
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
                        Assert.Equal(RunCommandType.ShellForm, result.Command.CommandType);
                        Assert.Equal("echo `\n#test comment\nhello", result.Command.ToString());
                        Assert.IsType<ShellFormRunCommand>(result.Command);
                        ShellFormRunCommand cmd = (ShellFormRunCommand)result.Command;
                        Assert.Equal("echo hello", cmd.Value);
                    }
                },
                new RunInstructionParseTestScenario
                {
                    Text = "RUN [\"/bin/bash\", \"-c\", \"echo hello\"]",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ExecFormRunCommand>(token, "[\"/bin/bash\", \"-c\", \"echo hello\"]",
                            token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                            token => ValidateSymbol(token, ','),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                            token => ValidateSymbol(token, ','),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("RUN", result.InstructionName);
                        Assert.Equal(RunCommandType.ExecForm, result.Command.CommandType);
                        Assert.Equal("[\"/bin/bash\", \"-c\", \"echo hello\"]", result.Command.ToString());
                        Assert.IsType<ExecFormRunCommand>(result.Command);
                        ExecFormRunCommand cmd = (ExecFormRunCommand)result.Command;
                        Assert.Equal(
                            new string[]
                            {
                                "/bin/bash",
                                "-c",
                                "echo hello"
                            },
                            cmd.CommandArgs.ToArray());
                    }
                },
                new RunInstructionParseTestScenario
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
                        token => ValidateAggregate<ExecFormRunCommand>(token, "[ \"/bi`\nn/bash\", `\n \"-c\" , \"echo he`\"llo\"]",
                            token => ValidateWhitespace(token, " "),
                            token => ValidateQuotableAggregate<LiteralToken>(token, "\"/bi`\nn/bash\"", ParseHelper.DoubleQuote,
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
                            token => ValidateLiteral(token, "echo he`\"llo", ParseHelper.DoubleQuote))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("RUN", result.InstructionName);
                        Assert.Equal(RunCommandType.ExecForm, result.Command.CommandType);
                        Assert.Equal("[ \"/bi`\nn/bash\", `\n \"-c\" , \"echo he`\"llo\"]", result.Command.ToString());
                        Assert.IsType<ExecFormRunCommand>(result.Command);
                        ExecFormRunCommand cmd = (ExecFormRunCommand)result.Command;
                        Assert.Equal(
                            new string[]
                            {
                                "/bin/bash",
                                "-c",
                                "echo he`\"llo"
                            },
                            cmd.CommandArgs.ToArray());
                    }
                },
                new RunInstructionParseTestScenario
                {
                    Text = "RUN ec`\nho `test",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormRunCommand>(token, "ec`\nho `test",
                            token => ValidateAggregate<LiteralToken>(token, "ec`\nho `test",
                                token => ValidateString(token, "ec"),
                                token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                    token => ValidateSymbol(token, '`'),
                                    token => ValidateNewLine(token, "\n")),
                                token => ValidateString(token, "ho `test")))
                    }
                },
                new RunInstructionParseTestScenario
                {
                    Text = "RUN \"ec`\nh`\"o `test\"",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormRunCommand>(token, "\"ec`\nh`\"o `test\"",
                            token => ValidateQuotableAggregate<LiteralToken>(token, "\"ec`\nh`\"o `test\"", null,
                                token => ValidateString(token, "\"ec"),
                                token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                    token => ValidateSymbol(token, '`'),
                                    token => ValidateNewLine(token, "\n")),
                                token => ValidateString(token, "h`\"o `test\"")))
                    }
                },
                new RunInstructionParseTestScenario
                {
                    Text = "RUN --mount=type=secret,id=id echo hello",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id",
                            token => ValidateSymbol(token, '-'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateAggregate<KeyValueToken<Mount>>(token, "mount=type=secret,id=id",
                                token => ValidateKeyword(token, "mount"),
                                token => ValidateSymbol(token, '='),
                                token => ValidateAggregate<SecretMount>(token, "type=secret,id=id",
                                    token => ValidateKeyValue(token, "type", "secret"),
                                    token => ValidateSymbol(token, ','),
                                    token => ValidateKeyValue(token, "id", "id")))),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormRunCommand>(token, "echo hello",
                            token => ValidateLiteral(token, "echo hello"))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("RUN", result.InstructionName);
                        Assert.Equal(RunCommandType.ShellForm, result.Command.CommandType);
                        Assert.Equal("echo hello", result.Command.ToString());
                        Assert.IsType<ShellFormRunCommand>(result.Command);
                        ShellFormRunCommand cmd = (ShellFormRunCommand)result.Command;
                        Assert.Equal("echo hello", cmd.Value);

                        Assert.Single(result.MountFlags);
                        Assert.IsType<SecretMount>(result.MountFlags.First().Mount);
                        Assert.Equal("--mount=type=secret,id=id", result.MountFlags.First().ToString());
                    }
                },
                new RunInstructionParseTestScenario
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
                            token => ValidateAggregate<KeyValueToken<Mount>>(token, "mount=type=secret,id=id",
                                token => ValidateKeyword(token, "mount"),
                                token => ValidateSymbol(token, '='),
                                token => ValidateAggregate<SecretMount>(token, "type=secret,id=id",
                                    token => ValidateKeyValue(token, "type", "secret"),
                                    token => ValidateSymbol(token, ','),
                                    token => ValidateKeyValue(token, "id", "id")))),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormRunCommand>(token, "echo hello",
                            token => ValidateLiteral(token, "echo hello"))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("RUN", result.InstructionName);
                        Assert.Equal(RunCommandType.ShellForm, result.Command.CommandType);
                        Assert.Equal("echo hello", result.Command.ToString());
                        Assert.IsType<ShellFormRunCommand>(result.Command);
                        ShellFormRunCommand cmd = (ShellFormRunCommand)result.Command;
                        Assert.Equal("echo hello", cmd.Value);

                        Assert.Single(result.MountFlags);
                        Assert.IsType<SecretMount>(result.MountFlags.First().Mount);
                        Assert.Equal("--mount=type=secret,id=id", result.MountFlags.First().ToString());
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
                        token => ValidateAggregate<ShellFormRunCommand>(token, "echo hello",
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
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ExecFormRunCommand>(token, "[\"/bin/bash\", \"-c\", \"echo hello\"]",
                            token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                            token => ValidateSymbol(token, ','),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                            token => ValidateSymbol(token, ','),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote))
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class RunInstructionParseTestScenario : ParseTestScenario<RunInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<RunInstruction>
        {
            public string Command { get; set; }
            public IEnumerable<string> Commands { get; set; }
        }
    }
}
