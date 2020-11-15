using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class PlatformFlagTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(PlatformFlagParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                PlatformFlag result = PlatformFlag.Parse(scenario.Text, scenario.EscapeChar);
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
            PlatformFlag result = PlatformFlag.Create(scenario.Platform);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Platform()
        {
            PlatformFlag platformFlag = PlatformFlag.Create("test");
            Assert.Equal("test", platformFlag.Platform);
            Assert.Equal("test", platformFlag.PlatformToken.Value);

            platformFlag.Platform = "test2";
            Assert.Equal("test2", platformFlag.Platform);
            Assert.Equal("test2", platformFlag.PlatformToken.Value);

            platformFlag.PlatformToken.ValueToken.Value = "test3";
            Assert.Equal("test3", platformFlag.Platform);
            Assert.Equal("test3", platformFlag.PlatformToken.Value);

            platformFlag.PlatformToken = KeyValueToken<KeywordToken, LiteralToken>.Create(
                new KeywordToken("platform"), new LiteralToken("test4"));
            Assert.Equal("test4", platformFlag.Platform);
            Assert.Equal("test4", platformFlag.PlatformToken.Value);

            Assert.Throws<ArgumentNullException>(() => platformFlag.Platform = null);
            Assert.Throws<ArgumentNullException>(() => platformFlag.PlatformToken = null);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            PlatformFlagParseTestScenario[] testInputs = new PlatformFlagParseTestScenario[]
            {
                new PlatformFlagParseTestScenario
                {
                    Text = "--platform=windows/amd64",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyValue(token, "platform", "windows/amd64")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("windows/amd64", result.Platform);
                    }
                },
                new PlatformFlagParseTestScenario
                {
                    Text = "-`\n-plat`\nform=win`\ndows/amd64",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateAggregate<KeyValueToken<KeywordToken, LiteralToken>>(token, $"plat`\nform=win`\ndows/amd64",
                            token => ValidateAggregate<KeywordToken>(token, "plat`\nform",
                                token => ValidateString(token, "plat"),
                                token => ValidateLineContinuation(token, '`', "\n"),
                                token => ValidateString(token, "form")),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<LiteralToken>(token, "win`\ndows/amd64",
                                token => ValidateString(token, "win"),
                                token => ValidateLineContinuation(token, '`', "\n"),
                                token => ValidateString(token, "dows/amd64")))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("windows/amd64", result.Platform);
                    }
                },
                new PlatformFlagParseTestScenario
                {
                    Text = "--platform=",
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
                    Platform = "linux/amd64",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateKeyValue(token, "platform", "linux/amd64")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("linux/amd64", result.Platform);
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class PlatformFlagParseTestScenario : ParseTestScenario<PlatformFlag>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<PlatformFlag>
        {
            public string Platform { get; set; }
        }
    }
}
