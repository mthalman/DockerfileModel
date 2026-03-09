using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class ParentsFlagTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<ParentsFlag> scenario) =>
        TestHelper.RunParseTest(scenario, ParentsFlag.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        ParentsFlag result = new();
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void ValueProperty()
    {
        ParentsFlag result = ParentsFlag.Parse("--parents");

        // Value getter returns null (via null! on the non-nullable override)
        Assert.Null(result.Value);

        // Value setter throws NotSupportedException
        Assert.Throws<NotSupportedException>(() => result.Value = "test");

        // IKeyValuePair.Value getter returns null
        Assert.Null(((IKeyValuePair)result).Value);

        // IKeyValuePair.Value setter throws NotSupportedException
        Assert.Throws<NotSupportedException>(() => ((IKeyValuePair)result).Value = "test");
    }

    [Fact]
    public void BoolValue_BareFlag()
    {
        ParentsFlag result = ParentsFlag.Parse("--parents");
        Assert.True(result.BoolValue);
        Assert.Null(result.Value);
    }

    [Fact]
    public void BoolValue_ExplicitTrue()
    {
        ParentsFlag result = ParentsFlag.Parse("--parents=true");
        Assert.True(result.BoolValue);
        Assert.Equal("true", result.Value);
    }

    [Fact]
    public void BoolValue_ExplicitFalse()
    {
        ParentsFlag result = ParentsFlag.Parse("--parents=false");
        Assert.False(result.BoolValue);
        Assert.Equal("false", result.Value);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<ParentsFlag>[] testInputs = new ParseTestScenario<ParentsFlag>[]
        {
            new ParseTestScenario<ParentsFlag>
            {
                Text = "--parents",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "parents")
                },
                Validate = result =>
                {
                    Assert.Equal("--parents", result.ToString());
                    Assert.Equal("parents", result.Key);
                    Assert.Null(((IKeyValuePair)result).Value);
                    Assert.True(result.BoolValue);
                }
            },
            new ParseTestScenario<ParentsFlag>
            {
                Text = "--parents=true",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "parents"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "true")
                },
                Validate = result =>
                {
                    Assert.Equal("--parents=true", result.ToString());
                    Assert.Equal("parents", result.Key);
                    Assert.Equal("true", ((IKeyValuePair)result).Value);
                    Assert.True(result.BoolValue);
                }
            },
            new ParseTestScenario<ParentsFlag>
            {
                Text = "--parents=false",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "parents"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "false")
                },
                Validate = result =>
                {
                    Assert.Equal("--parents=false", result.ToString());
                    Assert.Equal("parents", result.Key);
                    Assert.Equal("false", ((IKeyValuePair)result).Value);
                    Assert.False(result.BoolValue);
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
                    token => ValidateKeyword(token, "parents")
                },
                Validate = result =>
                {
                    Assert.Equal("--parents", result.ToString());
                    Assert.Equal("parents", result.Key);
                    Assert.Null(((IKeyValuePair)result).Value);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<ParentsFlag>
    {
    }
}
