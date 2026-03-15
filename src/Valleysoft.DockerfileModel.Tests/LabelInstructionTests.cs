using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class LabelInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<LabelInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, LabelInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        LabelInstruction result = new(scenario.Variables);

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Labels()
    {
        void Validate(LabelInstruction instruction, string expectedKey, string expectedValue)
        {
            Assert.Collection(instruction.Labels, new Action<IKeyValuePair>[]
            {
                pair =>
                {
                    Assert.Equal(expectedKey, pair.Key);
                    Assert.Equal(expectedValue, pair.Value);
                }
            });

            Assert.Collection(instruction.LabelTokens, new Action<KeyValueToken<LabelKeyToken, LiteralToken>>[]
            {
                token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, $"{expectedKey}={expectedValue}",
                    token => ValidateIdentifier<LabelKeyToken>(token, expectedKey),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, expectedValue))
            });
        }

        LabelInstruction result = new(
            new Dictionary<string, string>
            {
                { "VAR1", "test" }
            });
        Validate(result, "VAR1", "test");

        result.Labels[0].Key = "VAR2";
        Validate(result, "VAR2", "test");

        result.Labels[0].Value = "foo";
        Validate(result, "VAR2", "foo");

        result.LabelTokens[0].Key = "VAR3";
        Validate(result, "VAR3", "foo");

        result.LabelTokens[0].Value = "bar";
        Validate(result, "VAR3", "bar");
    }

    [Fact]
    public void SetValueOnEmptyLabel()
    {
        // Parse "LABEL key=" which produces a KeyValueToken with no value token
        LabelInstruction result = LabelInstruction.Parse("LABEL MY_LABEL=");
        Assert.Equal("MY_LABEL", result.Labels[0].Key);
        Assert.Equal("", result.Labels[0].Value);
        Assert.Null(result.LabelTokens[0].ValueToken);

        // Setting a value via the Labels projected list should insert a LiteralToken
        result.Labels[0].Value = "hello";
        Assert.Equal("hello", result.Labels[0].Value);
        Assert.NotNull(result.LabelTokens[0].ValueToken);
        Assert.Equal("LABEL MY_LABEL=hello", result.ToString());

        // Subsequent value changes should work via the normal path
        result.Labels[0].Value = "world";
        Assert.Equal("world", result.Labels[0].Value);
        Assert.Equal("LABEL MY_LABEL=world", result.ToString());
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<LabelInstruction>[] testInputs = new ParseTestScenario<LabelInstruction>[]
        {
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL MY_NAME=",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "MY_NAME=",
                        token => ValidateIdentifier<LabelKeyToken>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL MY_NAME=\"\"",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "MY_NAME=\"\"",
                        token => ValidateIdentifier<LabelKeyToken>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "", '\"'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL MY_NAME=John",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "MY_NAME=John",
                        token => ValidateIdentifier<LabelKeyToken>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "John"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL MY_NAME=\"John Doe\"",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "MY_NAME=\"John Doe\"",
                        token => ValidateIdentifier<LabelKeyToken>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "John Doe", '\"'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John Doe", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL MY_NAME=\"John `\nDoe\"",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "MY_NAME=\"John `\nDoe\"",
                        token => ValidateIdentifier<LabelKeyToken>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateQuotableAggregate<LiteralToken>(token, "John `\nDoe", '\"',
                            token => ValidateString(token, "John "),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateString(token, "Doe")))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John Doe", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL \"MY_NAME\"=John",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "\"MY_NAME\"=John",
                        token => ValidateIdentifier<LabelKeyToken>(token, "MY_NAME", '\"'),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "John"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("MY_NAME", pair.Key);
                            Assert.Equal("John", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL MY_NAME=\"John Doe\" MY_DOG=Rex` The` Dog ` \n MY_CAT=fluffy",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "MY_NAME=\"John Doe\"",
                        token => ValidateIdentifier<LabelKeyToken>(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "John Doe", '\"')),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "MY_DOG=Rex` The` Dog",
                        token => ValidateIdentifier<LabelKeyToken>(token, "MY_DOG"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "Rex` The` Dog")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LineContinuationToken>(token, "` \n",
                        token => ValidateSymbol(token, '`'),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateNewLine(token, "\n")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "MY_CAT=fluffy",
                        token => ValidateIdentifier<LabelKeyToken>(token, "MY_CAT"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "fluffy"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
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
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL mykey=$var",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "mykey=$var",
                        token => ValidateIdentifier<LabelKeyToken>(token, "mykey"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateQuotableAggregate<LiteralToken>(token, "$var", null,
                            token => ValidateAggregate<VariableRefToken>(token, "$var",
                                token => ValidateString(token, "var"))))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("mykey", pair.Key);
                            Assert.Equal("$var", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL mykey=${var}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "mykey=${var}",
                        token => ValidateIdentifier<LabelKeyToken>(token, "mykey"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateQuotableAggregate<LiteralToken>(token, "${var}", null,
                            token => ValidateAggregate<VariableRefToken>(token, "${var}",
                                token => ValidateSymbol(token, '{'),
                                token => ValidateString(token, "var"),
                                token => ValidateSymbol(token, '}'))))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("mykey", pair.Key);
                            Assert.Equal("${var}", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL \"it's\"=value",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "\"it's\"=value",
                        token => ValidateIdentifier<LabelKeyToken>(token, "it's", '\"'),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "value"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("it's", pair.Key);
                            Assert.Equal("value", pair.Value);
                        }
                    });
                }
            },
            // Hash (#) in unquoted LABEL values is NOT a comment delimiter — it is regular text.
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL key=#value",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "key=#value",
                        token => ValidateIdentifier<LabelKeyToken>(token, "key"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "#value"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("key", pair.Key);
                            Assert.Equal("#value", pair.Value);
                        }
                    });
                }
            },
            // Hash in a CSS hex color code LABEL value.
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL color=#FF0000",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "color=#FF0000",
                        token => ValidateIdentifier<LabelKeyToken>(token, "color"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "#FF0000"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("LABEL", result.InstructionName);
                    Assert.Collection(result.Labels, new Action<IKeyValuePair>[]
                    {
                        pair =>
                        {
                            Assert.Equal("color", pair.Key);
                            Assert.Equal("#FF0000", pair.Value);
                        }
                    });
                }
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL $var=1",
                ParseExceptionPosition = new Position(6, 1, 7)
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL \"$_var\"=value",
                ParseExceptionPosition = new Position(7, 1, 8)
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
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "VAR1=test",
                        token => ValidateIdentifier<LabelKeyToken>(token, "VAR1"),
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
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "VAR1=test\\ 123",
                        token => ValidateIdentifier<LabelKeyToken>(token, "VAR1"),
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
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "VAR1=test",
                        token => ValidateIdentifier<LabelKeyToken>(token, "VAR1"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "test")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "VAR2=\"testing 1 2 3\"",
                        token => ValidateIdentifier<LabelKeyToken>(token, "VAR2"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "testing 1 2 3", '\"'))
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<LabelInstruction>
    {
        public Dictionary<string, string> Variables { get; set; }
    }

    [Fact]
    public void LabelInstruction_UnicodeValue_RoundTrips()
    {
        string text = "LABEL description=\"Hello 世界 🚀\"\n";
        LabelInstruction inst = LabelInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

    [Fact]
    public void LabelInstruction_KeyWithDots_RoundTrips()
    {
        string text = "LABEL com.example.app.version=1.0\n";
        LabelInstruction inst = LabelInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

    [Fact]
    public void LabelInstruction_KeyWithHyphens_RoundTrips()
    {
        string text = "LABEL my-label-key=value\n";
        LabelInstruction inst = LabelInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

    [Fact]
    public void LabelInstruction_EmptyValue_RoundTrips()
    {
        string text = "LABEL mykey=\n";
        LabelInstruction inst = LabelInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
        Assert.Equal("", inst.Labels[0].Value);
    }
}
