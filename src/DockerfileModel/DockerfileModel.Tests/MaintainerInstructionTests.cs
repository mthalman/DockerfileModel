using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class MaintainerInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(MaintainerInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                MaintainerInstruction result = MaintainerInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => MaintainerInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            MaintainerInstruction result = new MaintainerInstruction(scenario.Maintainer);

            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void MaintainerWithVariables()
        {
            MaintainerInstruction result = new MaintainerInstruction("$test");
            TestHelper.TestVariablesWithLiteral(() => result.MaintainerToken, "test", canContainVariables: true);
        }

        [Fact]
        public void Maintainer()
        {
            MaintainerInstruction result = new MaintainerInstruction("test");
            Assert.Equal("test", result.Maintainer);
            Assert.Equal("test", result.MaintainerToken.Value);
            Assert.Equal("MAINTAINER test", result.ToString());

            result.Maintainer = "test2";
            Assert.Equal("test2", result.Maintainer);
            Assert.Equal("test2", result.MaintainerToken.Value);
            Assert.Equal("MAINTAINER test2", result.ToString());

            result.MaintainerToken.Value = "test3";
            Assert.Equal("test3", result.Maintainer);
            Assert.Equal("test3", result.MaintainerToken.Value);
            Assert.Equal("MAINTAINER test3", result.ToString());

            result.Maintainer = "";
            result.MaintainerToken.QuoteChar = '\"';
            Assert.Equal("", result.Maintainer);
            Assert.Equal("", result.MaintainerToken.Value);
            Assert.Equal("MAINTAINER \"\"", result.ToString());
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            MaintainerInstructionParseTestScenario[] testInputs = new MaintainerInstructionParseTestScenario[]
            {
                new MaintainerInstructionParseTestScenario
                {
                    Text = "MAINTAINER name",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "MAINTAINER"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "name")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("MAINTAINER", result.InstructionName);
                        Assert.Equal("name", result.Maintainer);
                    }
                },
                new MaintainerInstructionParseTestScenario
                {
                    Text = "MAINTAINER \"name\"",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "MAINTAINER"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "name", '\"')
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("MAINTAINER", result.InstructionName);
                        Assert.Equal("name", result.Maintainer);
                    }
                },
                new MaintainerInstructionParseTestScenario
                {
                    Text = "MAINTAINER \"\"",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "MAINTAINER"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "", '\"')
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("MAINTAINER", result.InstructionName);
                        Assert.Equal("", result.Maintainer);
                    }
                },
                new MaintainerInstructionParseTestScenario
                {
                    Text = "MAINTAINER $var",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "MAINTAINER"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateQuotableAggregate<LiteralToken>(token, "$var", null,
                            token => ValidateAggregate<VariableRefToken>(token, "$var",
                                token => ValidateString(token, "var")))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("MAINTAINER", result.InstructionName);
                        Assert.Equal("$var", result.Maintainer);
                    }
                },
                new MaintainerInstructionParseTestScenario
                {
                    Text = "MAINTAINER `\n name",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "MAINTAINER"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "name")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("MAINTAINER", result.InstructionName);
                        Assert.Equal("name", result.Maintainer);
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
                    Maintainer = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "MAINTAINER"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "test")
                    }
                },
                new CreateTestScenario
                {
                    Maintainer = "",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "MAINTAINER"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "", '\"')
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class MaintainerInstructionParseTestScenario : ParseTestScenario<MaintainerInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<MaintainerInstruction>
        {
            public string Maintainer { get; set; }
        }
    }
}
