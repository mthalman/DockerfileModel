using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class StageNameTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<StageName> scenario) =>
        TestHelper.RunParseTest(scenario, (text, escapeChar) => new StageName(text, escapeChar));

    [Fact]
    public void Value()
    {
        StageName stageName = new("test");
        Assert.Equal("test", stageName.Value);

        stageName.Value = "test2";
        Assert.Equal("test2", stageName.Value);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<StageName>[] testInputs = new ParseTestScenario<StageName>[]
        {
            new ParseTestScenario<StageName>
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
            new ParseTestScenario<StageName>
            {
                Text = "test_-.x",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test_-.x"),
                },
                Validate = result =>
                {
                    Assert.Equal("test_-.x", result.Value);
                }
            },
            new ParseTestScenario<StageName>
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
            new ParseTestScenario<StageName>
            {
                Text = "-test",
                ParseExceptionPosition = new Position(1, 1, 1)
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

}
