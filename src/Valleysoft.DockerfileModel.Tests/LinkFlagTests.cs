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
        LinkFlag result = scenario.CreateFlag();
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void ValueProperty()
    {
        LinkFlag result = LinkFlag.Parse("--link");

        // Value getter returns null for bare flag
        Assert.Null(result.Value);

        // Value setter throws NotSupportedException
        Assert.Throws<NotSupportedException>(() => result.Value = "test");

        // IKeyValuePair.Value getter returns null
        Assert.Null(((IKeyValuePair)result).Value);

        // IKeyValuePair.Value setter throws NotSupportedException
        Assert.Throws<NotSupportedException>(() => ((IKeyValuePair)result).Value = "test");
    }

    [Fact]
    public void ValueProperty_ExplicitTrue()
    {
        LinkFlag result = LinkFlag.Parse("--link=true");

        Assert.Equal("true", result.Value);
        Assert.True(result.BoolValue);
    }

    [Fact]
    public void ValueProperty_ExplicitFalse()
    {
        LinkFlag result = LinkFlag.Parse("--link=false");

        Assert.Equal("false", result.Value);
        Assert.False(result.BoolValue);
    }

    [Fact]
    public void BoolValue_BareFlag()
    {
        LinkFlag result = LinkFlag.Parse("--link");
        Assert.True(result.BoolValue);
    }

    [Theory]
    [InlineData("--link=yes")]
    [InlineData("--link=1")]
    [InlineData("--link=0")]
    [InlineData("--link=")]
    [InlineData("--link=on")]
    [InlineData("--link=off")]
    public void Parse_InvalidValues_ThrowsParseException(string text)
    {
        Assert.Throws<ParseException>(() => LinkFlag.Parse(text));
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
                    Assert.True(result.BoolValue);
                }
            },
            new ParseTestScenario<LinkFlag>
            {
                Text = "--link=true",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "link"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "true")
                },
                Validate = result =>
                {
                    Assert.Equal("--link=true", result.ToString());
                    Assert.Equal("link", result.Key);
                    Assert.Equal("true", result.Value);
                    Assert.True(result.BoolValue);
                }
            },
            new ParseTestScenario<LinkFlag>
            {
                Text = "--link=false",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "link"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "false")
                },
                Validate = result =>
                {
                    Assert.Equal("--link=false", result.ToString());
                    Assert.Equal("link", result.Key);
                    Assert.Equal("false", result.Value);
                    Assert.False(result.BoolValue);
                }
            },
            new ParseTestScenario<LinkFlag>
            {
                Text = "--link=True",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "link"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "True")
                },
                Validate = result =>
                {
                    Assert.Equal("--link=True", result.ToString());
                    Assert.Equal("link", result.Key);
                    Assert.Equal("True", result.Value);
                    Assert.True(result.BoolValue);
                }
            },
            new ParseTestScenario<LinkFlag>
            {
                Text = "--link=FALSE",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "link"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "FALSE")
                },
                Validate = result =>
                {
                    Assert.Equal("--link=FALSE", result.ToString());
                    Assert.Equal("link", result.Key);
                    Assert.Equal("FALSE", result.Value);
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
                CreateFlag = () => new LinkFlag(),
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
                    Assert.True(result.BoolValue);
                }
            },
            new CreateTestScenario
            {
                CreateFlag = () => new LinkFlag(true),
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "link"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "true")
                },
                Validate = result =>
                {
                    Assert.Equal("--link=true", result.ToString());
                    Assert.Equal("link", result.Key);
                    Assert.Equal("true", result.Value);
                    Assert.True(result.BoolValue);
                }
            },
            new CreateTestScenario
            {
                CreateFlag = () => new LinkFlag(false),
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "link"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "false")
                },
                Validate = result =>
                {
                    Assert.Equal("--link=false", result.ToString());
                    Assert.Equal("link", result.Key);
                    Assert.Equal("false", result.Value);
                    Assert.False(result.BoolValue);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<LinkFlag>
    {
        public Func<LinkFlag> CreateFlag { get; set; } = () => new LinkFlag();
    }
}
