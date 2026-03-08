using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class StopSignalInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<StopSignalInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, StopSignalInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        StopSignalInstruction result = new(scenario.Signal);

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Signal()
    {
        StopSignalInstruction result = new("test");
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
        ParseTestScenario<StopSignalInstruction>[] testInputs = new ParseTestScenario<StopSignalInstruction>[]
        {
            new ParseTestScenario<StopSignalInstruction>
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
            new ParseTestScenario<StopSignalInstruction>
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
            },
            new ParseTestScenario<StopSignalInstruction>
            {
                Text = "STOPSIGNAL $SIG",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "STOPSIGNAL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "$SIG",
                        token => ValidateAggregate<VariableRefToken>(token, "$SIG",
                            token => ValidateString(token, "SIG")))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("STOPSIGNAL", result.InstructionName);
                    Assert.Equal("$SIG", result.Signal);
                }
            },
            new ParseTestScenario<StopSignalInstruction>
            {
                Text = "STOPSIGNAL ${SIGNAL}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "STOPSIGNAL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "${SIGNAL}",
                        token => ValidateAggregate<VariableRefToken>(token, "${SIGNAL}",
                            token => ValidateSymbol(token, '{'),
                            token => ValidateString(token, "SIGNAL"),
                            token => ValidateSymbol(token, '}')))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("STOPSIGNAL", result.InstructionName);
                    Assert.Equal("${SIGNAL}", result.Signal);
                }
            },
            new ParseTestScenario<StopSignalInstruction>
            {
                Text = "STOPSIGNAL SIG$RT",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "STOPSIGNAL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "SIG$RT",
                        token => ValidateString(token, "SIG"),
                        token => ValidateAggregate<VariableRefToken>(token, "$RT",
                            token => ValidateString(token, "RT")))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("STOPSIGNAL", result.InstructionName);
                    Assert.Equal("SIG$RT", result.Signal);
                }
            },
            new ParseTestScenario<StopSignalInstruction>
            {
                Text = "STOPSIGNAL ${SIG:-SIGTERM}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "STOPSIGNAL"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "${SIG:-SIGTERM}",
                        token => ValidateAggregate<VariableRefToken>(token, "${SIG:-SIGTERM}",
                            token => ValidateSymbol(token, '{'),
                            token => ValidateString(token, "SIG"),
                            token => ValidateSymbol(token, ':'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateAggregate<LiteralToken>(token, "SIGTERM",
                                token => ValidateString(token, "SIGTERM")),
                            token => ValidateSymbol(token, '}')))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("STOPSIGNAL", result.InstructionName);
                    Assert.Equal("${SIG:-SIGTERM}", result.Signal);
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

    public class CreateTestScenario : TestScenario<StopSignalInstruction>
    {
        public string Signal { get; set; }
    }
}
