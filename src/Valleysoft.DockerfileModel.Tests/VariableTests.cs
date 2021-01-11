using System;
using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests
{
    public class VariableTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(VariableParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                Variable result = new Variable(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => new Variable(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Fact]
        public void Value()
        {
            Variable variable = new Variable("test");
            Assert.Equal("test", variable.Value);

            variable.Value = "test2";
            Assert.Equal("test2", variable.Value);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            VariableParseTestScenario[] testInputs = new VariableParseTestScenario[]
            {
                new VariableParseTestScenario
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
                new VariableParseTestScenario
                {
                    Text = "test_x",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateString(token, "test_x"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("test_x", result.Value);
                    }
                },
                new VariableParseTestScenario
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
                new VariableParseTestScenario
                {
                    Text = "_test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateString(token, "_test"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("_test", result.Value);
                    }
                },
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class VariableParseTestScenario : ParseTestScenario<Variable>
        {
            public char EscapeChar { get; set; }
        }
    }
}
