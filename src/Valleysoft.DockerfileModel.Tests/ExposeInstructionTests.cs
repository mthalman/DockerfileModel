using System;
using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests
{
    public class ExposeInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ExposeInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                ExposeInstruction result = ExposeInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => ExposeInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            ExposeInstruction result = new ExposeInstruction(scenario.Port, scenario.Protocol);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Port()
        {
            ExposeInstruction result = new ExposeInstruction("23", "protocol");
            Assert.Equal("23", result.Port);
            Assert.Equal("23", result.PortToken.Value);
            Assert.Equal("EXPOSE 23/protocol", result.ToString());

            result.Port = "45";
            Assert.Equal("45", result.Port);
            Assert.Equal("45", result.PortToken.Value);
            Assert.Equal("EXPOSE 45/protocol", result.ToString());

            result.PortToken.Value = "67";
            Assert.Equal("67", result.Port);
            Assert.Equal("67", result.PortToken.Value);
            Assert.Equal("EXPOSE 67/protocol", result.ToString());

            result.PortToken = new LiteralToken("78");
            Assert.Equal("78", result.Port);
            Assert.Equal("78", result.PortToken.Value);
            Assert.Equal("EXPOSE 78/protocol", result.ToString());

            Assert.Throws<ArgumentNullException>(() => result.Port = null);
            Assert.Throws<ArgumentException>(() => result.Port = "");
            Assert.Throws<ArgumentNullException>(() => result.PortToken = null);
        }

        [Fact]
        public void Protocol()
        {
            ExposeInstruction result = new ExposeInstruction("23", "test");
            Assert.Equal("test", result.Protocol);
            Assert.Equal("test", result.ProtocolToken.Value);
            Assert.Equal("EXPOSE 23/test", result.ToString());

            result.Protocol = "test2";
            Assert.Equal("test2", result.Protocol);
            Assert.Equal("test2", result.ProtocolToken.Value);
            Assert.Equal("EXPOSE 23/test2", result.ToString());

            result.Protocol = null;
            Assert.Null(result.Protocol);
            Assert.Null(result.ProtocolToken);
            Assert.Equal("EXPOSE 23", result.ToString());

            result.ProtocolToken = new LiteralToken("test3");
            Assert.Equal("test3", result.Protocol);
            Assert.Equal("test3", result.ProtocolToken.Value);
            Assert.Equal("EXPOSE 23/test3", result.ToString());

            result.ProtocolToken.Value = "test4";
            Assert.Equal("test4", result.Protocol);
            Assert.Equal("test4", result.ProtocolToken.Value);
            Assert.Equal("EXPOSE 23/test4", result.ToString());

            result.ProtocolToken = null;
            Assert.Null(result.Protocol);
            Assert.Null(result.ProtocolToken);
            Assert.Equal("EXPOSE 23", result.ToString());
        }

        [Fact]
        public void PortWithVariables()
        {
            ExposeInstruction result = new ExposeInstruction("$var", "test");
            TestHelper.TestVariablesWithLiteral(() => result.PortToken, "var", canContainVariables: true);
        }

        [Fact]
        public void ProtocolWithVariables()
        {
            ExposeInstruction result = new ExposeInstruction("23", "$var");
            TestHelper.TestVariablesWithNullableLiteral(
                () => result.ProtocolToken, token => result.ProtocolToken = token, val => result.Protocol = val, "var", canContainVariables: true);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            ExposeInstructionParseTestScenario[] testInputs = new ExposeInstructionParseTestScenario[]
            {
                new ExposeInstructionParseTestScenario
                {
                    Text = "EXPOSE 80",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "EXPOSE"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "80")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("EXPOSE", result.InstructionName);
                        Assert.Equal("80", result.Port);
                    }
                },
                new ExposeInstructionParseTestScenario
                {
                    Text = "EXPOSE 433/tcp",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "EXPOSE"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "433"),
                        token => ValidateSymbol(token, '/'),
                        token => ValidateLiteral(token, "tcp")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("EXPOSE", result.InstructionName);
                        Assert.Equal("433", result.Port);
                        Assert.Equal("tcp", result.Protocol);
                    }
                },
                new ExposeInstructionParseTestScenario
                {
                    Text = "EXPOSE $TEST",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "EXPOSE"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LiteralToken>(token, "$TEST",
                            token => ValidateAggregate<VariableRefToken>(token, "$TEST",
                                token => ValidateString(token, "TEST")))
                    }
                },
                new ExposeInstructionParseTestScenario
                {
                    Text = "EXPOSE`\n 80`\n/`\ntcp",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "EXPOSE"),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "80"),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateSymbol(token, '/'),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateLiteral(token, "tcp")
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
                    Port = "8080",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "EXPOSE"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "8080")
                    }
                },
                new CreateTestScenario
                {
                    Port = "80",
                    Protocol = "udp",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "EXPOSE"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "80"),
                        token => ValidateSymbol(token, '/'),
                        token => ValidateLiteral(token, "udp")
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class ExposeInstructionParseTestScenario : ParseTestScenario<ExposeInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<ExposeInstruction>
        {
            public string Port { get; set; }
            public string Protocol { get; set; }
        }
    }
}
