using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class KeywordTokenTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(KeywordTokenParseTestScenario scenario)
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
        KeywordTokenParseTestScenario[] testInputs = new KeywordTokenParseTestScenario[]
        {
            new KeywordTokenParseTestScenario
            {
                Text = "test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test")
                }
            },
            new KeywordTokenParseTestScenario
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

public class KeywordTokenParseTestScenario : ParseTestScenario<KeywordToken>
{
    public char EscapeChar { get; set; }
}
