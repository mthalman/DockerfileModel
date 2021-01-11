using System;
using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests
{
    public class WhitespaceTests
    {
        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(ParseTestScenario<Whitespace> scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                Whitespace result = new Whitespace(scenario.Text);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => new Whitespace(scenario.Text));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Fact]
        public void Value()
        {
            Whitespace whitespace = new Whitespace(" ");
            Assert.Equal(" ", whitespace.Value);
            Assert.Equal(" ", whitespace.ValueToken.Value);

            whitespace.Value = "\t";
            Assert.Equal("\t", whitespace.Value);
            Assert.Equal("\t", whitespace.ValueToken.Value);

            whitespace.Value = null;
            Assert.Null(whitespace.Value);
            Assert.Null(whitespace.ValueToken);

            whitespace.ValueToken = new WhitespaceToken("\t\t");
            Assert.Equal("\t\t", whitespace.Value);
            Assert.Equal("\t\t", whitespace.ValueToken.Value);

            whitespace.ValueToken.Value = "  ";
            Assert.Equal("  ", whitespace.Value);
            Assert.Equal("  ", whitespace.ValueToken.Value);

            whitespace.ValueToken = null;
            Assert.Null(whitespace.Value);
            Assert.Null(whitespace.ValueToken);
        }

        [Fact]
        public void NewLine()
        {
            Whitespace whitespace = new Whitespace(" ");
            Assert.Null(whitespace.NewLine);
            Assert.Null(whitespace.NewLineToken);

            whitespace.NewLine = "\n";
            Assert.Equal("\n", whitespace.NewLine);
            Assert.Equal("\n", whitespace.NewLineToken.Value);

            whitespace.NewLine = null;
            Assert.Null(whitespace.NewLine);
            Assert.Null(whitespace.NewLineToken);

            whitespace.NewLineToken = new NewLineToken("\r\n");
            Assert.Equal("\r\n", whitespace.NewLine);
            Assert.Equal("\r\n", whitespace.NewLineToken.Value);

            whitespace.NewLineToken.Value = "\n";
            Assert.Equal("\n", whitespace.NewLine);
            Assert.Equal("\n", whitespace.NewLineToken.Value);

            whitespace.NewLineToken = null;
            Assert.Null(whitespace.NewLine);
            Assert.Null(whitespace.NewLineToken);
        }

        public static IEnumerable<object[]> CreateTestInput()
        {
            ParseTestScenario<Whitespace>[] testInputs = new ParseTestScenario<Whitespace>[]
            {
                new ParseTestScenario<Whitespace>
                {
                    Text = "  \t  ",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateWhitespace(token, "  \t  ")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("  \t  ", result.Value);

                        result.Value = " ";
                        Assert.Equal(" ", result.ToString());
                    }
                },
                new ParseTestScenario<Whitespace>
                {
                    Text = "",
                    TokenValidators = Array.Empty<Action<Token>>()
                },
                new ParseTestScenario<Whitespace>
                {
                    Text = "\t ",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateWhitespace(token, "\t ")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("\t ", result.Value);

                        result.Value = "";
                        Assert.Equal("", result.ToString());
                    }
                },
                new ParseTestScenario<Whitespace>
                {
                    Text = "\t \n",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateWhitespace(token, "\t "),
                        token => ValidateNewLine(token, "\n"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("\t ", result.Value);
                        Assert.Equal("\n", result.NewLine);

                        result.Value = "";
                        Assert.Equal("\n", result.ToString());
                    }
                },
                new ParseTestScenario<Whitespace>
                {
                    Text = "\r\n",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateNewLine(token, "\r\n"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("\r\n", result.Value);
                    }
                },
                new ParseTestScenario<Whitespace>
                {
                    Text = "  x  ",
                    ParseExceptionPosition = new Position(0, 1, 3)
                }
            };

            return testInputs.Select(input => new object[] { input });
        }
    }
}
