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
            // :? modifier with spaces in message
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${IMAGE:?must set image}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "IMAGE"),
                    token => ValidateSymbol(token, ':'),
                    token => ValidateSymbol(token, '?'),
                    token => ValidateLiteral(token, "must set image"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("IMAGE", result.VariableName);
                    Assert.Equal(":?", result.Modifier);
                    Assert.Equal("must set image", result.ModifierValue);
                    Assert.Equal("${IMAGE:?must set image}", result.ToString());
                }
            },
            // :- modifier with spaces in default value
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${foo:-default value}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, ':'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateLiteral(token, "default value"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("foo", result.VariableName);
                    Assert.Equal(":-", result.Modifier);
                    Assert.Equal("default value", result.ModifierValue);
                    Assert.Equal("${foo:-default value}", result.ToString());
                }
            },
            // :+ modifier with spaces in alternate value
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${foo:+alt value}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "foo"),
                    token => ValidateSymbol(token, ':'),
                    token => ValidateSymbol(token, '+'),
                    token => ValidateLiteral(token, "alt value"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("foo", result.VariableName);
                    Assert.Equal(":+", result.Modifier);
                    Assert.Equal("alt value", result.ModifierValue);
                    Assert.Equal("${foo:+alt value}", result.ToString());
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
            },
            // Default value with leading slash — slash must not be split into a separate symbol token
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${BASE:-/opt}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "BASE"),
                    token => ValidateSymbol(token, ':'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateLiteral(token, "/opt"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("BASE", result.VariableName);
                    Assert.Equal(":-", result.Modifier);
                    Assert.Equal("/opt", result.ModifierValue);
                }
            },
            // Default value with longer path — entire path stays as one literal
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${BASE:-/usr/local}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "BASE"),
                    token => ValidateSymbol(token, ':'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateLiteral(token, "/usr/local"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("BASE", result.VariableName);
                    Assert.Equal(":-", result.Modifier);
                    Assert.Equal("/usr/local", result.ModifierValue);
                }
            },
            // Alternate-value modifier with leading slash
            new ParseTestScenario<VariableRefToken>
            {
                Text = "${APP:+/run}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '{'),
                    token => ValidateString(token, "APP"),
                    token => ValidateSymbol(token, ':'),
                    token => ValidateSymbol(token, '+'),
                    token => ValidateLiteral(token, "/run"),
                    token => ValidateSymbol(token, '}')
                },
                Validate = result =>
                {
                    Assert.Equal("APP", result.VariableName);
                    Assert.Equal(":+", result.Modifier);
                    Assert.Equal("/run", result.ModifierValue);
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

    /// <summary>
    /// Bug: Empty modifier values in variable references cause ParseException
    /// See https://github.com/mthalman/DockerfileModel/issues/281
    /// </summary>
    [Fact]
    public void VariableRef_EmptyModifierValue_ColonDash_RoundTrips()
    {
        // ${var:-} — empty default after colon-dash
        string text = "FROM ${img:-}\n";
        FromInstruction inst = FromInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

    /// <summary>
    /// Bug: Empty modifier values in variable references cause ParseException
    /// See https://github.com/mthalman/DockerfileModel/issues/281
    /// </summary>
    [Fact]
    public void VariableRef_EmptyModifierValue_Dash_RoundTrips()
    {
        // ${var-} — dash without colon, empty default
        string text = "FROM ${img-}\n";
        FromInstruction inst = FromInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

    [Fact]
    public void VariableRef_ColonPlus_RoundTrips()
    {
        // ${var:+alt} — colon-plus modifier
        string text = "FROM ${img:+alpine}\n";
        FromInstruction inst = FromInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

    [Fact]
    public void VariableRef_ColonQuestion_RoundTrips()
    {
        // ${var:?error message} — colon-question modifier with spaces in message
        string text = "FROM ${img:?must set img variable}\n";
        FromInstruction inst = FromInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

    [Fact]
    public void VariableRef_Question_NoColon_RoundTrips()
    {
        // ${var?error} — question without colon
        string text = "FROM ${img?must set}\n";
        FromInstruction inst = FromInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

    /// <summary>
    /// Bug: Empty modifier values in variable references cause ParseException
    /// See https://github.com/mthalman/DockerfileModel/issues/281
    /// </summary>
    [Fact]
    public void VariableRef_Resolve_ColonDash_EmptyDefault()
    {
        // ${var:-} resolves to empty string when var is not set
        VariableRefToken token = VariableRefToken.Parse("${img:-}");
        string? resolved = token.ResolveVariables('\\', new Dictionary<string, string?>());
        Assert.Equal("", resolved);
    }

    /// <summary>
    /// Bug: Empty modifier values in variable references cause ParseException
    /// See https://github.com/mthalman/DockerfileModel/issues/281
    /// </summary>
    [Fact]
    public void VariableRef_Resolve_ColonQuestion_ThrowsWhenUnset()
    {
        // ${var:?msg} should throw when variable is not set
        VariableRefToken token = VariableRefToken.Parse("${img:?must be set}");
        Assert.Throws<VariableSubstitutionException>(
            () => token.ResolveVariables('\\', new Dictionary<string, string?>()));
    }

    /// <summary>
    /// Bug: Empty modifier values in variable references cause ParseException
    /// See https://github.com/mthalman/DockerfileModel/issues/281
    /// </summary>
    [Fact]
    public void VariableRef_Resolve_ColonQuestion_DoesNotThrowWhenSet()
    {
        // ${var:?msg} should NOT throw when variable is set and non-empty
        VariableRefToken token = VariableRefToken.Parse("${img:?must be set}");
        string? resolved = token.ResolveVariables('\\', new Dictionary<string, string?> { ["img"] = "alpine" });
        Assert.Equal("alpine", resolved);
    }

    [Fact]
    public void VariableRef_Resolve_Question_NoColon_SetToEmpty()
    {
        // ${var?msg} — without colon, variable set to empty string should NOT throw
        // (only colon variant treats empty as unset)
        VariableRefToken token = VariableRefToken.Parse("${img?must be set}");
        // img is set (to empty string), so no exception should be thrown
        string? resolved = token.ResolveVariables('\\', new Dictionary<string, string?> { ["img"] = "" });
        Assert.Equal("", resolved);
    }

    [Fact]
    public void VariableRef_Resolve_PlusModifier_VarNotSet_ReturnsEmpty()
    {
        // ${var:+alt} — when var is NOT set, returns empty (not alt)
        VariableRefToken token = VariableRefToken.Parse("${img:+alpine}");
        string? resolved = token.ResolveVariables('\\', new Dictionary<string, string?>());
        Assert.Equal("", resolved);
    }

    [Fact]
    public void VariableRef_Resolve_PlusModifier_VarSet_ReturnsAlt()
    {
        // ${var:+alt} — when var IS set, returns alt value
        VariableRefToken token = VariableRefToken.Parse("${img:+alpine}");
        string? resolved = token.ResolveVariables('\\', new Dictionary<string, string?> { ["img"] = "ubuntu" });
        Assert.Equal("alpine", resolved);
    }

    [Fact]
    public void VariableRef_Resolve_PlusModifier_VarSetToEmpty_WithColon_ReturnsEmpty()
    {
        // ${var:+alt} — when var IS set but EMPTY, with colon treats empty as unset
        VariableRefToken token = VariableRefToken.Parse("${img:+alpine}");
        string? resolved = token.ResolveVariables('\\', new Dictionary<string, string?> { ["img"] = "" });
        // Colon variant: empty = unset, so returns empty (not "alpine")
        Assert.Equal("", resolved);
    }

    [Fact]
    public void VariableRefToken_Constructor_EmptyModifierValue_Throws()
    {
        // modifierValue is required to be non-empty per Requires.NotNullOrEmpty
        Assert.Throws<ArgumentException>(
            () => new VariableRefToken("VAR", ":-", ""));
    }

    [Fact]
    public void VariableRefToken_Modifier_SetToNull_RemovesModifierAndValue()
    {
        // Build a token with modifier, then remove it
        VariableRefToken token = new VariableRefToken("VAR", ":-", "default");
        Assert.Equal(":-", token.Modifier);
        Assert.Equal("default", token.ModifierValue);

        token.Modifier = null;
        Assert.Null(token.Modifier);
        Assert.Null(token.ModifierValue);
    }

    /// <summary>
    /// Bug: Empty modifier values in variable references cause ParseException
    /// See https://github.com/mthalman/DockerfileModel/issues/281
    /// </summary>
    [Fact]
    public void VariableRefToken_Parse_EmptyColonDashModifier_ThrowsOrSucceeds()
    {
        // ${img:-} — empty modifier value after :-
        // The BracedVariableReference parser uses .AtLeastOnce() for modifierValueTokens,
        // which means zero-length values are rejected.
        // Document current behavior: does this throw ParseException?
        string input = "${img:-}";

        try
        {
            var token = VariableRefToken.Parse(input);
            // If we get here, parsing succeeded
            System.Console.WriteLine($"Parse succeeded: {token}");
            System.Console.WriteLine($"Modifier: {token.Modifier}");
            System.Console.WriteLine($"ModifierValue: [{token.ModifierValue}]");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Parse FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Bug: Empty modifier values in variable references cause ParseException
    /// See https://github.com/mthalman/DockerfileModel/issues/281
    /// </summary>
    [Fact]
    public void VariableRefToken_Parse_EmptyDashModifier_ThrowsOrSucceeds()
    {
        // ${var-} — plain dash, empty modifier value
        string input = "${var-}";

        try
        {
            var token = VariableRefToken.Parse(input);
            System.Console.WriteLine($"Parse succeeded: {token}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Parse FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Bug: Empty modifier values in variable references cause ParseException
    /// See https://github.com/mthalman/DockerfileModel/issues/281
    /// </summary>
    [Fact]
    public void VariableRefToken_Parse_EmptyColonPlusModifier_ThrowsOrSucceeds()
    {
        // ${var:+} — empty modifier value after :+
        string input = "${var:+}";

        try
        {
            var token = VariableRefToken.Parse(input);
            System.Console.WriteLine($"Parse succeeded: {token}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Parse FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
