using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class UnpackFlagTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<UnpackFlag> scenario) =>
        TestHelper.RunParseTest(scenario, UnpackFlag.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        UnpackFlag result = new();
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void ValueProperty()
    {
        UnpackFlag result = UnpackFlag.Parse("--unpack");

        // Value getter returns null (via null! on the non-nullable override)
        Assert.Null(result.Value);

        // Value setter throws NotSupportedException
        Assert.Throws<NotSupportedException>(() => result.Value = "test");

        // IKeyValuePair.Value getter returns null
        Assert.Null(((IKeyValuePair)result).Value);

        // IKeyValuePair.Value setter throws NotSupportedException
        Assert.Throws<NotSupportedException>(() => ((IKeyValuePair)result).Value = "test");
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<UnpackFlag>[] testInputs = new ParseTestScenario<UnpackFlag>[]
        {
            new ParseTestScenario<UnpackFlag>
            {
                Text = "--unpack",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "unpack")
                },
                Validate = result =>
                {
                    Assert.Equal("--unpack", result.ToString());
                    Assert.Equal("unpack", result.Key);
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
                    token => ValidateKeyword(token, "unpack")
                },
                Validate = result =>
                {
                    Assert.Equal("--unpack", result.ToString());
                    Assert.Equal("unpack", result.Key);
                    Assert.Null(((IKeyValuePair)result).Value);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<UnpackFlag>
    {
    }
}
