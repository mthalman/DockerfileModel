﻿using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class KeyValueTokenTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(KeyValueTokenParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                KeyValueToken<LiteralToken> result = KeyValueToken<LiteralToken>.Parse(
                    scenario.Text, scenario.EscapeChar, scenario.Key);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => ArgInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            KeyValueToken<LiteralToken> result = KeyValueToken<LiteralToken>.Create(scenario.Key, scenario.Value);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Key()
        {
            KeyValueToken<LiteralToken> token = KeyValueToken<LiteralToken>.Create("foo", "test");
            Assert.Equal("foo", token.Key);
            Assert.Equal("foo", token.KeyToken.Value);

            token.Key = "foo2";
            Assert.Equal("foo2", token.Key);
            Assert.Equal("foo2", token.KeyToken.Value);

            token.KeyToken.Value = "foo3";
            Assert.Equal("foo3", token.Key);
            Assert.Equal("foo3", token.KeyToken.Value);

            token.KeyToken = new KeywordToken("foo4");
            Assert.Equal("foo4", token.Key);
            Assert.Equal("foo4", token.KeyToken.Value);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            KeyValueTokenParseTestScenario[] testInputs = new KeyValueTokenParseTestScenario[]
            {
                new KeyValueTokenParseTestScenario
                {
                    Key = "key",
                    Text = "key=val",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "key"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "val")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("key", result.Key);
                        Assert.Equal("val", result.Value);
                    }
                },
                new KeyValueTokenParseTestScenario
                {
                    Key = "key",
                    Text = "k`\ney=va`\nl",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<KeywordToken>(token, "k`\ney",
                            token => ValidateString(token, "k"),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateString(token, "ey")),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<LiteralToken>(token, "va`\nl",
                            token => ValidateString(token, "va"),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateString(token, "l"))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("key", result.Key);
                        Assert.Equal("val", result.Value);
                    }
                },
                new KeyValueTokenParseTestScenario
                {
                    Key = "key",
                    Text = "key=$val",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "key"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<LiteralToken>(token, "$val",
                            token => ValidateAggregate<VariableRefToken>(token, "$val",
                                token => ValidateString(token, "val")))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("key", result.Key);
                        Assert.Equal("$val", result.Value);
                    }
                },
                new KeyValueTokenParseTestScenario
                {
                    Text = "=val",
                    ParseExceptionPosition = new Position(1, 1, 1)
                },
                new KeyValueTokenParseTestScenario
                {
                    Text = "key=",
                    ParseExceptionPosition = new Position(1, 1, 1)
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
                    Key = "foo",
                    Value = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "foo"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, "test")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("foo", result.Key);
                        Assert.Equal("test", result.Value);
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class KeyValueTokenParseTestScenario : ParseTestScenario<KeyValueToken<LiteralToken>>
        {
            public char EscapeChar { get; set; }
            public string Key { get; set; }
        }

        public class CreateTestScenario : TestScenario<KeyValueToken<LiteralToken>>
        {
            public string Key { get; set; }
            public string Value { get; set; }
        }
    }
}