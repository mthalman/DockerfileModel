﻿using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class EnvInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(EnvInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                EnvInstruction result = EnvInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => EnvInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            EnvInstruction result = EnvInstruction.Create(scenario.Variables);

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

                Assert.Collection(instruction.VariableTokens, new Action<KeyValueToken<IdentifierToken, LiteralToken>>[]
                {
                    token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, $"{expectedKey}={expectedValue}",
                        token => ValidateIdentifier(token, expectedKey),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, expectedValue))
                });
            }

            EnvInstruction result = EnvInstruction.Create(
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

        public static IEnumerable<object[]> ParseTestInput()
        {
            EnvInstructionParseTestScenario[] testInputs = new EnvInstructionParseTestScenario[]
            {
                new EnvInstructionParseTestScenario
                {
                    Text = "ENV MY_NAME=",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ENV"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "MY_NAME=",
                            token => ValidateIdentifier(token, "MY_NAME"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, ""))
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
                new EnvInstructionParseTestScenario
                {
                    Text = "ENV MY_NAME=\"\"",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ENV"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "MY_NAME=\"\"",
                            token => ValidateIdentifier(token, "MY_NAME"),
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
                new EnvInstructionParseTestScenario
                {
                    Text = "ENV MY_NAME=John",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ENV"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "MY_NAME=John",
                            token => ValidateIdentifier(token, "MY_NAME"),
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
                new EnvInstructionParseTestScenario
                {
                    Text = "ENV MY_NAME=\"John Doe\"",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ENV"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "MY_NAME=\"John Doe\"",
                            token => ValidateIdentifier(token, "MY_NAME"),
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
                new EnvInstructionParseTestScenario
                {
                    Text = "ENV MY_NAME=John` Doe",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ENV"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "MY_NAME=John` Doe",
                            token => ValidateIdentifier(token, "MY_NAME"),
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
                new EnvInstructionParseTestScenario
                {
                    Text = "ENV MY_NAME=\"John Doe\" MY_DOG=Rex` The` Dog ` \n MY_CAT=fluffy",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ENV"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "MY_NAME=\"John Doe\"",
                            token => ValidateIdentifier(token, "MY_NAME"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "John Doe", '\"')),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "MY_DOG=Rex` The` Dog",
                            token => ValidateIdentifier(token, "MY_DOG"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "Rex` The` Dog")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "` \n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "MY_CAT=fluffy",
                            token => ValidateIdentifier(token, "MY_CAT"),
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
                new EnvInstructionParseTestScenario
                {
                    Text = "ENV MY_NAME John",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ENV"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "MY_NAME John",
                            token => ValidateIdentifier(token, "MY_NAME"),
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
                new EnvInstructionParseTestScenario
                {
                    Text = "ENV MY_`\nNAME `\nJo`\nhn",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ENV"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "MY_`\nNAME `\nJo`\nhn",
                            token => ValidateAggregate<IdentifierToken>(token, "MY_`\nNAME",
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
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "VAR1=test",
                            token => ValidateIdentifier(token, "VAR1"),
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
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "VAR1=test\\ 123",
                            token => ValidateIdentifier(token, "VAR1"),
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
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "VAR1=test",
                            token => ValidateIdentifier(token, "VAR1"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "test")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<KeyValueToken<IdentifierToken, LiteralToken>>(token, "VAR2=\"testing 1 2 3\"",
                            token => ValidateIdentifier(token, "VAR2"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "testing 1 2 3", '\"'))
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class EnvInstructionParseTestScenario : ParseTestScenario<EnvInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<EnvInstruction>
        {
            public Dictionary<string, string> Variables { get; set; }
        }
    }
}
