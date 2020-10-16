using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public static class Test
    {
        static readonly Parser<Content> Content =
            from chars in Parse.CharExcept(new char[] { '$', '}' }).Many()
            select new Content { Text = new string(chars.ToArray()) };

        static readonly Parser<Node> Node =
            from tag in Parse.String("${")
            from nodes in Parse.Ref(() => Item).Many()
            from end in Parse.Char('}').Token()
            select new Node { Name = "", Children = nodes };

        public static readonly Parser<Item> Item =
            from item in Node.Select(n => (Item)n).XOr(Content)
            select item;

    }

    public class Item { }

    public class Content : Item
    {
        public string Text;

        public override string ToString()
        {
            return Text;
        }
    }

    public class Node : Item
    {
        public string Name;
        public IEnumerable<Item> Children;

        public override string ToString()
        {
            if (Children != null)
                return Children.Aggregate("", (s, c) => s + c);
            return string.Format("<{0}/>", Name);
        }
    }

    public class ArgInstructionTests
    {
        private Parser<string> ArgRef() =>
            from opening in Sprache.Parse.String("${").Named("opening")
            from c in Content().Many()
            //from closing in Sprache.Parse.Char('}').Named("closing")
            select String.Concat(c);

        private Parser<string> Content() =>
            from chars in Sprache.Parse.AnyChar.Except(Sprache.Parse.Char('$')).Many().Text().Named("arg content")
            from argRef in Sprache.Parse.Ref(() => ArgRef()).Many().Named("argref")
            select chars + String.Concat(argRef);

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
            ArgInstruction result = ArgInstruction.Create(scenario.ArgName, scenario.ArgValue);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
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
                        token => ValidateLineContinuation(token, "`"),
                        token => ValidateNewLine(token, "\n"),
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
                        token => ValidateSymbol(token, "=")
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
                        token => ValidateLineContinuation(token, "`"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateAggregate<CommentToken>(token, "# my comment",
                            token => ValidateSymbol(token, "#"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLiteral(token, "my comment")),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateIdentifier(token, "MYARG"),
                        token => ValidateSymbol(token, "=")
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
                        token => ValidateSymbol(token, "="),
                        token => ValidateQuotableAggregate<ArgValue>(token, "1", null,
                            token => ValidateLiteral(token, "1"))
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
                        token => ValidateLineContinuation(token, "`"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateIdentifier(token, "MYARG"),
                        token => ValidateSymbol(token, "="),
                        token => ValidateQuotableAggregate<ArgValue>(token, "\"test\"", '\"',
                            token => ValidateLiteral(token, "test"))
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
                        token => ValidateSymbol(token, "="),
                        token => ValidateQuotableAggregate<ArgValue>(token, "'value'", '\'',
                            token => ValidateLiteral(token, "value")),
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
                        token => ValidateSymbol(token, "="),
                        token => ValidateQuotableAggregate<ArgValue>(token, "'va`'lue'", '\'',
                            token => ValidateLiteral(token, "va`'lue")),
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
                        token => ValidateSymbol(token, "="),
                        token => ValidateQuotableAggregate<ArgValue>(token, "va`'lue", null,
                            token => ValidateLiteral(token, "va`'lue")),
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
                        token => ValidateSymbol(token, "="),
                        token => ValidateQuotableAggregate<ArgValue>(token, "''", '\'')
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
                        token => ValidateSymbol(token, "="),
                        token => ValidateQuotableAggregate<ArgValue>(token, "b", null,
                            token => ValidateLiteral(token, "b"))
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
                        token => ValidateSymbol(token, "=")
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
