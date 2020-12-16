using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class StopSignalInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(StopSignalInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                StopSignalInstruction result = StopSignalInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => StopSignalInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            StopSignalInstruction result = new StopSignalInstruction(scenario.Signal);

            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Signal()
        {
            StopSignalInstruction result = new StopSignalInstruction("test");
            Assert.Equal("test", result.Signal);
            Assert.Equal("test", result.SignalToken.Value);
            Assert.Equal("STOPSIGNAL test", result.ToString());

            result.Signal = "test2";
            Assert.Equal("test2", result.Signal);
            Assert.Equal("test2", result.SignalToken.Value);
            Assert.Equal("STOPSIGNAL test2", result.ToString());

            result.SignalToken.Value = "test3";
            Assert.Equal("test3", result.Signal);
            Assert.Equal("test3", result.SignalToken.Value);
            Assert.Equal("STOPSIGNAL test3", result.ToString());

            Assert.Throws<ArgumentNullException>(() => result.Signal = null);
            Assert.Throws<ArgumentException>(() => result.Signal = "");
            Assert.Throws<ArgumentNullException>(() => result.SignalToken = null);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            StopSignalInstructionParseTestScenario[] testInputs = new StopSignalInstructionParseTestScenario[]
            {
                new StopSignalInstructionParseTestScenario
                {
                    Text = "STOPSIGNAL name",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "STOPSIGNAL"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "name")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("STOPSIGNAL", result.InstructionName);
                        Assert.Equal("name", result.Signal);
                    }
                },
                new StopSignalInstructionParseTestScenario
                {
                    Text = "STOPSIGNAL `\n name",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "STOPSIGNAL"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "name")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("STOPSIGNAL", result.InstructionName);
                        Assert.Equal("name", result.Signal);
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
                    Signal = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "STOPSIGNAL"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "test")
                    }
                },
                new CreateTestScenario
                {
                    Signal = "1",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "STOPSIGNAL"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "1")
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class StopSignalInstructionParseTestScenario : ParseTestScenario<StopSignalInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<StopSignalInstruction>
        {
            public string Signal { get; set; }
        }
    }
}
