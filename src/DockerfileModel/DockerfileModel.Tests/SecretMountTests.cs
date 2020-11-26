using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class SecretMountTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(SecretMountParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                SecretMount result = SecretMount.Parse(scenario.Text, scenario.EscapeChar);
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
            SecretMount result = new SecretMount(scenario.Id, scenario.DestinationPath);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Id()
        {
            SecretMount secretMount = new SecretMount("test");
            Assert.Equal("test", secretMount.Id);
            Assert.Equal("test", secretMount.IdToken.Value);

            secretMount.Id = "test2";
            Assert.Equal("test2", secretMount.Id);
            Assert.Equal("test2", secretMount.IdToken.Value);

            secretMount.IdToken.ValueToken.Value = "test3";
            Assert.Equal("test3", secretMount.Id);
            Assert.Equal("test3", secretMount.IdToken.Value);

            secretMount.IdToken = new KeyValueToken<KeywordToken, LiteralToken>(
                new KeywordToken("id"), new LiteralToken("test4"));
            Assert.Equal("test4", secretMount.Id);
            Assert.Equal("test4", secretMount.IdToken.Value);

            Assert.Throws<ArgumentNullException>(() => secretMount.Id = null);
            Assert.Throws<ArgumentNullException>(() => secretMount.IdToken = null);
        }

        [Fact]
        public void IdWithVariables()
        {
            SecretMount secretMount = new SecretMount("$var");
            TestHelper.TestVariablesWithLiteral(() => secretMount.IdToken.ValueToken, "var", canContainVariables: true);
        }

        [Fact]
        public void DestinationPath()
        {
            SecretMount secretMount = new SecretMount("foo", "test");
            Assert.Equal("test", secretMount.DestinationPath);
            Assert.Equal("test", secretMount.DestinationPathToken.Value);

            secretMount.DestinationPath = "test2";
            Assert.Equal("test2", secretMount.DestinationPath);
            Assert.Equal("test2", secretMount.DestinationPathToken.Value);

            secretMount.DestinationPathToken.ValueToken.Value = "test3";
            Assert.Equal("test3", secretMount.DestinationPath);
            Assert.Equal("test3", secretMount.DestinationPathToken.Value);

            secretMount.DestinationPathToken = new KeyValueToken<KeywordToken, LiteralToken>(
                new KeywordToken("dst"), new LiteralToken("test4"));
            Assert.Equal("test4", secretMount.DestinationPath);
            Assert.Equal("test4", secretMount.DestinationPathToken.Value);
            Assert.Equal("type=secret,id=foo,dst=test4", secretMount.ToString());

            secretMount.DestinationPath = null;
            Assert.Equal("type=secret,id=foo", secretMount.ToString());
            Assert.Null(secretMount.DestinationPath);
            Assert.Null(secretMount.DestinationPathToken);

            secretMount.DestinationPath = "test5";

            secretMount.DestinationPathToken = null;
            Assert.Equal("type=secret,id=foo", secretMount.ToString());
            Assert.Null(secretMount.DestinationPath);
            Assert.Null(secretMount.DestinationPathToken);
        }

        [Fact]
        public void DestinationWithVariables()
        {
            SecretMount secretMount = new SecretMount("id", "$var");
            TestHelper.TestVariablesWithLiteral(
                () => secretMount.DestinationPathToken.ValueToken, "var", canContainVariables: true);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            SecretMountParseTestScenario[] testInputs = new SecretMountParseTestScenario[]
            {
                new SecretMountParseTestScenario
                {
                    Text = "type=secret,id=foo",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyValue(token, "type", "secret"),
                        token => ValidateSymbol(token, ','),
                        token => ValidateKeyValue(token, "id", "foo"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("secret", result.Type);
                        Assert.Equal("foo", result.Id);
                        Assert.Null(result.DestinationPath);
                    }
                },
                new SecretMountParseTestScenario
                {
                    Text = "type=secret,id=foo,dst=test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyValue(token, "type", "secret"),
                        token => ValidateSymbol(token, ','),
                        token => ValidateKeyValue(token, "id", "foo"),
                        token => ValidateSymbol(token, ','),
                        token => ValidateKeyValue(token, "dst", "test"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("secret", result.Type);
                        Assert.Equal("foo", result.Id);
                        Assert.Equal("test", result.DestinationPath);
                    }
                },
                new SecretMountParseTestScenario
                {
                    EscapeChar = '`',
                    Text = "typ`\ne`\n=`\nsecret`\n,`\nid=foo",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<KeyValueToken<KeywordToken, LiteralToken>>(token, "typ`\ne`\n=`\nsecret",
                            token => ValidateAggregate<KeywordToken>(token, "typ`\ne",
                                token => ValidateString(token, "typ"),
                                token => ValidateLineContinuation(token, '`', "\n"),
                                token => ValidateString(token, "e")),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateLiteral(token, "secret")),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateSymbol(token, ','),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateKeyValue(token, "id", "foo")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("secret", result.Type);
                        Assert.Equal("foo", result.Id);
                        Assert.Null(result.DestinationPath);
                    }
                },
                new SecretMountParseTestScenario
                {
                    Text = "type=secret,id=$secretid",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyValue(token, "type", "secret"),
                        token => ValidateSymbol(token, ','),
                        token => ValidateAggregate<KeyValueToken<KeywordToken, LiteralToken>>(token, "id=$secretid",
                            token => ValidateKeyword(token, "id"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<LiteralToken>(token, "$secretid",
                                token => ValidateAggregate<VariableRefToken>(token, "$secretid",
                                    token => ValidateString(token, "secretid"))))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("secret", result.Type);
                        Assert.Equal("$secretid", result.Id);
                        Assert.Null(result.DestinationPath);
                    }
                },
                new SecretMountParseTestScenario
                {
                    Text = "type=foo",
                    ParseExceptionPosition = new Position(1, 1, 1)
                },
                new SecretMountParseTestScenario
                {
                    Text = "type=secret",
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
                    Id = "foo",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyValue(token, "type", "secret"),
                        token => ValidateSymbol(token, ','),
                        token => ValidateKeyValue(token, "id", "foo"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("secret", result.Type);
                        Assert.Equal("foo", result.Id);
                        Assert.Null(result.DestinationPath);
                    }
                },
                new CreateTestScenario
                {
                    Id = "foo",
                    DestinationPath = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyValue(token, "type", "secret"),
                        token => ValidateSymbol(token, ','),
                        token => ValidateKeyValue(token, "id", "foo"),
                        token => ValidateSymbol(token, ','),
                        token => ValidateKeyValue(token, "dst", "test"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("secret", result.Type);
                        Assert.Equal("foo", result.Id);
                        Assert.Equal("test", result.DestinationPath);
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class SecretMountParseTestScenario : ParseTestScenario<SecretMount>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<SecretMount>
        {
            public string Id { get; set; }
            public string DestinationPath { get; set; }
        }
    }
}
