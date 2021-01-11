using System;
using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Xunit;
using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests
{
    public class VariableRefTokenTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(VariableRefTokenParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                VariableRefToken result = VariableRefToken.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => VariableRefToken.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            VariableRefToken result;
            if (scenario.Modifier is null)
            {
                result = new VariableRefToken(scenario.VariableName);
            }
            else
            {
                result = new VariableRefToken(scenario.VariableName, scenario.Modifier, scenario.ModifierValue);
            }

            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void VariableName()
        {
            VariableRefToken token = new VariableRefToken("foo");
            Assert.Equal("foo", token.VariableName);
            Assert.Equal("foo", token.VariableNameToken.Value);

            token.VariableName = "test";
            Assert.Equal("test", token.VariableName);
            Assert.Equal("test", token.VariableNameToken.Value);

            token.VariableNameToken.Value = "test2";
            Assert.Equal("test2", token.VariableName);
            Assert.Equal("test2", token.VariableNameToken.Value);

            token.VariableNameToken = new StringToken("test3");
            Assert.Equal("test3", token.VariableName);
            Assert.Equal("test3", token.VariableNameToken.Value);

            Assert.Throws<ArgumentNullException>(() => token.VariableName = null);
            Assert.Throws<ArgumentException>(() => token.VariableName = "");
            Assert.Throws<ArgumentNullException>(() => token.VariableNameToken = null);
        }

        [Fact]
        public void Modifier()
        {
            VariableRefToken token = new VariableRefToken("foo", "-", "bar");
            Assert.Equal("-", token.Modifier);
            Assert.Collection(token.ModifierTokens, new Action<SymbolToken>[]
            {
                token => ValidateSymbol(token, '-')
            });

            token.Modifier = ":-";
            Assert.Equal(":-", token.Modifier);
            Assert.Collection(token.ModifierTokens, new Action<SymbolToken>[]
            {
                token => ValidateSymbol(token, ':'),
                token => ValidateSymbol(token, '-')
            });

            token.Modifier = null;
            Assert.Null(token.Modifier);
            Assert.Empty(token.ModifierTokens);
            Assert.Null(token.ModifierValue);
        }

        [Fact]
        public void ModifierValue()
        {
            VariableRefToken token = new VariableRefToken("foo", "-", "bar");
            Assert.Equal("bar", token.ModifierValue);
            Assert.Equal("bar", token.ModifierValueToken.Value);
            Assert.NotEmpty(token.ModifierTokens);
            Assert.Equal("${foo-bar}", token.ToString());

            token.ModifierValue = "test";
            Assert.Equal("test", token.ModifierValue);
            Assert.Equal("test", token.ModifierValueToken.Value);
            Assert.NotEmpty(token.ModifierTokens);
            Assert.Equal("${foo-test}", token.ToString());

            token.ModifierValueToken.Value = "test2";
            Assert.Equal("test2", token.ModifierValue);
            Assert.Equal("test2", token.ModifierValueToken.Value);
            Assert.NotEmpty(token.ModifierTokens);
            Assert.Equal("${foo-test2}", token.ToString());

            token.ModifierValue = null;
            Assert.Null(token.ModifierValue);
            Assert.Null(token.ModifierValueToken);
            Assert.Empty(token.ModifierTokens);
            Assert.Equal("${foo}", token.ToString());

            token.ModifierValueToken = new LiteralToken("test3");
            Assert.Equal("test3", token.ModifierValue);
            Assert.Equal("test3", token.ModifierValueToken.Value);
            Assert.Equal("${footest3}", token.ToString());

            token.Modifier = "-";
            Assert.NotEmpty(token.ModifierTokens);
            Assert.Equal("${foo-test3}", token.ToString());

            token.ModifierValueToken = null;
            Assert.Null(token.ModifierValue);
            Assert.Null(token.ModifierValueToken);
            Assert.Empty(token.ModifierTokens);
            Assert.Equal("${foo}", token.ToString());
        }

        [Fact]
        public void ModifierValueWithVariables()
        {
            VariableRefToken token = new VariableRefToken("foo", "-", "$var");
            TestHelper.TestVariablesWithNullableLiteral(
                () => token.ModifierValueToken, t => token.ModifierValueToken = t, val => token.ModifierValue = val, "var", canContainVariables: true);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            VariableRefTokenParseTestScenario[] testInputs = new VariableRefTokenParseTestScenario[]
            {
                new VariableRefTokenParseTestScenario
                {
                    Text = "$foo",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateString(token, "foo")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("foo", result.VariableName);
                    }
                },
                new VariableRefTokenParseTestScenario
                {
                    Text = "${foo}",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '{'),
                        token => ValidateString(token, "foo"),
                        token => ValidateSymbol(token, '}')
                    },
                    Validate = result =>
                    {
                        Assert.Equal("foo", result.VariableName);
                    }
                },
                new VariableRefTokenParseTestScenario
                {
                    Text = "${foo:-test}",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '{'),
                        token => ValidateString(token, "foo"),
                        token => ValidateSymbol(token, ':'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateLiteral(token, "test"),
                        token => ValidateSymbol(token, '}')
                    },
                    Validate = result =>
                    {
                        Assert.Equal("foo", result.VariableName);
                        Assert.Equal(":-", result.Modifier);
                        Assert.Equal("test", result.ModifierValue);
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
                    VariableName = "foo",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateString(token, "foo")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("foo", result.VariableName);
                    }
                },
                new CreateTestScenario
                {
                    VariableName = "foo",
                    Modifier = "-",
                    ModifierValue = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '{'),
                        token => ValidateString(token, "foo"),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<LiteralToken>(token, "test",
                            token => ValidateString(token, "test")),
                        token => ValidateSymbol(token, '}')
                    },
                    Validate = token =>
                    {
                        Dictionary<string, string> variables = new Dictionary<string, string>
                        {

                        };

                        string result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("test", result);

                        variables["foo"] = null;
                        result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("", result);

                        variables["foo"] = "test2";
                        result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("test2", result);
                    }
                },
                new CreateTestScenario
                {
                    VariableName = "foo",
                    Modifier = ":-",
                    ModifierValue = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '{'),
                        token => ValidateString(token, "foo"),
                        token => ValidateSymbol(token, ':'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<LiteralToken>(token, "test",
                            token => ValidateString(token, "test")),
                        token => ValidateSymbol(token, '}')
                    },
                    Validate = token =>
                    {
                        Dictionary<string, string> variables = new Dictionary<string, string>
                        {
                        };

                        string result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("test", result);

                        variables["foo"] = null;
                        result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("test", result);

                        variables["foo"] = "test2";
                        result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("test2", result);
                    }
                },
                new CreateTestScenario
                {
                    VariableName = "foo",
                    Modifier = "+",
                    ModifierValue = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '{'),
                        token => ValidateString(token, "foo"),
                        token => ValidateSymbol(token, '+'),
                        token => ValidateAggregate<LiteralToken>(token, "test",
                            token => ValidateString(token, "test")),
                        token => ValidateSymbol(token, '}')
                    },
                    Validate = token =>
                    {
                        Dictionary<string, string> variables = new Dictionary<string, string>
                        {
                        };

                        string result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("", result);

                        variables["foo"] = null;
                        result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("test", result);

                        variables["foo"] = "test2";
                        result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("test", result);
                    }
                },
                new CreateTestScenario
                {
                    VariableName = "foo",
                    Modifier = ":+",
                    ModifierValue = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '{'),
                        token => ValidateString(token, "foo"),
                        token => ValidateSymbol(token, ':'),
                        token => ValidateSymbol(token, '+'),
                        token => ValidateAggregate<LiteralToken>(token, "test",
                            token => ValidateString(token, "test")),
                        token => ValidateSymbol(token, '}')
                    },
                    Validate = token =>
                    {
                        Dictionary<string, string> variables = new Dictionary<string, string>
                        {
                        };

                        string result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("", result);

                        variables["foo"] = null;
                        result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("", result);

                        variables["foo"] = "test2";
                        result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("test", result);
                    }
                },
                new CreateTestScenario
                {
                    VariableName = "foo",
                    Modifier = "?",
                    ModifierValue = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '{'),
                        token => ValidateString(token, "foo"),
                        token => ValidateSymbol(token, '?'),
                        token => ValidateAggregate<LiteralToken>(token, "test",
                            token => ValidateString(token, "test")),
                        token => ValidateSymbol(token, '}')
                    },
                    Validate = token =>
                    {
                        Dictionary<string, string> variables = new Dictionary<string, string>
                        {
                        };

                        Assert.Throws<VariableSubstitutionException>(() => token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables));

                        variables["foo"] = null;
                        string result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("", result);

                        variables["foo"] = "test2";
                        result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("test2", result);
                    }
                },
                new CreateTestScenario
                {
                    VariableName = "foo",
                    Modifier = ":?",
                    ModifierValue = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '{'),
                        token => ValidateString(token, "foo"),
                        token => ValidateSymbol(token, ':'),
                        token => ValidateSymbol(token, '?'),
                        token => ValidateAggregate<LiteralToken>(token, "test",
                            token => ValidateString(token, "test")),
                        token => ValidateSymbol(token, '}')
                    },
                    Validate = token =>
                    {
                        Dictionary<string, string> variables = new Dictionary<string, string>
                        {
                        };

                        Assert.Throws<VariableSubstitutionException>(() => token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables));

                        variables["foo"] = null;
                        Assert.Throws<VariableSubstitutionException>(() => token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables));

                        variables["foo"] = "test2";
                        string result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("test2", result);
                    }
                },
                new CreateTestScenario
                {
                    VariableName = "foo",
                    Modifier = ":-",
                    ModifierValue = "a${bar}x",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '{'),
                        token => ValidateString(token, "foo"),
                        token => ValidateSymbol(token, ':'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<LiteralToken>(token, "a${bar}x",
                            token => ValidateString(token, "a"),
                            token => ValidateAggregate<VariableRefToken>(token, "${bar}",
                                token => ValidateSymbol(token, '{'),
                                token => ValidateString(token, "bar"),
                                token => ValidateSymbol(token, '}')),
                            token => ValidateString(token, "x")),
                        token => ValidateSymbol(token, '}'),
                    },
                    Validate = token =>
                    {
                        Dictionary<string, string> variables = new Dictionary<string, string>
                        {
                            { "bar", "test2" }
                        };

                        string result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                        Assert.Equal("atest2x", result);

                    }
                },
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class VariableRefTokenParseTestScenario : ParseTestScenario<VariableRefToken>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<VariableRefToken>
        {
            public string VariableName { get; set; }
            public string Modifier { get; set; }
            public string ModifierValue { get; set; }
        }
    }
}
