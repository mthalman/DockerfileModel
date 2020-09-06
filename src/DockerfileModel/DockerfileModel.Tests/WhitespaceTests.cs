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
        public void Create(TestScenario scenario)
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
            var testInputs = new TestScenario[]
            {
                new TestScenario
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
                new TestScenario
                {
                    Text = "",
                    TokenValidators = new Action<Token>[]
                    {
                    }
                },
                new TestScenario
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
                new TestScenario
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
                new TestScenario
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
                new TestScenario
                {
                    Text = "  x  ",
                    ParseExceptionPosition = new Position(0, 1, 3)
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class TestScenario
        {
            public Action<Whitespace> Validate { get; set; }
            public Action<Token>[] TokenValidators { get; set; }
            public string Text { get; set; }
            public Position ParseExceptionPosition { get; set; }
        }
    }
}
