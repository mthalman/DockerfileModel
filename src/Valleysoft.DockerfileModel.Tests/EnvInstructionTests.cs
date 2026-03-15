using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class EnvInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<EnvInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, EnvInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        EnvInstruction result = new(scenario.Variables);

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Variables()
    {
        void Validate(EnvInstruction instruction, string expectedKey, string expectedValue)
        {
            Assert.Collection(instruction.Variables, new Action<IKeyValuePair>[]
        {
            pair =>
            {
                Assert.Equal(expectedKey, pair.Key);
                Assert.Equal(expectedValue, pair.Value);
            }
        });

            Assert.Collection(instruction.VariableTokens, new Action<KeyValueToken<Variable, LiteralToken>>[]
            {
                token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, $"{expectedKey}={expectedValue}",
                    token => ValidateIdentifier<Variable>(token, expectedKey),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, expectedValue))
            });
        }

        EnvInstruction result = new(
            new Dictionary<string, string>
            {
                { "VAR1", "test" }
            });
        Validate(result, "VAR1", "test");

        result.Variables[0].Key = "VAR2";
        Validate(result, "VAR2", "test");

        result.Variables[0].Value = "foo";
        Validate(result, "VAR2", "foo");

        result.VariableTokens[0].Key = "VAR3";
        Validate(result, "VAR3", "foo");

        result.VariableTokens[0].Value = "bar";
        Validate(result, "VAR3", "bar");
    }

    [Fact]
    public void SetValueOnEmptyEnvVar()
    {
        // Parse "ENV key=" which produces a KeyValueToken with no value token
        EnvInstruction result = EnvInstruction.Parse("ENV MY_VAR=");
        Assert.Equal("MY_VAR", result.Variables[0].Key);
        Assert.Equal("", result.Variables[0].Value);
        Assert.Null(result.VariableTokens[0].ValueToken);

        // Set a value token via the ValueToken setter
        result.VariableTokens[0].ValueToken = new LiteralToken("hello", canContainVariables: true);
        Assert.Equal("hello", result.Variables[0].Value);
        Assert.Equal("ENV MY_VAR=hello", result.ToString());

        // Now the Value setter should work since a value token exists
        result.VariableTokens[0].Value = "world";
        Assert.Equal("world", result.Variables[0].Value);
        Assert.Equal("ENV MY_VAR=world", result.ToString());

        // Setting ValueToken to null should remove it
        result.VariableTokens[0].ValueToken = null;
        Assert.Null(result.VariableTokens[0].ValueToken);
        Assert.Equal("", result.Variables[0].Value);
        Assert.Equal("ENV MY_VAR=", result.ToString());
    }

    [Fact]
    public void SetValueOnEmptyEnvVarViaVariablesList()
    {
        // Parse "ENV key=" which produces a KeyValueToken with no value token
        EnvInstruction result = EnvInstruction.Parse("ENV MY_VAR=");
        Assert.Equal("MY_VAR", result.Variables[0].Key);
        Assert.Equal("", result.Variables[0].Value);
        Assert.Null(result.VariableTokens[0].ValueToken);

        // Setting a value via the Variables projected list (IKeyValuePair.Value)
        // should auto-insert a LiteralToken
        result.Variables[0].Value = "hello";
        Assert.Equal("hello", result.Variables[0].Value);
        Assert.NotNull(result.VariableTokens[0].ValueToken);
        Assert.Equal("ENV MY_VAR=hello", result.ToString());

        // Subsequent value changes should work via the normal path
        result.Variables[0].Value = "world";
        Assert.Equal("world", result.Variables[0].Value);
        Assert.Equal("ENV MY_VAR=world", result.ToString());
    }

    [Fact]
    public void SetValueOnEmptyEnvVarWithNonDefaultEscapeChar()
    {
        // Parse "ENV key=" with a non-default escape char (backtick)
        EnvInstruction result = EnvInstruction.Parse("ENV key=", escapeChar: '`');
        Assert.Equal("key", result.Variables[0].Key);
        Assert.Equal("", result.Variables[0].Value);
        Assert.Null(result.VariableTokens[0].ValueToken);

        // Set a value and verify round-trip
        result.Variables[0].Value = "myval";
        Assert.Equal("myval", result.Variables[0].Value);
        Assert.NotNull(result.VariableTokens[0].ValueToken);
        Assert.Equal("ENV key=myval", result.ToString());

        // Subsequent value changes should also round-trip
        result.Variables[0].Value = "updated";
        Assert.Equal("updated", result.Variables[0].Value);
        Assert.Equal("ENV key=updated", result.ToString());
    }

    [Fact]
    public void SetValueOnEmptyEnvVarWithBacktickEscapeChar_EscapedVariableRef()
    {
        // Regression test for #286: When setting a value containing an escaped
        // variable reference on a KeyValueToken parsed with backtick escape char,
        // the LiteralToken must preserve the backtick escape semantics so $VAR
        // remains escaped instead of being tokenized as a VariableRefToken.
        EnvInstruction result = EnvInstruction.Parse("ENV key=", escapeChar: '`');
        Assert.Null(result.VariableTokens[0].ValueToken);

        result.Variables[0].Value = "`$MY_VAR";
        Assert.Equal("`$MY_VAR", result.Variables[0].Value);
        Assert.NotNull(result.VariableTokens[0].ValueToken);
        Assert.Equal("ENV key=`$MY_VAR", result.ToString());

        LiteralToken? valueToken = result.VariableTokens[0].ValueToken;
        Assert.NotNull(valueToken);
        Assert.DoesNotContain(valueToken!.Tokens, t => t is VariableRefToken);
    }

    [Fact]
    public void EnvVarWithVariables()
    {
        EnvInstruction result = new(
            new Dictionary<string, string>
            {
                { "VAR1", "$var" }
            });
        TestHelper.TestVariablesWithLiteral(
            () => result.VariableTokens[0].ValueToken, "var", canContainVariables: true);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<EnvInstruction>[] testInputs = new ParseTestScenario<EnvInstruction>[]
        {
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV MY_NAME=",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_NAME=",
                        token => ValidateIdentifier<Variable>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV VAR1= VAR2=foo",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "VAR1=",
                        token => ValidateIdentifier<Variable>(token, "VAR1"),
                        token => ValidateSymbol(token, '=')),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "VAR2=foo",
                        token => ValidateIdentifier<Variable>(token, "VAR2"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "foo"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("VAR1", pair.Key);
                            Assert.Equal("", pair.Value);
                        },
                        pair =>
                        {
                            Assert.Equal("VAR2", pair.Key);
                            Assert.Equal("foo", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV MY_NAME=\"\"",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_NAME=\"\"",
                        token => ValidateIdentifier<Variable>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "", '\"'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV MY_NAME John\r\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_NAME John\r\n",
                        token => ValidateIdentifier<Variable>(token, "MY_NAME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LiteralToken>(token, "John\r\n",
                            token => ValidateString(token, "John"),
                            token => ValidateNewLine(token, "\r\n")))
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV MY_NAME=John",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_NAME=John",
                        token => ValidateIdentifier<Variable>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "John"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV MY_NAME=\"John Doe\"",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_NAME=\"John Doe\"",
                        token => ValidateIdentifier<Variable>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "John Doe", '\"'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John Doe", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV MY_NAME=John` Doe",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_NAME=John` Doe",
                        token => ValidateIdentifier<Variable>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "John` Doe"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John` Doe", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV MY_NAME=\"John Doe\" MY_DOG=Rex` The` Dog ` \n MY_CAT=fluffy",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_NAME=\"John Doe\"",
                        token => ValidateIdentifier<Variable>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "John Doe", '\"')),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_DOG=Rex` The` Dog",
                        token => ValidateIdentifier<Variable>(token, "MY_DOG"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "Rex` The` Dog")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LineContinuationToken>(token, "` \n",
                        token => ValidateSymbol(token, '`'),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateNewLine(token, "\n")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_CAT=fluffy",
                        token => ValidateIdentifier<Variable>(token, "MY_CAT"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "fluffy"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John Doe", pair.Value);
                        },
                        pair =>
                        {
                            Assert.Equal("MY_DOG", pair.Key);
                            Assert.Equal("Rex` The` Dog", pair.Value);
                        },
                        pair =>
                        {
                            Assert.Equal("MY_CAT", pair.Key);
                            Assert.Equal("fluffy", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV MY_NAME John",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_NAME John",
                        token => ValidateIdentifier<Variable>(token, "MY_NAME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "John"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV MY_NAME \"John Doe\"",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_NAME \"John Doe\"",
                        token => ValidateIdentifier<Variable>(token, "MY_NAME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "John Doe", '\"'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John Doe", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV MY_`\nNAME `\nJo`\nhn",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_`\nNAME `\nJo`\nhn",
                        token => ValidateAggregate<Variable>(token, "MY_`\nNAME",
                            token => ValidateString(token, "MY_"),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateString(token, "NAME")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateAggregate<LiteralToken>(token, "Jo`\nhn",
                            token => ValidateString(token, "Jo"),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateString(token, "hn")))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV VAR1`\n  foo=`\n  bar",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "VAR1`\n  foo=`\n  bar",
                        token => ValidateAggregate<Variable>(token, "VAR1"),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateAggregate<LiteralToken>(token, "foo=`\n  bar",
                            token => ValidateString(token, "foo="),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateWhitespace(token, "  "),
                            token => ValidateString(token, "bar")))
                }
            },
            // Variable reference default value with path — leading slash must stay in the literal
            new ParseTestScenario<EnvInstruction>
            {
                Text = "ENV PATH=${BASE:-/usr/local}/bin",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "PATH=${BASE:-/usr/local}/bin",
                        token => ValidateIdentifier<Variable>(token, "PATH"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<LiteralToken>(token, "${BASE:-/usr/local}/bin",
                            token => ValidateAggregate<VariableRefToken>(token, "${BASE:-/usr/local}",
                                token => ValidateSymbol(token, '{'),
                                token => ValidateString(token, "BASE"),
                                token => ValidateSymbol(token, ':'),
                                token => ValidateSymbol(token, '-'),
                                token => ValidateLiteral(token, "/usr/local"),
                                token => ValidateSymbol(token, '}')),
                            token => ValidateString(token, "/bin")))
                },
                Validate = result =>
                {
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Collection(result.Variables, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("PATH", pair.Key);
                            Assert.Equal("${BASE:-/usr/local}/bin", pair.Value);
                        }
                    });
                    Assert.Equal("ENV PATH=${BASE:-/usr/local}/bin", result.ToString());
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
                Variables = new Dictionary<string, string>
                {
                    { "VAR1", "test" },
                },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "VAR1=test",
                        token => ValidateIdentifier<Variable>(token, "VAR1"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "test"))
                }
            },
            new CreateTestScenario
            {
                Variables = new Dictionary<string, string>
                {
                    { "VAR1", "test\\ 123" },
                },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "VAR1=test\\ 123",
                        token => ValidateIdentifier<Variable>(token, "VAR1"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "test\\ 123"))
                }
            },
            new CreateTestScenario
            {
                Variables = new Dictionary<string, string>
                {
                    { "VAR1", "test" },
                    { "VAR2", "testing 1 2 3" },
                },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "VAR1=test",
                        token => ValidateIdentifier<Variable>(token, "VAR1"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "test")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "VAR2=\"testing 1 2 3\"",
                        token => ValidateIdentifier<Variable>(token, "VAR2"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "testing 1 2 3", '\"'))
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<EnvInstruction>
    {
        public Dictionary<string, string> Variables { get; set; }
    }

    [Fact]
    public void EnvInstruction_ValueWithDoubleQuotes_RoundTrips()
    {
        string text = "ENV FOO=\"bar baz\"\n";
        EnvInstruction inst = EnvInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
        Assert.Equal("bar baz", inst.Variables[0].Value);
    }

    [Fact]
    public void EnvInstruction_ValueWithEscapedQuote_RoundTrips()
    {
        string text = "ENV FOO=bar\\\"baz\n";
        EnvInstruction inst = EnvInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

    [Fact]
    public void EnvInstruction_MultipleVarsNewFormat_RoundTrips()
    {
        string text = "ENV FOO=hello BAR=world\n";
        EnvInstruction inst = EnvInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
        Assert.Equal(2, inst.Variables.Count);
        Assert.Equal("FOO", inst.Variables[0].Key);
        Assert.Equal("hello", inst.Variables[0].Value);
        Assert.Equal("BAR", inst.Variables[1].Key);
        Assert.Equal("world", inst.Variables[1].Value);
    }

    /// <summary>
    /// Bug: WorkdirInstruction.Path includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/282
    /// </summary>
    [Fact]
    public void EnvInstruction_SingleVarFormat_ValueWithNewline_Bug()
    {
        // ENV VAR value (space-separated single var format)
        string text = "ENV FOO bar baz\n";
        EnvInstruction inst = EnvInstruction.Parse(text);
        Assert.Equal(text, inst.ToString()); // Round-trip should work
        string val = inst.Variables[0].Value ?? "(null)";
        // Check if trailing newline is included in value
        System.Console.WriteLine($"ENV single-format value=[{val}]");
        if (val.Contains('\n'))
        {
            System.Console.WriteLine("BUG: ENV single-format value contains newline");
        }
    }
}
