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
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, ""))
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
                ParseExceptionPosition = new Position(9, 1, 10)
            },
            new ParseTestScenario<LabelInstruction>
            {
                Text = "LABEL $var=1",
                ParseExceptionPosition = new Position(6, 1, 7)
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
}
