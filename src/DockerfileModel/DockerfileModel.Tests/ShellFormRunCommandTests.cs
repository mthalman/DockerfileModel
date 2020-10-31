using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class ShellFormRunCommandTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ShellFormRunCommandParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                ShellFormRunCommand result = ShellFormRunCommand.Parse(scenario.Text, scenario.EscapeChar);
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
            ShellFormRunCommand result = ShellFormRunCommand.Create(scenario.Command);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Value()
        {
            ShellFormRunCommand result = ShellFormRunCommand.Create("echo hello");
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

        public static IEnumerable<object[]> ParseTestInput()
        {
            ShellFormRunCommandParseTestScenario[] testInputs = new ShellFormRunCommandParseTestScenario[]
            {
                new ShellFormRunCommandParseTestScenario
                {
                    Text = "echo hello",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateLiteral(token, "echo hello")
                    },
                    Validate = result =>
                    {
                        Assert.Equal(RunCommandType.ShellForm, result.CommandType);
                        Assert.Equal("echo hello", result.ToString());
                        Assert.Equal("echo hello", result.Value);
                    }
                },
                new ShellFormRunCommandParseTestScenario
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
                        Assert.Equal(RunCommandType.ShellForm, result.CommandType);
                        Assert.Equal("echo `\n#test comment\nhello", result.ToString());
                        Assert.Equal("echo hello", result.Value);
                    }
                },
                new ShellFormRunCommandParseTestScenario
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
                new ShellFormRunCommandParseTestScenario
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

        public class ShellFormRunCommandParseTestScenario : ParseTestScenario<ShellFormRunCommand>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<ShellFormRunCommand>
        {
            public string Command { get; set; }
        }
    }
}
