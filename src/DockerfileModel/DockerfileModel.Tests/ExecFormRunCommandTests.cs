using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class ExecFormRunCommandTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ExecFormRunCommandParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                ExecFormRunCommand result = ExecFormRunCommand.Parse(scenario.Text, scenario.EscapeChar);
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
            ExecFormRunCommand result = ExecFormRunCommand.Create(scenario.Commands);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Commands()
        {
            ExecFormRunCommand result = ExecFormRunCommand.Create(new string[]
            {
                "/bin/bash",
                "-c",
                "echo hello"
            });

            Assert.Equal(
                new string[]
                {
                    "/bin/bash",
                    "-c",
                    "echo hello"
                },
                result.CommandArgs);

            Assert.Collection(result.CommandArgTokens, new Action<LiteralToken>[]
            {
                token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote),
            });

            result.CommandArgs[2] = "echo bye";
            Assert.Equal(
                new string[]
                {
                    "/bin/bash",
                    "-c",
                    "echo bye"
                },
                result.CommandArgs);

            Assert.Collection(result.CommandArgTokens, new Action<LiteralToken>[]
            {
                token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "echo bye", ParseHelper.DoubleQuote),
            });

            result.CommandArgTokens.Last().Value = "echo hola";
            Assert.Equal(
                new string[]
                {
                    "/bin/bash",
                    "-c",
                    "echo hola"
                },
                result.CommandArgs.ToArray());

            Assert.Collection(result.CommandArgTokens, new Action<LiteralToken>[]
            {
                token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "echo hola", ParseHelper.DoubleQuote),
            });

        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            ExecFormRunCommandParseTestScenario[] testInputs = new ExecFormRunCommandParseTestScenario[]
            {
                new ExecFormRunCommandParseTestScenario
                {
                    Text = "[\"/bin/bash\", \"-c\", \"echo hello\"]",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ","),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ","),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote)
                    },
                    Validate = result =>
                    {
                        Assert.Equal(RunCommandType.ExecForm, result.CommandType);
                        Assert.Equal("[\"/bin/bash\", \"-c\", \"echo hello\"]", result.ToString());
                        Assert.Equal(
                            new string[]
                            {
                                "/bin/bash",
                                "-c",
                                "echo hello"
                            },
                            result.CommandArgs.ToArray());
                    }
                },
                new ExecFormRunCommandParseTestScenario
                {
                    Text = "[ \"/bi`\nn/bash\", `\n \"-c\" , \"echo he`\"llo\"]",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateWhitespace(token, " "),
                        token => ValidateQuotableAggregate<LiteralToken>(token, "\"/bi`\nn/bash\"", ParseHelper.DoubleQuote,
                            token => ValidateString(token, "/bi"),
                            token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                token => ValidateSymbol(token, "`"),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "n/bash")),
                        token => ValidateSymbol(token, ","),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, "`"),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, ","),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo he`\"llo", ParseHelper.DoubleQuote)
                    },
                    Validate = result =>
                    {
                        Assert.Equal(RunCommandType.ExecForm, result.CommandType);
                        Assert.Equal("[ \"/bi`\nn/bash\", `\n \"-c\" , \"echo he`\"llo\"]", result.ToString());
                        Assert.Equal(
                            new string[]
                            {
                                "/bin/bash",
                                "-c",
                                "echo he`\"llo"
                            },
                            result.CommandArgs.ToArray());
                    }
                },
                new ExecFormRunCommandParseTestScenario
                {
                    Text = "echo hello",
                    ParseExceptionPosition = new Position(0, 1, 1)
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
                    Commands = new string[]
                    {
                        "/bin/bash",
                        "-c",
                        "echo hello"
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ","),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ","),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote)
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class ExecFormRunCommandParseTestScenario : ParseTestScenario<ExecFormRunCommand>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<ExecFormRunCommand>
        {
            public IEnumerable<string> Commands { get; set; }
        }
    }
}
