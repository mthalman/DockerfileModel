using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class VariableRefTokenTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<VariableRefToken> scenario) =>
        TestHelper.RunParseTest(scenario, VariableRefToken.Parse);

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
        VariableRefToken token = new("foo");
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
        VariableRefToken token = new("foo", "-", "bar");
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
        VariableRefToken token = new("foo", "-", "bar");
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
        VariableRefToken token = new("foo", "-", "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => token.ModifierValueToken, t => token.ModifierValueToken = t, val => token.ModifierValue = val, "var", canContainVariables: true);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<VariableRefToken>[] testInputs = new ParseTestScenario<VariableRefToken>[]
        {
            new ParseTestScenario<VariableRefToken>
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
            new ParseTestScenario<VariableRefToken>
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
            new ParseTestScenario<VariableRefToken>
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
            },
            // POSIX prefix removal: # (shortest)
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${foo#pattern}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '#'),
                    token => ValidateLiteral(token, "pattern"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("foo", result.VariableName);
                    Assert.Equal("#", result.Modifier);
                    Assert.Equal("pattern", result.ModifierValue);
                }
            },
            // POSIX prefix removal: ## (longest)
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${foo##pattern}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '#'),
                    token => ValidateSymbol(token, '#'),
                    token => ValidateLiteral(token, "pattern"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("foo", result.VariableName);
                    Assert.Equal("##", result.Modifier);
                    Assert.Equal("pattern", result.ModifierValue);
                }
            },
            // POSIX suffix removal: % (shortest)
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${foo%suffix}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '%'),
                    token => ValidateLiteral(token, "suffix"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("foo", result.VariableName);
                    Assert.Equal("%", result.Modifier);
                    Assert.Equal("suffix", result.ModifierValue);
                }
            },
            // POSIX suffix removal: %% (longest)
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${foo%%suffix}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '%'),
                    token => ValidateSymbol(token, '%'),
                    token => ValidateLiteral(token, "suffix"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("foo", result.VariableName);
                    Assert.Equal("%%", result.Modifier);
                    Assert.Equal("suffix", result.ModifierValue);
                }
            },
            // POSIX replacement: / (first occurrence)
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${foo/old/new}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateLiteral(token, "old/new"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("foo", result.VariableName);
                    Assert.Equal("/", result.Modifier);
                    Assert.Equal("old/new", result.ModifierValue);
                }
            },
            // POSIX replacement: // (all occurrences)
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${foo//old/new}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateLiteral(token, "old/new"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("foo", result.VariableName);
                    Assert.Equal("//", result.Modifier);
                    Assert.Equal("old/new", result.ModifierValue);
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
                    Dictionary<string, string> variables = new() {

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
                    Dictionary<string, string> variables = new() {
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
                    Dictionary<string, string> variables = new() {
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
                    Dictionary<string, string> variables = new() {
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
                    Dictionary<string, string> variables = new() {
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
                    Dictionary<string, string> variables = new() {
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
                    Dictionary<string, string> variables = new() {
                        { "bar", "test2" }
                    };

                    string result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                    Assert.Equal("atest2x", result);

                }
            },
            // POSIX prefix removal: # (shortest) — returns raw text since not supported for resolution
            new CreateTestScenario
            {
                VariableName = "foo",
                Modifier = "#",
                ModifierValue = "pattern",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '#'),
                    token => ValidateAggregate<LiteralToken>(token, "pattern",
                        token => ValidateString(token, "pattern")),
                    token => ValidateSymbol(token, '}')
                },
                Validate = token =>
                {
                    Dictionary<string, string?> variables = new() {
                        { "foo", "hello_world" }
                    };

                    // POSIX modifiers return the raw variable reference text unchanged
                    string? result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                    Assert.Equal("${foo#pattern}", result);
                }
            },
            // POSIX prefix removal: ## (longest) — returns raw text since not supported for resolution
            new CreateTestScenario
            {
                VariableName = "foo",
                Modifier = "##",
                ModifierValue = "pattern",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '#'),
                    token => ValidateSymbol(token, '#'),
                    token => ValidateAggregate<LiteralToken>(token, "pattern",
                        token => ValidateString(token, "pattern")),
                    token => ValidateSymbol(token, '}')
                },
                Validate = token =>
                {
                    Dictionary<string, string?> variables = new() {
                        { "foo", "hello_world" }
                    };

                    string? result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                    Assert.Equal("${foo##pattern}", result);
                }
            },
            // POSIX suffix removal: % (shortest) — returns raw text since not supported for resolution
            new CreateTestScenario
            {
                VariableName = "foo",
                Modifier = "%",
                ModifierValue = "suffix",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '%'),
                    token => ValidateAggregate<LiteralToken>(token, "suffix",
                        token => ValidateString(token, "suffix")),
                    token => ValidateSymbol(token, '}')
                },
                Validate = token =>
                {
                    Dictionary<string, string?> variables = new() {
                        { "foo", "hello_world" }
                    };

                    string? result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                    Assert.Equal("${foo%suffix}", result);
                }
            },
            // POSIX suffix removal: %% (longest) — returns raw text since not supported for resolution
            new CreateTestScenario
            {
                VariableName = "foo",
                Modifier = "%%",
                ModifierValue = "suffix",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '%'),
                    token => ValidateSymbol(token, '%'),
                    token => ValidateAggregate<LiteralToken>(token, "suffix",
                        token => ValidateString(token, "suffix")),
                    token => ValidateSymbol(token, '}')
                },
                Validate = token =>
                {
                    Dictionary<string, string?> variables = new() {
                        { "foo", "hello_world" }
                    };

                    string? result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                    Assert.Equal("${foo%%suffix}", result);
                }
            },
            // POSIX replacement: / (first occurrence) — returns raw text since not supported for resolution
            new CreateTestScenario
            {
                VariableName = "foo",
                Modifier = "/",
                ModifierValue = "old/new",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateAggregate<LiteralToken>(token, "old/new",
                        token => ValidateString(token, "old/new")),
                    token => ValidateSymbol(token, '}')
                },
                Validate = token =>
                {
                    Dictionary<string, string?> variables = new() {
                        { "foo", "hello_world" }
                    };

                    string? result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                    Assert.Equal("${foo/old/new}", result);
                }
            },
            // POSIX replacement: // (all occurrences) — returns raw text since not supported for resolution
            new CreateTestScenario
            {
                VariableName = "foo",
                Modifier = "//",
                ModifierValue = "old/new",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateAggregate<LiteralToken>(token, "old/new",
                        token => ValidateString(token, "old/new")),
                    token => ValidateSymbol(token, '}')
                },
                Validate = token =>
                {
                    Dictionary<string, string?> variables = new() {
                        { "foo", "hello_world" }
                    };

                    string? result = token.ResolveVariables(Dockerfile.DefaultEscapeChar, variables);
                    Assert.Equal("${foo//old/new}", result);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<VariableRefToken>
    {
        public string VariableName { get; set; }
        public string Modifier { get; set; }
        public string ModifierValue { get; set; }
    }
}
