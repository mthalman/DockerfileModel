using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class KeywordTokenTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<KeywordToken> scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            KeywordToken result = new(scenario.Text, scenario.EscapeChar);
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

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<KeywordToken>[] testInputs = new ParseTestScenario<KeywordToken>[]
        {
            new ParseTestScenario<KeywordToken>
            {
                Text = "test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test")
                }
            },
            new ParseTestScenario<KeywordToken>
            {
                Text = "t`\nest",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "t"),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateString(token, "est"),
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }
}
