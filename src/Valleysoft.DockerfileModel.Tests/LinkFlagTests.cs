using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class LinkFlagTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<LinkFlag> scenario) =>
        TestHelper.RunParseTest(scenario, LinkFlag.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        LinkFlag result = new();
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<LinkFlag>[] testInputs = new ParseTestScenario<LinkFlag>[]
        {
            new ParseTestScenario<LinkFlag>
            {
                Text = "--link",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "link")
                },
                Validate = result =>
                {
                    Assert.Equal("--link", result.ToString());
                    Assert.Equal("link", result.Key);
                    Assert.Null(((IKeyValuePair)result).Value);
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
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "link")
                },
                Validate = result =>
                {
                    Assert.Equal("--link", result.ToString());
                    Assert.Equal("link", result.Key);
                    Assert.Null(((IKeyValuePair)result).Value);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<LinkFlag>
    {
    }
}
