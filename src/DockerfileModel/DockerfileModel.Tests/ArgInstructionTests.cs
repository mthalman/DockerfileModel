using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class ArgInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ArgInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                ArgInstruction result = ArgInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => ArgInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            ArgInstruction result = new ArgInstruction(scenario.ArgName, scenario.ArgValue);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void ArgName()
        {
            ArgInstruction arg = new ArgInstruction("test");
            Assert.Equal("test", arg.ArgName);
            Assert.Equal("test", arg.ArgNameToken.Value);

            arg.ArgName = "test2";
            Assert.Equal("test2", arg.ArgName);
            Assert.Equal("test2", arg.ArgNameToken.Value);

            arg.ArgNameToken.Value = "test3";
            Assert.Equal("test3", arg.ArgName);
            Assert.Equal("test3", arg.ArgNameToken.Value);

            Assert.Throws<ArgumentNullException>(() => arg.ArgName = null);
            Assert.Throws<ArgumentException>(() => arg.ArgName = "");
            Assert.Throws<ArgumentNullException>(() => arg.ArgNameToken= null);
        }

        [Fact]
        public void ArgValue()
        {
            ArgInstruction arg = new ArgInstruction("test");
            Assert.Null(arg.ArgValue);
            Assert.Null(arg.ArgValueToken);
            Assert.False(arg.HasAssignmentOperator);

            arg.ArgValue = "foo";
            Assert.Equal("foo", arg.ArgValue);
            Assert.Equal("foo", arg.ArgValueToken.Value);
            Assert.True(arg.HasAssignmentOperator);

            arg.ArgValue = "";
            Assert.Equal("", arg.ArgValue);
            Assert.Equal("", arg.ArgValueToken.Value);
            Assert.True(arg.HasAssignmentOperator);

            arg.ArgValue = "foo";

            arg.ArgValue = null;
            Assert.Null(arg.ArgValue);
            Assert.Null(arg.ArgValueToken);
            Assert.False(arg.HasAssignmentOperator);

            arg.ArgValueToken = new LiteralToken("foo2");
            Assert.Equal("foo2", arg.ArgValue);
            Assert.Equal("foo2", arg.ArgValueToken.Value);
            Assert.True(arg.HasAssignmentOperator);

            arg.ArgValueToken = new LiteralToken("foo3");
            Assert.Equal("foo3", arg.ArgValue);
            Assert.Equal("foo3", arg.ArgValueToken.Value);
            Assert.True(arg.HasAssignmentOperator);

            arg.ArgValueToken = null;
            Assert.Null(arg.ArgValue);
            Assert.Null(arg.ArgValueToken);
            Assert.False(arg.HasAssignmentOperator);
        }

        [Fact]
        public void ArgValueWithVariables()
        {
            ArgInstruction arg = new ArgInstruction("test", "$var");
            TestHelper.TestVariablesWithNullableLiteral(
                () => arg.ArgValueToken, token => arg.ArgValueToken = token, val => arg.ArgValue = val, "var", canContainVariables: true);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            ArgInstructionParseTestScenario[] testInputs = new ArgInstructionParseTestScenario[]
            {
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MYARG",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "MYARG")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MYARG", result.ArgName);
                        Assert.Null(result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MYARG\r\n",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "MYARG"),
                        token => ValidateNewLine(token, "\r\n")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MYARG", result.ArgName);
                        Assert.Null(result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG `\nMYARG",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateIdentifier(token, "MYARG")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MYARG", result.ArgName);
                        Assert.Null(result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MYARG=",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "MYARG"),
                        token => ValidateSymbol(token, '=')
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MYARG", result.ArgName);
                        Assert.Equal("", result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MYARG=\"\"",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "MYARG"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "", '\"')
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MYARG", result.ArgName);
                        Assert.Equal("", result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG `\n# my comment\n  MYARG=",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateAggregate<CommentToken>(token, "# my comment\n",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateString(token, "my comment"),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateIdentifier(token, "MYARG"),
                        token => ValidateSymbol(token, '=')
                    },
                    Validate = result =>
                    {
                        Assert.Collection(result.Comments,
                            comment => Assert.Equal("my comment", comment));
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MYARG", result.ArgName);
                        Assert.Equal("", result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG myarg=1",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "myarg"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "1")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("myarg", result.ArgName);
                        Assert.Equal("1", result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG myarg`\n=`\n1",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "myarg"),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateLiteral(token, "1")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("myarg", result.ArgName);
                        Assert.Equal("1", result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG `\nMYARG=\"test\"",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateIdentifier(token, "MYARG"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "test", '\"')
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MYARG", result.ArgName);
                        Assert.Equal("test", result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MY_ARG",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "MY_ARG"),
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MY_ARG", result.ArgName);
                        Assert.Null(result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG \"MY_ARG\"='value'",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "MY_ARG", '\"'),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "value", '\''),
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MY_ARG", result.ArgName);
                        Assert.Equal("value", result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG \"MY`\"_ARG\"='va`'lue'",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "MY`\"_ARG", '\"'),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "va`'lue", '\''),
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MY`\"_ARG", result.ArgName);
                        Assert.Equal("va`'lue", result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MY_ARG=va`'lue",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "MY_ARG"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "va`'lue"),
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MY_ARG", result.ArgName);
                        Assert.Equal("va`'lue", result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MY_ARG=\'\'",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "MY_ARG"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "", '\'')
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal("MY_ARG", result.ArgName);
                        Assert.Empty(result.ArgValue);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "xARG ",
                    ParseExceptionPosition = new Position(1, 1, 1)
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG ",
                    ParseExceptionPosition = new Position(1, 1, 5)
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG =",
                    ParseExceptionPosition = new Position(1, 1, 5)
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
                    ArgName = "TEST1",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "TEST1")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("TEST1", result.ArgName);
                        Assert.Null(result.ArgValue);

                        result.ArgName = "TEST2";
                        Assert.Equal("TEST2", result.ArgName);
                        Assert.Equal("ARG TEST2", result.ToString());

                        result.ArgValue = "a";
                        Assert.Equal("a", result.ArgValue);
                        Assert.Equal("ARG TEST2=a", result.ToString());

                        result.ArgValue = null;
                        Assert.Null(result.ArgValue);
                        Assert.Equal("ARG TEST2", result.ToString());

                        result.ArgValue = "";
                        Assert.Equal("", result.ArgValue);
                        Assert.Equal("ARG TEST2=", result.ToString());
                    }
                },
                new CreateTestScenario
                {
                    ArgName = "TEST1",
                    ArgValue = "b",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "TEST1"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "b")
                    }
                },
                new CreateTestScenario
                {
                    ArgName = "TEST1",
                    ArgValue = "",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "TEST1"),
                        token => ValidateSymbol(token, '=')
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class ArgInstructionParseTestScenario : ParseTestScenario<ArgInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<ArgInstruction>
        {
            public string ArgName { get; set; }
            public string ArgValue { get; set; }
        }
    }
}
