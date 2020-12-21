using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class HealthCheckInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(HealthCheckInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                HealthCheckInstruction result = HealthCheckInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => HealthCheckInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            HealthCheckInstruction result;
            
            if (scenario.Command is not null)
            {
                if (scenario.Args is not null)
                {
                    result = new HealthCheckInstruction(scenario.Command, scenario.Args, scenario.Interval, scenario.Timeout, scenario.StartPeriod, scenario.Retries);
                }
                else
                {
                    result = new HealthCheckInstruction(scenario.Command, scenario.Interval, scenario.Timeout, scenario.StartPeriod, scenario.Retries);
                }
            }
            else
            {
                result = new HealthCheckInstruction();
            }

            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Interval()
        {
            HealthCheckInstruction instruction = new HealthCheckInstruction("command", interval: "10s");
            Assert.Equal("10s", instruction.Interval);
            Assert.Equal("10s", instruction.IntervalToken.Value);
            Assert.Equal("HEALTHCHECK --interval=10s CMD command", instruction.ToString());

            instruction.Interval = "20s";
            Assert.Equal("20s", instruction.Interval);
            Assert.Equal("20s", instruction.IntervalToken.Value);
            Assert.Equal("HEALTHCHECK --interval=20s CMD command", instruction.ToString());

            instruction.Interval = null;
            Assert.Null(instruction.Interval);
            Assert.Null(instruction.IntervalToken);
            Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());

            instruction.IntervalToken = new LiteralToken("40s");
            Assert.Equal("40s", instruction.Interval);
            Assert.Equal("40s", instruction.IntervalToken.Value);
            Assert.Equal("HEALTHCHECK --interval=40s CMD command", instruction.ToString());

            instruction.IntervalToken.Value = "30s";
            Assert.Equal("30s", instruction.Interval);
            Assert.Equal("30s", instruction.IntervalToken.Value);
            Assert.Equal("HEALTHCHECK --interval=30s CMD command", instruction.ToString());

            instruction.IntervalToken = null;
            Assert.Null(instruction.Interval);
            Assert.Null(instruction.IntervalToken);
            Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());
        }

        [Fact]
        public void IntervalWithVariables()
        {
            HealthCheckInstruction instruction = new HealthCheckInstruction("command", interval: "$var");
            TestHelper.TestVariablesWithNullableLiteral(
                () => instruction.IntervalToken, token => instruction.IntervalToken = token, val => instruction.Interval = val, "var", canContainVariables: true);
        }

        [Fact]
        public void Timeout()
        {
            HealthCheckInstruction instruction = new HealthCheckInstruction("command", timeout: "10s");
            Assert.Equal("10s", instruction.Timeout);
            Assert.Equal("10s", instruction.TimeoutToken.Value);
            Assert.Equal("HEALTHCHECK --timeout=10s CMD command", instruction.ToString());

            instruction.Timeout = "20s";
            Assert.Equal("20s", instruction.Timeout);
            Assert.Equal("20s", instruction.TimeoutToken.Value);
            Assert.Equal("HEALTHCHECK --timeout=20s CMD command", instruction.ToString());

            instruction.Timeout = null;
            Assert.Null(instruction.Timeout);
            Assert.Null(instruction.TimeoutToken);
            Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());

            instruction.TimeoutToken = new LiteralToken("40s");
            Assert.Equal("40s", instruction.Timeout);
            Assert.Equal("40s", instruction.TimeoutToken.Value);
            Assert.Equal("HEALTHCHECK --timeout=40s CMD command", instruction.ToString());

            instruction.TimeoutToken.Value = "30s";
            Assert.Equal("30s", instruction.Timeout);
            Assert.Equal("30s", instruction.TimeoutToken.Value);
            Assert.Equal("HEALTHCHECK --timeout=30s CMD command", instruction.ToString());

            instruction.TimeoutToken = null;
            Assert.Null(instruction.Timeout);
            Assert.Null(instruction.TimeoutToken);
            Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());
        }

        [Fact]
        public void TimeoutWithVariables()
        {
            HealthCheckInstruction instruction = new HealthCheckInstruction("command", timeout: "$var");
            TestHelper.TestVariablesWithNullableLiteral(
                () => instruction.TimeoutToken, token => instruction.TimeoutToken = token, val => instruction.Timeout = val, "var", canContainVariables: true);
        }

        [Fact]
        public void StartPeriod()
        {
            HealthCheckInstruction instruction = new HealthCheckInstruction("command", startPeriod: "10s");
            Assert.Equal("10s", instruction.StartPeriod);
            Assert.Equal("10s", instruction.StartPeriodToken.Value);
            Assert.Equal("HEALTHCHECK --start-period=10s CMD command", instruction.ToString());

            instruction.StartPeriod = "20s";
            Assert.Equal("20s", instruction.StartPeriod);
            Assert.Equal("20s", instruction.StartPeriodToken.Value);
            Assert.Equal("HEALTHCHECK --start-period=20s CMD command", instruction.ToString());

            instruction.StartPeriod = null;
            Assert.Null(instruction.StartPeriod);
            Assert.Null(instruction.StartPeriodToken);
            Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());

            instruction.StartPeriodToken = new LiteralToken("40s");
            Assert.Equal("40s", instruction.StartPeriod);
            Assert.Equal("40s", instruction.StartPeriodToken.Value);
            Assert.Equal("HEALTHCHECK --start-period=40s CMD command", instruction.ToString());

            instruction.StartPeriodToken.Value = "30s";
            Assert.Equal("30s", instruction.StartPeriod);
            Assert.Equal("30s", instruction.StartPeriodToken.Value);
            Assert.Equal("HEALTHCHECK --start-period=30s CMD command", instruction.ToString());

            instruction.StartPeriodToken = null;
            Assert.Null(instruction.StartPeriod);
            Assert.Null(instruction.StartPeriodToken);
            Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());
        }

        [Fact]
        public void StartPeriodWithVariables()
        {
            HealthCheckInstruction instruction = new HealthCheckInstruction("command", startPeriod: "$var");
            TestHelper.TestVariablesWithNullableLiteral(
                () => instruction.StartPeriodToken, token => instruction.StartPeriodToken = token, val => instruction.StartPeriod = val, "var", canContainVariables: true);
        }

        [Fact]
        public void Retries()
        {
            HealthCheckInstruction instruction = new HealthCheckInstruction("command", retries: "10s");
            Assert.Equal("10s", instruction.Retries);
            Assert.Equal("10s", instruction.RetriesToken.Value);
            Assert.Equal("HEALTHCHECK --retries=10s CMD command", instruction.ToString());

            instruction.Retries = "20s";
            Assert.Equal("20s", instruction.Retries);
            Assert.Equal("20s", instruction.RetriesToken.Value);
            Assert.Equal("HEALTHCHECK --retries=20s CMD command", instruction.ToString());

            instruction.Retries = null;
            Assert.Null(instruction.Retries);
            Assert.Null(instruction.RetriesToken);
            Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());

            instruction.RetriesToken = new LiteralToken("40s");
            Assert.Equal("40s", instruction.Retries);
            Assert.Equal("40s", instruction.RetriesToken.Value);
            Assert.Equal("HEALTHCHECK --retries=40s CMD command", instruction.ToString());

            instruction.RetriesToken.Value = "30s";
            Assert.Equal("30s", instruction.Retries);
            Assert.Equal("30s", instruction.RetriesToken.Value);
            Assert.Equal("HEALTHCHECK --retries=30s CMD command", instruction.ToString());

            instruction.RetriesToken = null;
            Assert.Null(instruction.Retries);
            Assert.Null(instruction.RetriesToken);
            Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());
        }

        [Fact]
        public void RetriesWithVariables()
        {
            HealthCheckInstruction instruction = new HealthCheckInstruction("command", retries: "$var");
            TestHelper.TestVariablesWithNullableLiteral(
                () => instruction.RetriesToken, token => instruction.RetriesToken = token, val => instruction.Retries = val, "var", canContainVariables: true);
        }

        [Fact]
        public void Command()
        {
            HealthCheckInstruction instruction = new HealthCheckInstruction("command1");
            Assert.Equal("HEALTHCHECK CMD command1", instruction.ToString());

            instruction.CmdInstruction = new CmdInstruction(new string[] { "command", "arg" });
            Assert.Equal("HEALTHCHECK CMD [\"command\", \"arg\"]", instruction.ToString());

            instruction.CmdInstruction = null;
            Assert.Equal("HEALTHCHECK NONE", instruction.ToString());

            instruction.CmdInstruction = new CmdInstruction("cmd");
            Assert.Equal("HEALTHCHECK CMD cmd", instruction.ToString());
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            HealthCheckInstructionParseTestScenario[] testInputs = new HealthCheckInstructionParseTestScenario[]
            {
                new HealthCheckInstructionParseTestScenario
                {
                    Text = "HEALTHCHECK NONE",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "HEALTHCHECK"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyword(token, "NONE")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("HEALTHCHECK", result.InstructionName);
                        Assert.Null(result.CmdInstruction);
                    }
                },
                new HealthCheckInstructionParseTestScenario
                {
                    Text = "HEALTHCHECK CMD /bin/check-running",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "HEALTHCHECK"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<CmdInstruction>(token, "CMD /bin/check-running",
                            token => ValidateKeyword(token, "CMD"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                                token => ValidateLiteral(token, "/bin/check-running")))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("HEALTHCHECK", result.InstructionName);
                        Assert.IsType<ShellFormCommand>(result.CmdInstruction.Command);
                        Assert.Equal("/bin/check-running", ((ShellFormCommand)result.CmdInstruction.Command).Value);
                    }
                },
                new HealthCheckInstructionParseTestScenario
                {
                    Text = "HEALTHCHECK --start-period=10s CMD /bin/check-running",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "HEALTHCHECK"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "10s"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<CmdInstruction>(token, "CMD /bin/check-running",
                            token => ValidateKeyword(token, "CMD"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                                token => ValidateLiteral(token, "/bin/check-running")))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("HEALTHCHECK", result.InstructionName);
                        Assert.IsType<ShellFormCommand>(result.CmdInstruction.Command);
                        Assert.Equal("/bin/check-running", ((ShellFormCommand)result.CmdInstruction.Command).Value);
                        Assert.Equal("10s", result.StartPeriod);
                    }
                },
                new HealthCheckInstructionParseTestScenario
                {
                    Text = "HEALTHCHECK --interval=1m --start-period=10s --retries=5 --timeout=1h CMD /bin/check-running",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "HEALTHCHECK"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyValueFlag<IntervalFlag>(token, "interval", "1m"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "10s"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyValueFlag<RetriesFlag>(token, "retries", "5"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyValueFlag<TimeoutFlag>(token, "timeout", "1h"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<CmdInstruction>(token, "CMD /bin/check-running",
                            token => ValidateKeyword(token, "CMD"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                                token => ValidateLiteral(token, "/bin/check-running")))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("HEALTHCHECK", result.InstructionName);
                        Assert.IsType<ShellFormCommand>(result.CmdInstruction.Command);
                        Assert.Equal("/bin/check-running", ((ShellFormCommand)result.CmdInstruction.Command).Value);
                        Assert.Equal("10s", result.StartPeriod);
                        Assert.Equal("5", result.Retries);
                        Assert.Equal("1m", result.Interval);
                        Assert.Equal("1h", result.Timeout);
                    }
                },
                new HealthCheckInstructionParseTestScenario
                {
                    Text = "HEALTHCHECK `\n--start-period=10s `\nCMD /bin/check-running",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "HEALTHCHECK"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "10s"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateAggregate<CmdInstruction>(token, "CMD /bin/check-running",
                            token => ValidateKeyword(token, "CMD"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                                token => ValidateLiteral(token, "/bin/check-running")))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("HEALTHCHECK", result.InstructionName);
                        Assert.IsType<ShellFormCommand>(result.CmdInstruction.Command);
                        Assert.Equal("/bin/check-running", ((ShellFormCommand)result.CmdInstruction.Command).Value);
                        Assert.Equal("10s", result.StartPeriod);
                    }
                },
            };

            return testInputs.Select(input => new object[] { input });
        }

        public static IEnumerable<object[]> CreateTestInput()
        {
            CreateTestScenario[] testInputs = new CreateTestScenario[]
            {
                new CreateTestScenario
                {
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "HEALTHCHECK"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyword(token, "NONE")
                    }
                },
                new CreateTestScenario
                {
                    Command = "/bin/check-running",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "HEALTHCHECK"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<CmdInstruction>(token, "CMD /bin/check-running",
                            token => ValidateKeyword(token, "CMD"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                                token => ValidateLiteral(token, "/bin/check-running")))
                    }
                },
                new CreateTestScenario
                {
                    Command = "/bin/check-running",
                    Args = new string[]
                    {
                        "-f"
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "HEALTHCHECK"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<CmdInstruction>(token, "CMD [\"/bin/check-running\", \"-f\"]",
                            token => ValidateKeyword(token, "CMD"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<ExecFormCommand>(token, "[\"/bin/check-running\", \"-f\"]",
                                token => ValidateSymbol(token, '['),
                                token => ValidateLiteral(token, "/bin/check-running", '\"'),
                                token => ValidateSymbol(token, ','),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLiteral(token, "-f", '\"'),
                                token => ValidateSymbol(token, ']')))
                    }
                },
                new CreateTestScenario
                {
                    Command = "/bin/check-running",
                    Interval = "1s",
                    StartPeriod = "2s",
                    Timeout = "3s",
                    Retries = "10",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "HEALTHCHECK"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyValueFlag<IntervalFlag>(token, "interval", "1s"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyValueFlag<TimeoutFlag>(token, "timeout", "3s"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "2s"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyValueFlag<RetriesFlag>(token, "retries", "10"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<CmdInstruction>(token, "CMD /bin/check-running",
                            token => ValidateKeyword(token, "CMD"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                                token => ValidateLiteral(token, "/bin/check-running")))
                    }
                },
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class HealthCheckInstructionParseTestScenario : ParseTestScenario<HealthCheckInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<HealthCheckInstruction>
        {
            public string Command { get; set; }
            public IEnumerable<string> Args { get; set; }
            public string Interval { get; set; }
            public string Timeout { get; set; }
            public string StartPeriod { get; set; }
            public string Retries { get; set; }
        }
    }
}
