using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class ArgInstructionTests
    {
        [Fact]
        public void Test()
        {
            var escapedQuote = Sprache.Parse.String("\\\'").Text();
            var escape = Sprache.Parse.String("\\").Text();
            var simpleLiteral = Sprache.Parse.AnyChar.Except(Sprache.Parse.Char('\'')).Except(Sprache.Parse.Char('\\')).Many().Text();
            var foo = (from open in Sprache.Parse.Char('\'')
                       from content in escapedQuote.Or(escape).Or(simpleLiteral).Many()
                       from close in Sprache.Parse.Char('\'')
                       select String.Concat(content));

            var result = foo.Parse("'fdsf\\\'sdfdsf'");

            var dockerfile = Dockerfile.Parse(System.IO.File.ReadAllText(@"C:\repos\docker-tools\src\Microsoft.DotNet.ImageBuilder\Dockerfile.linux"));
            dockerfile.ResolveArgValues(new Dictionary<string, string>(), '\\');
        }

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
            var testInputs = new ArgInstructionParseTestScenario[]
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
                        token => ValidatePunctuation(token, "="),
                        token => ValidateLiteral(token, "")
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
                        token => ValidateComment(token),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateCommentText(token, "my comment"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateIdentifier(token, "MYARG"),
                        token => ValidatePunctuation(token, "="),
                        token => ValidateLiteral(token, "")
                    },
                    Validate = result =>
                    {
                        Assert.Collection(result.Comments,
                            token => ValidateCommentText(token, "my comment"));
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
                        token => ValidatePunctuation(token, "="),
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
                        token => ValidateLineContinuation(token, "`"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateIdentifier(token, "MYARG"),
                        token => ValidatePunctuation(token, "="),
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
                        token => ValidatePunctuation(token, "="),
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
            var testInputs = new CreateTestScenario[]
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
                        token => ValidatePunctuation(token, "="),
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
                        token => ValidatePunctuation(token, "="),
                        token => ValidateLiteral(token, "")
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
