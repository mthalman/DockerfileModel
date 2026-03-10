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

    [Fact]
    public void BoolValue_BareFlag()
    {
        UnpackFlag result = UnpackFlag.Parse("--unpack");
        Assert.True(result.BoolValue);
        Assert.Null(result.Value);
    }

    [Fact]
    public void BoolValue_ExplicitTrue()
    {
        UnpackFlag result = UnpackFlag.Parse("--unpack=true");
        Assert.True(result.BoolValue);
        Assert.Equal("true", result.Value);
    }

    [Fact]
    public void BoolValue_ExplicitFalse()
    {
        UnpackFlag result = UnpackFlag.Parse("--unpack=false");
        Assert.False(result.BoolValue);
        Assert.Equal("false", result.Value);
    }

    [Theory]
    [InlineData("--unpack=yes")]
    [InlineData("--unpack=1")]
    [InlineData("--unpack=")]
    public void Parse_InvalidValues_ThrowsParseException(string text)
    {
        Assert.Throws<ParseException>(() => UnpackFlag.Parse(text));
    }

    [Theory]
    [InlineData("--unpacker")]
    [InlineData("--unpack-extra")]
    [InlineData("--unpack/path")]
    [InlineData("--unpack_foo")]
    [InlineData("--unpack.foo")]
    public void Parse_KeywordPrefix_ThrowsParseException(string text)
    {
        // The bare-flag parser must not match when the keyword is only a prefix
        // of a longer token (e.g. --unpacker, --unpack-extra, --unpack/path).
        // The boundary guard uses an allow-list (whitespace, end-of-input, #, \) so that
        // any non-boundary character causes the parse to fail.
        Assert.Throws<ParseException>(() => UnpackFlag.Parse(text));
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
                    Assert.True(result.BoolValue);
                }
            },
            new ParseTestScenario<UnpackFlag>
            {
                Text = "--unpack=true",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "unpack"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "true")
                },
                Validate = result =>
                {
                    Assert.Equal("--unpack=true", result.ToString());
                    Assert.Equal("unpack", result.Key);
                    Assert.Equal("true", ((IKeyValuePair)result).Value);
                    Assert.True(result.BoolValue);
                }
            },
            new ParseTestScenario<UnpackFlag>
            {
                Text = "--unpack=false",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "unpack"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "false")
                },
                Validate = result =>
                {
                    Assert.Equal("--unpack=false", result.ToString());
                    Assert.Equal("unpack", result.Key);
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
