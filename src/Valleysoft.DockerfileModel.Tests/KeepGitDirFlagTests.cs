using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class KeepGitDirFlagTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<KeepGitDirFlag> scenario) =>
        TestHelper.RunParseTest(scenario, KeepGitDirFlag.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        KeepGitDirFlag result = scenario.CreateFlag();
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void BoolValue_BareFlag()
    {
        KeepGitDirFlag result = KeepGitDirFlag.Parse("--keep-git-dir");
        Assert.True(result.BoolValue);
        Assert.Null(result.Value);
    }

    [Fact]
    public void BoolValue_ExplicitTrue()
    {
        KeepGitDirFlag result = KeepGitDirFlag.Parse("--keep-git-dir=true");
        Assert.True(result.BoolValue);
        Assert.Equal("true", result.Value);
    }

    [Fact]
    public void BoolValue_ExplicitFalse()
    {
        KeepGitDirFlag result = KeepGitDirFlag.Parse("--keep-git-dir=false");
        Assert.False(result.BoolValue);
        Assert.Equal("false", result.Value);
    }

    [Theory]
    [InlineData("--keep-git-dir=yes")]
    [InlineData("--keep-git-dir=1")]
    [InlineData("--keep-git-dir=")]
    public void Parse_InvalidValues_ThrowsParseException(string text)
    {
        Assert.Throws<ParseException>(() => KeepGitDirFlag.Parse(text));
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<KeepGitDirFlag>[] testInputs = new ParseTestScenario<KeepGitDirFlag>[]
        {
            new ParseTestScenario<KeepGitDirFlag>
            {
                Text = "--keep-git-dir",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "keep-git-dir")
                },
                Validate = result =>
                {
                    Assert.Equal("--keep-git-dir", result.ToString());
                    Assert.Equal("keep-git-dir", result.Key);
                    Assert.Null(((IKeyValuePair)result).Value);
                    Assert.True(result.BoolValue);
                }
            },
            new ParseTestScenario<KeepGitDirFlag>
            {
                Text = "--keep-git-dir=true",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "keep-git-dir"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "true")
                },
                Validate = result =>
                {
                    Assert.Equal("--keep-git-dir=true", result.ToString());
                    Assert.Equal("keep-git-dir", result.Key);
                    Assert.Equal("true", result.Value);
                    Assert.True(result.BoolValue);
                }
            },
            new ParseTestScenario<KeepGitDirFlag>
            {
                Text = "--keep-git-dir=false",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "keep-git-dir"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "false")
                },
                Validate = result =>
                {
                    Assert.Equal("--keep-git-dir=false", result.ToString());
                    Assert.Equal("keep-git-dir", result.Key);
                    Assert.Equal("false", result.Value);
                    Assert.False(result.BoolValue);
                }
            },
            new ParseTestScenario<KeepGitDirFlag>
            {
                Text = "--keep-git-dir=TRUE",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "keep-git-dir"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "TRUE")
                },
                Validate = result =>
                {
                    Assert.Equal("--keep-git-dir=TRUE", result.ToString());
                    Assert.Equal("keep-git-dir", result.Key);
                    Assert.Equal("TRUE", result.Value);
                    Assert.True(result.BoolValue);
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
                CreateFlag = () => new KeepGitDirFlag(),
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "keep-git-dir")
                },
                Validate = result =>
                {
                    Assert.Equal("--keep-git-dir", result.ToString());
                    Assert.Equal("keep-git-dir", result.Key);
                    Assert.Null(((IKeyValuePair)result).Value);
                    Assert.True(result.BoolValue);
                }
            },
            new CreateTestScenario
            {
                CreateFlag = () => new KeepGitDirFlag(true),
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "keep-git-dir"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "true")
                },
                Validate = result =>
                {
                    Assert.Equal("--keep-git-dir=true", result.ToString());
                    Assert.Equal("keep-git-dir", result.Key);
                    Assert.Equal("true", result.Value);
                    Assert.True(result.BoolValue);
                }
            },
            new CreateTestScenario
            {
                CreateFlag = () => new KeepGitDirFlag(false),
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "keep-git-dir"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "false")
                },
                Validate = result =>
                {
                    Assert.Equal("--keep-git-dir=false", result.ToString());
                    Assert.Equal("keep-git-dir", result.Key);
                    Assert.Equal("false", result.Value);
                    Assert.False(result.BoolValue);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<KeepGitDirFlag>
    {
        public Func<KeepGitDirFlag> CreateFlag { get; set; } = () => new KeepGitDirFlag();
    }
}
