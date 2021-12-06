using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class LabelInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(LabelInstructionParseTestScenario scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            LabelInstruction result = LabelInstruction.Parse(scenario.Text, scenario.EscapeChar);
            Assert.Equal(scenario.Text, result.ToString());
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }
        else
        {
            ParseException exception = Assert.Throws<ParseException>(
                () => LabelInstruction.Parse(scenario.Text, scenario.EscapeChar));
            Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
            Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
        }
    }

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

            Assert.Collection(instruction.LabelTokens, new Action<KeyValueToken<LiteralToken, LiteralToken>>[]
            {
                token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, $"{expectedKey}={expectedValue}",
                    token => ValidateLiteral(token, expectedKey),
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
        LabelInstructionParseTestScenario[] testInputs = new LabelInstructionParseTestScenario[]
        {
            new LabelInstructionParseTestScenario
            {
                Text = "LABEL MY_NAME=",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "MY_NAME=",
                        token => ValidateLiteral(token, "MY_NAME"),
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
            new LabelInstructionParseTestScenario
            {
                Text = "LABEL MY_NAME=\"\"",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "MY_NAME=\"\"",
                        token => ValidateLiteral(token, "MY_NAME"),
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
            new LabelInstructionParseTestScenario
            {
                Text = "LABEL MY_NAME=John",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "MY_NAME=John",
                        token => ValidateLiteral(token, "MY_NAME"),
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
            new LabelInstructionParseTestScenario
            {
                Text = "LABEL $var1-$var2=$var3",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "$var1-$var2=$var3",
                        token => ValidateQuotableAggregate<LiteralToken>(token, "$var1-$var2", null,
                            token => ValidateAggregate<VariableRefToken>(token, "$var1",
                                token => ValidateString(token, "var1")),
                            token => ValidateString(token, "-"),
                            token => ValidateAggregate<VariableRefToken>(token, "$var2",
                                token => ValidateString(token, "var2"))),
                        token => ValidateSymbol(token, '='),
                        token => ValidateQuotableAggregate<LiteralToken>(token, "$var3", null,
                            token => ValidateAggregate<VariableRefToken>(token, "$var3",
                                token => ValidateString(token, "var3"))))
                }
            },
            new LabelInstructionParseTestScenario
            {
                Text = "LABEL MY_NAME=\"John Doe\"",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "MY_NAME=\"John Doe\"",
                        token => ValidateLiteral(token, "MY_NAME"),
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
            new LabelInstructionParseTestScenario
            {
                Text = "LABEL MY_NAME=\"John `\nDoe\"",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "MY_NAME=\"John `\nDoe\"",
                        token => ValidateLiteral(token, "MY_NAME"),
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
            new LabelInstructionParseTestScenario
            {
                Text = "LABEL \"MY_NAME\"=John",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "\"MY_NAME\"=John",
                        token => ValidateLiteral(token, "MY_NAME", '\"'),
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
            new LabelInstructionParseTestScenario
            {
                Text = "LABEL MY_NAME=\"John Doe\" MY_DOG=Rex` The` Dog ` \n MY_CAT=fluffy",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "LABEL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "MY_NAME=\"John Doe\"",
                        token => ValidateLiteral(token, "MY_NAME"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "John Doe", '\"')),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "MY_DOG=Rex` The` Dog",
                        token => ValidateLiteral(token, "MY_DOG"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "Rex` The` Dog")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LineContinuationToken>(token, "` \n",
                        token => ValidateSymbol(token, '`'),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateNewLine(token, "\n")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "MY_CAT=fluffy",
                        token => ValidateLiteral(token, "MY_CAT"),
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
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "VAR1=test",
                        token => ValidateLiteral(token, "VAR1"),
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
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "VAR1=test\\ 123",
                        token => ValidateLiteral(token, "VAR1"),
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
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "VAR1=test",
                        token => ValidateLiteral(token, "VAR1"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "test")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<KeyValueToken<LiteralToken, LiteralToken>>(token, "VAR2=\"testing 1 2 3\"",
                        token => ValidateLiteral(token, "VAR2"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "testing 1 2 3", '\"'))
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class LabelInstructionParseTestScenario : ParseTestScenario<LabelInstruction>
    {
        public char EscapeChar { get; set; }
    }

    public class CreateTestScenario : TestScenario<LabelInstruction>
    {
        public Dictionary<string, string> Variables { get; set; }
    }
}
