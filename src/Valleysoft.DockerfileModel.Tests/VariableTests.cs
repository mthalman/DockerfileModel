using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class VariableTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<Variable> scenario) =>
        TestHelper.RunParseTest(scenario, (text, escapeChar) => new Variable(text, escapeChar));

    [Fact]
    public void Value()
    {
        Variable variable = new("test");
        Assert.Equal("test", variable.Value);

        variable.Value = "test2";
        Assert.Equal("test2", variable.Value);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<Variable>[] testInputs = new ParseTestScenario<Variable>[]
        {
            new ParseTestScenario<Variable>
            {
                Text = "test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test"),
                },
                Validate = result =>
                {
                    Assert.Equal("test", result.Value);
                }
            },
            new ParseTestScenario<Variable>
            {
                Text = "test_x",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test_x"),
                },
                Validate = result =>
                {
                    Assert.Equal("test_x", result.Value);
                }
            },
            new ParseTestScenario<Variable>
            {
                Text = "te`\nst",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "te"),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateString(token, "st"),
                },
                Validate = result =>
                {
                    Assert.Equal("test", result.Value);
                }
            },
            new ParseTestScenario<Variable>
            {
                Text = "_test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "_test"),
                },
                Validate = result =>
                {
                    Assert.Equal("_test", result.Value);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

}
