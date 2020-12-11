using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class ExecFormCommandTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ExecFormCommandParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                ExecFormCommand result = ExecFormCommand.Parse(scenario.Text, scenario.EscapeChar);
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
            ExecFormCommand result = new ExecFormCommand(scenario.Commands);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Commands()
        {
            ExecFormCommand result = new ExecFormCommand(new string[]
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
                result.Values);

            Assert.Collection(result.ValueTokens, new Action<LiteralToken>[]
            {
                token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote),
            });

            result.Values[2] = "echo bye";
            Assert.Equal(
                new string[]
                {
                    "/bin/bash",
                    "-c",
                    "echo bye"
                },
                result.Values);

            Assert.Collection(result.ValueTokens, new Action<LiteralToken>[]
            {
                token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "echo bye", ParseHelper.DoubleQuote),
            });

            result.ValueTokens.Last().Value = "echo hola";
            Assert.Equal(
                new string[]
                {
                    "/bin/bash",
                    "-c",
                    "echo hola"
                },
                result.Values.ToArray());

            Assert.Collection(result.ValueTokens, new Action<LiteralToken>[]
            {
                token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                token => ValidateLiteral(token, "echo hola", ParseHelper.DoubleQuote),
            });

        }

        [Fact]
        public void CommandArgsWithVariablesNotParsed()
        {
            ExecFormCommand result = new ExecFormCommand(new string[]
            {
                "$var"
            });
            TestHelper.TestVariablesWithLiteral(() => result.ValueTokens.First(), "$var", canContainVariables: false);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            ExecFormCommandParseTestScenario[] testInputs = new ExecFormCommandParseTestScenario[]
            {
                new ExecFormCommandParseTestScenario
                {
                    Text = "[\"/bin/bash\", \"-c\", \"echo hello\"]\n",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']'),
                        token => ValidateNewLine(token, "\n")
                    },
                    Validate = result =>
                    {
                        Assert.Equal(CommandType.ExecForm, result.CommandType);
                        Assert.Equal("[\"/bin/bash\", \"-c\", \"echo hello\"]\n", result.ToString());
                        Assert.Equal(
                            new string[]
                            {
                                "/bin/bash",
                                "-c",
                                "echo hello"
                            },
                            result.Values.ToArray());
                    }
                },
                new ExecFormCommandParseTestScenario
                {
                    Text = "[ \"/bi`\nn/bash\", `\n \"-c\" , \"echo he`\"llo\"]",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
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
                        token => ValidateSymbol(token, ']')
                    },
                    Validate = result =>
                    {
                        Assert.Equal(CommandType.ExecForm, result.CommandType);
                        Assert.Equal("[ \"/bi`\nn/bash\", `\n \"-c\" , \"echo he`\"llo\"]", result.ToString());
                        Assert.Equal(
                            new string[]
                            {
                                "/bin/bash",
                                "-c",
                                "echo he`\"llo"
                            },
                            result.Values.ToArray());
                    }
                },
                new ExecFormCommandParseTestScenario
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
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/bin/bash", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo hello", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']')
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class ExecFormCommandParseTestScenario : ParseTestScenario<ExecFormCommand>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<ExecFormCommand>
        {
            public IEnumerable<string> Commands { get; set; }
        }
    }
}
