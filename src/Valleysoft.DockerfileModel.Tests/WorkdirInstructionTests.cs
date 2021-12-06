using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class WorkdirInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(WorkdirInstructionParseTestScenario scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            WorkdirInstruction result = WorkdirInstruction.Parse(scenario.Text, scenario.EscapeChar);
            Assert.Equal(scenario.Text, result.ToString());
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }
        else
        {
            ParseException exception = Assert.Throws<ParseException>(
                () => WorkdirInstruction.Parse(scenario.Text, scenario.EscapeChar));
            Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
            Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
        }
    }

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        WorkdirInstruction result = new(scenario.Path);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Path()
    {
        WorkdirInstruction result = new("/test");
        Assert.Equal("/test", result.Path);
        Assert.Equal("/test", result.PathToken.Value);
        Assert.Equal("WORKDIR /test", result.ToString());

        result.Path = "/test2";
        Assert.Equal("/test2", result.Path);
        Assert.Equal("/test2", result.PathToken.Value);
        Assert.Equal("WORKDIR /test2", result.ToString());

        result.PathToken.Value = "/test3";
        Assert.Equal("/test3", result.Path);
        Assert.Equal("/test3", result.PathToken.Value);
        Assert.Equal("WORKDIR /test3", result.ToString());

        result.PathToken = new LiteralToken("/test4");
        Assert.Equal("/test4", result.Path);
        Assert.Equal("/test4", result.PathToken.Value);
        Assert.Equal("WORKDIR /test4", result.ToString());

        Assert.Throws<ArgumentNullException>(() => result.Path = null);
        Assert.Throws<ArgumentException>(() => result.Path = "");
        Assert.Throws<ArgumentNullException>(() => result.PathToken = null);
    }

    [Fact]
    public void PathWithVariables()
    {
        WorkdirInstruction result = new("$var");
        TestHelper.TestVariablesWithLiteral(() => result.PathToken, "var", canContainVariables: true);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        WorkdirInstructionParseTestScenario[] testInputs = new WorkdirInstructionParseTestScenario[]
        {
            new WorkdirInstructionParseTestScenario
            {
                Text = "WORKDIR /test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "WORKDIR"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/test")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("WORKDIR", result.InstructionName);
                    Assert.Equal("/test", result.Path);
                }
            },
            new WorkdirInstructionParseTestScenario
            {
                Text = "WORKDIR $TEST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "WORKDIR"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "$TEST",
                        token => ValidateAggregate<VariableRefToken>(token, "$TEST",
                            token => ValidateString(token, "TEST")))
                }
            },
            new WorkdirInstructionParseTestScenario
            {
                Text = "WORKDIR`\n /test",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "WORKDIR"),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/test")
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
                Path = "/test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "WORKDIR"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/test")
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class WorkdirInstructionParseTestScenario : ParseTestScenario<WorkdirInstruction>
    {
        public char EscapeChar { get; set; }
    }

    public class CreateTestScenario : TestScenario<WorkdirInstruction>
    {
        public string Path { get; set; }
    }
}
