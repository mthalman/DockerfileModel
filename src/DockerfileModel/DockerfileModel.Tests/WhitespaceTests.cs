using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class WhitespaceTests
    {
        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(ParseTestScenario<Whitespace> scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                Whitespace result = Whitespace.Create(scenario.Text);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => Whitespace.Create(scenario.Text));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        public static IEnumerable<object[]> CreateTestInput()
        {
            var testInputs = new ParseTestScenario<Whitespace>[]
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
                        Assert.Equal("  \t  ", result.Text.Value);

                        result.Text.Value = " ";
                        Assert.Equal(" ", result.ToString());
                    }
                },
                new ParseTestScenario<Whitespace>
                {
                    Text = "",
                    TokenValidators = new Action<Token>[]
                    {
                    }
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
                        Assert.Equal("\t ", result.Text.Value);

                        result.Text.Value = "";
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
                        Assert.Equal("\t ", result.Text.Value);
                        Assert.Equal("\n", result.NewLine.Value);

                        result.Text.Value = "";
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
                        Assert.Equal("\r\n", result.Text.Value);
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
