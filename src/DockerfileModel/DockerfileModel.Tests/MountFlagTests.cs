using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class MountFlagTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(MountFlagParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                MountFlag result = MountFlag.Parse(scenario.Text, scenario.EscapeChar);
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
            MountFlag result = MountFlag.Create(scenario.Mount);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Mount()
        {
            SecretMount mount = SecretMount.Create("foo");
            MountFlag flag = MountFlag.Create(mount);

            Assert.Equal("--mount=type=secret,id=foo", flag.ToString());
            Assert.Equal(mount.ToString(), flag.Mount.ToString());

            mount = SecretMount.Create("test");
            flag.Mount = mount;

            Assert.Equal("--mount=type=secret,id=test", flag.ToString());
            Assert.Equal(mount.ToString(), flag.Mount.ToString());
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            MountFlagParseTestScenario[] testInputs = new MountFlagParseTestScenario[]
            {
                new MountFlagParseTestScenario
                {
                    Text = "--mount=type=secret,id=foo",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<KeyValueToken<KeywordToken, Mount>>(token, "mount=type=secret,id=foo",
                            token => ValidateKeyword(token, "mount"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<SecretMount>(token, "type=secret,id=foo",
                                token => ValidateKeyValue(token, "type", "secret"),
                                token => ValidateSymbol(token, ','),
                                token => ValidateKeyValue(token, "id", "foo")))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("type=secret,id=foo", result.Mount.ToString());
                    }
                },
                new MountFlagParseTestScenario
                {
                    Text = "--mount=type=secret,id=foo,dst=test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<KeyValueToken<KeywordToken, Mount>>(token, "mount=type=secret,id=foo,dst=test",
                            token => ValidateKeyword(token, "mount"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<SecretMount>(token, "type=secret,id=foo,dst=test",
                                token => ValidateKeyValue(token, "type", "secret"),
                                token => ValidateSymbol(token, ','),
                                token => ValidateKeyValue(token, "id", "foo"),
                                token => ValidateSymbol(token, ','),
                                token => ValidateKeyValue(token, "dst", "test")))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("type=secret,id=foo,dst=test", result.Mount.ToString());
                    }
                },
                new MountFlagParseTestScenario
                {
                    Text = "-`\n-`\nmount=type=secret,id=foo",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateAggregate<KeyValueToken<KeywordToken, Mount>>(token, "mount=type=secret,id=foo",
                            token => ValidateKeyword(token, "mount"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<SecretMount>(token, "type=secret,id=foo",
                                token => ValidateKeyValue(token, "type", "secret"),
                                token => ValidateSymbol(token, ','),
                                token => ValidateKeyValue(token, "id", "foo")))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("type=secret,id=foo", result.Mount.ToString());
                    }
                },
                new MountFlagParseTestScenario
                {
                    Text = "mount=foo",
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
                    Mount = SecretMount.Create("foo"),
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<KeyValueToken<KeywordToken, Mount>>(token, "mount=type=secret,id=foo",
                            token => ValidateKeyword(token, "mount"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<SecretMount>(token, "type=secret,id=foo",
                                token => ValidateKeyValue(token, "type", "secret"),
                                token => ValidateSymbol(token, ','),
                                token => ValidateKeyValue(token, "id", "foo")))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("type=secret,id=foo", result.Mount.ToString());
                    }
                },
                new CreateTestScenario
                {
                    Mount = SecretMount.Create("foo", "test"),
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<KeyValueToken<KeywordToken, Mount>>(token, "mount=type=secret,id=foo,dst=test",
                            token => ValidateKeyword(token, "mount"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<SecretMount>(token, "type=secret,id=foo,dst=test",
                                token => ValidateKeyValue(token, "type", "secret"),
                                token => ValidateSymbol(token, ','),
                                token => ValidateKeyValue(token, "id", "foo"),
                                token => ValidateSymbol(token, ','),
                                token => ValidateKeyValue(token, "dst", "test")))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("type=secret,id=foo,dst=test", result.Mount.ToString());
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class MountFlagParseTestScenario : ParseTestScenario<MountFlag>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<MountFlag>
        {
            public Mount Mount { get; set; }
        }
    }
}
