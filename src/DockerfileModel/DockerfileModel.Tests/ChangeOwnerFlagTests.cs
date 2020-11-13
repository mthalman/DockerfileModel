using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class ChangeOwnerFlagTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ChangeOwnerFlagParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                ChangeOwnerFlag result = ChangeOwnerFlag.Parse(scenario.Text, scenario.EscapeChar);
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
            ChangeOwnerFlag result = ChangeOwnerFlag.Create(scenario.User, scenario.Group);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void User()
        {
            ChangeOwnerFlag flag = ChangeOwnerFlag.Create("foo");
            Assert.Equal("foo", flag.User);
            Assert.Equal("--chown=foo", flag.ToString());

            flag.User = "test";
            Assert.Equal("test", flag.User);
            Assert.Equal("--chown=test", flag.ToString());

            Assert.Throws<ArgumentException>(() => flag.User = "");
            Assert.Throws<ArgumentNullException>(() => flag.User = null);
        }

        [Fact]
        public void Group()
        {
            ChangeOwnerFlag flag = ChangeOwnerFlag.Create("user", "foo");
            Assert.Equal("foo", flag.Group);
            Assert.Equal("--chown=user:foo", flag.ToString());

            flag.Group = "test";
            Assert.Equal("test", flag.Group);
            Assert.Equal("--chown=user:test", flag.ToString());

            flag.Group = null;
            Assert.Null(flag.Group);
            Assert.Equal("--chown=user", flag.ToString());
        }

        [Fact]
        public void ChangeOwnerToken()
        {
            ChangeOwnerFlag flag = ChangeOwnerFlag.Create("user");
            Assert.Equal("--chown=user", flag.ToString());

            flag.ChangeOwnerToken.ValueToken.Group = "foo";
            Assert.Equal("foo", flag.Group);
            Assert.Equal("--chown=user:foo", flag.ToString());

            flag.ChangeOwnerToken = KeyValueToken<ChangeOwner>.Create("chown", ChangeOwner.Create("test1:test2"));
            Assert.Equal("test2", flag.Group);
            Assert.Equal("--chown=test1:test2", flag.ToString());
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            ChangeOwnerFlagParseTestScenario[] testInputs = new ChangeOwnerFlagParseTestScenario[]
            {
                new ChangeOwnerFlagParseTestScenario
                {
                    Text = "--chown=foo:bar",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<KeyValueToken<ChangeOwner>>(token, "chown=foo:bar",
                            token => ValidateKeyword(token, "chown"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<ChangeOwner>(token, "foo:bar",
                                token => ValidateLiteral(token, "foo"),
                                token => ValidateSymbol(token, ':'),
                                token => ValidateLiteral(token, "bar")))
                    }
                },
                new ChangeOwnerFlagParseTestScenario
                {
                    Text = "--chown=foo",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<KeyValueToken<ChangeOwner>>(token, "chown=foo",
                            token => ValidateKeyword(token, "chown"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<ChangeOwner>(token, "foo",
                                token => ValidateLiteral(token, "foo")))
                    }
                },
                new ChangeOwnerFlagParseTestScenario
                {
                    Text = "--chown`\n=`\nfoo",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<KeyValueToken<ChangeOwner>>(token, "chown`\n=`\nfoo",
                            token => ValidateKeyword(token, "chown"),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateAggregate<ChangeOwner>(token, "foo",
                                token => ValidateLiteral(token, "foo")))
                    }
                },
                new ChangeOwnerFlagParseTestScenario
                {
                    Text = "changeOwner=foo",
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
                    User = "user",
                    Group = "group",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<KeyValueToken<ChangeOwner>>(token, "chown=user:group",
                            token => ValidateKeyword(token, "chown"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<ChangeOwner>(token, "user:group",
                                token => ValidateLiteral(token, "user"),
                                token => ValidateSymbol(token, ':'),
                                token => ValidateLiteral(token, "group")))
                    }
                },
                new CreateTestScenario
                {
                    User = "user",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<KeyValueToken<ChangeOwner>>(token, "chown=user",
                            token => ValidateKeyword(token, "chown"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<ChangeOwner>(token, "user",
                                token => ValidateLiteral(token, "user")))
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class ChangeOwnerFlagParseTestScenario : ParseTestScenario<ChangeOwnerFlag>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<ChangeOwnerFlag>
        {
            public string User { get; set; }
            public string Group { get; set; }
        }
    }
}
