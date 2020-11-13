using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
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
            ExposeInstruction result = ExposeInstruction.Create(scenario.Port, scenario.Protocol);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
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
                        Assert.Equal(80, result.Port);
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
                        Assert.Equal(433, result.Port);
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
                    Port = 8080,
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "EXPOSE"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "8080")
                    }
                },
                new CreateTestScenario
                {
                    Port = 80,
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
            public int Port { get; set; }
            public string Protocol { get; set; }
        }
    }
}
