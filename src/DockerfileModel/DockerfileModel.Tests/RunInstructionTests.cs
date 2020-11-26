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
                if (scenario.Mounts is null)
                {
                    result = new RunInstruction(scenario.Command);
                }
                else
                {
                    result = new RunInstruction(scenario.Command, scenario.Mounts);
                }
            }
            else
            {
                if (scenario.Mounts is null)
                {
                    result = new RunInstruction(scenario.Commands);
                }
                else
                {
                    result = new RunInstruction(scenario.Commands, scenario.Mounts);
                }
            }

            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Mounts()
        {
            RunInstruction instruction = new RunInstruction("echo hello", new Mount[] { new SecretMount("id") });
            Assert.Single(instruction.Mounts);
            Assert.Equal("RUN --mount=type=secret,id=id echo hello", instruction.ToString());

            ((SecretMount)instruction.Mounts[0]).Id = "id2";
            Assert.Equal("RUN --mount=type=secret,id=id2 echo hello", instruction.ToString());

            instruction.Mounts[0] = new SecretMount("id3");
            Assert.Equal("RUN --mount=type=secret,id=id3 echo hello", instruction.ToString());
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
                new RunInstructionParseTestScenario
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
                new RunInstructionParseTestScenario
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
                new RunInstructionParseTestScenario
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
                new RunInstructionParseTestScenario
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
                new RunInstructionParseTestScenario
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
                new RunInstructionParseTestScenario
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
                        token => ValidateAggregate<ExecFormCommand>(token, "[ \"/bi`\nn/bash\", `\n \"-c\" , \"echo he`\"llo\"]",
                            token => ValidateSymbol(token, '['),
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
                        token => ValidateAggregate<ShellFormCommand>(token, "ec`\nho `test",
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
                        token => ValidateAggregate<ShellFormCommand>(token, "\"ec`\nh`\"o `test\"",
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
                            token => ValidateKeyword(token, "mount"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<SecretMount>(token, "type=secret,id=id",
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
                        Assert.IsType<SecretMount>(result.Mounts.First());
                        Assert.Equal("type=secret,id=id", result.Mounts.First().ToString());
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
                            token => ValidateKeyword(token, "mount"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<SecretMount>(token, "type=secret,id=id",
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
                        Assert.IsType<SecretMount>(result.Mounts.First());
                        Assert.Equal("type=secret,id=id", result.Mounts.First().ToString());
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
                        new SecretMount("id")
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
                            token => ValidateAggregate<SecretMount>(token, "type=secret,id=id",
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
                        new SecretMount("id"),
                        new SecretMount("id2")
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
                            token => ValidateAggregate<SecretMount>(token, "type=secret,id=id",
                                token => ValidateKeyValue(token, "type", "secret"),
                                token => ValidateSymbol(token, ','),
                                token => ValidateKeyValue(token, "id", "id"))),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<MountFlag>(token, "--mount=type=secret,id=id2",
                            token => ValidateSymbol(token, '-'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateKeyword(token, "mount"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<SecretMount>(token, "type=secret,id=id2",
                                token => ValidateKeyValue(token, "type", "secret"),
                                token => ValidateSymbol(token, ','),
                                token => ValidateKeyValue(token, "id", "id2"))),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                            token => ValidateLiteral(token, "echo hello"))
                    }
                },
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
            public IEnumerable<Mount> Mounts { get; set; }
        }
    }
}
