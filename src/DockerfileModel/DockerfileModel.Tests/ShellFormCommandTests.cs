using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class ShellFormCommandTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ShellFormCommandParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                ShellFormCommand result = ShellFormCommand.Parse(scenario.Text, scenario.EscapeChar);
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
            ShellFormCommand result = new ShellFormCommand(scenario.Command);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Value()
        {
            ShellFormCommand result = new ShellFormCommand("echo hello");
            Assert.Equal("echo hello", result.Value);
            Assert.Equal("echo hello", result.ValueToken.Value);

            result.Value = "echo bye";
            Assert.Equal("echo bye", result.Value);
            Assert.Equal("echo bye", result.ValueToken.Value);

            result.ValueToken.Value = "echo hola";
            Assert.Equal("echo hola", result.Value);
            Assert.Equal("echo hola", result.ValueToken.Value);

            Assert.Throws<ArgumentNullException>(() => result.Value = null);
            Assert.Throws<ArgumentException>(() => result.Value = "");
        }

        [Fact]
        public void ValueWithVariables()
        {
            ShellFormCommand result = new ShellFormCommand("$var");
            TestHelper.TestVariablesWithLiteral(() => result.ValueToken, "$var", canContainVariables: false);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            ShellFormCommandParseTestScenario[] testInputs = new ShellFormCommandParseTestScenario[]
            {
                new ShellFormCommandParseTestScenario
                {
                    Text = "echo hello",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateLiteral(token, "echo hello")
                    },
                    Validate = result =>
                    {
                        Assert.Equal(CommandType.ShellForm, result.CommandType);
                        Assert.Equal("echo hello", result.ToString());
                        Assert.Equal("echo hello", result.Value);
                    }
                },
                new ShellFormCommandParseTestScenario
                {
                    Text = "echo `\n#test comment\nhello",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateQuotableAggregate<LiteralToken>(token, "echo `\n#test comment\nhello", null,
                            token => ValidateString(token, "echo "),
                            token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                token => ValidateSymbol(token, '`'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateAggregate<CommentToken>(token, "#test comment\n",
                                token => ValidateSymbol(token, '#'),
                                token => ValidateString(token, "test comment"),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "hello"))
                    },
                    Validate = result =>
                    {
                        Assert.Equal(CommandType.ShellForm, result.CommandType);
                        Assert.Equal("echo `\n#test comment\nhello", result.ToString());
                        Assert.Equal("echo hello", result.Value);
                    }
                },
                new ShellFormCommandParseTestScenario
                {
                    Text = "echo`\n  `\n  hello",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<LiteralToken>(token, "echo`\n  `\n  hello",
                            token => ValidateString(token, "echo"),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateString(token, "  "),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateString(token, "  hello")),
                    }
                },
                new ShellFormCommandParseTestScenario
                {
                    Text = "ec`\nho `test",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<LiteralToken>(token, "ec`\nho `test",
                            token => ValidateString(token, "ec"),
                            token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                token => ValidateSymbol(token, '`'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "ho `test"))
                    }
                },
                new ShellFormCommandParseTestScenario
                {
                    Text = "\"ec`\nh`\"o `test\"",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateQuotableAggregate<LiteralToken>(token, "\"ec`\nh`\"o `test\"", null,
                            token => ValidateString(token, "\"ec"),
                            token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                token => ValidateSymbol(token, '`'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "h`\"o `test\""))
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
                        token => ValidateLiteral(token, "echo hello")
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class ShellFormCommandParseTestScenario : ParseTestScenario<ShellFormCommand>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<ShellFormCommand>
        {
            public string Command { get; set; }
        }
    }
}
