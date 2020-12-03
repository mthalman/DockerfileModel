using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class StageNameTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(StageNameParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                StageName result = new StageName(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => new StageName(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Fact]
        public void Value()
        {
            StageName stageName = new StageName("test");
            Assert.Equal("test", stageName.Value);

            stageName.Value = "test2";
            Assert.Equal("test2", stageName.Value);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            StageNameParseTestScenario[] testInputs = new StageNameParseTestScenario[]
            {
                new StageNameParseTestScenario
                {
                    Text = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateString(token, "test"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("test", result.Value);
                    }
                },
                new StageNameParseTestScenario
                {
                    Text = "test_-.x",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateString(token, "test_-.x"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("test_-.x", result.Value);
                    }
                },
                new StageNameParseTestScenario
                {
                    Text = "te`\nst",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateString(token, "te"),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateString(token, "st"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("test", result.Value);
                    }
                },
                new StageNameParseTestScenario
                {
                    Text = "-test",
                    ParseExceptionPosition = new Position(1, 1, 1)
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class StageNameParseTestScenario : ParseTestScenario<StageName>
        {
            public char EscapeChar { get; set; }
        }
    }
}
