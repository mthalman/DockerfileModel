using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class ExcludeFlagTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<ExcludeFlag> scenario) =>
        TestHelper.RunParseTest(scenario, ExcludeFlag.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        ExcludeFlag result = new(scenario.Pattern);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<ExcludeFlag>[] testInputs = new ParseTestScenario<ExcludeFlag>[]
        {
            // simple glob pattern
            new ParseTestScenario<ExcludeFlag>
            {
                Text = "--exclude=*.txt",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "exclude"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "*.txt")
                },
                Validate = result =>
                {
                    Assert.Equal("exclude", result.Key);
                    Assert.Equal("*.txt", result.Value);
                }
            },
            // directory pattern
            new ParseTestScenario<ExcludeFlag>
            {
                Text = "--exclude=docs/",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "exclude"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "docs/")
                },
                Validate = result =>
                {
                    Assert.Equal("exclude", result.Key);
                    Assert.Equal("docs/", result.Value);
                }
            },
            // variable reference
            new ParseTestScenario<ExcludeFlag>
            {
                Text = "--exclude=$PATTERN",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "exclude"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateAggregate<LiteralToken>(token, "$PATTERN",
                        token => ValidateAggregate<VariableRefToken>(token, "$PATTERN",
                            token => ValidateString(token, "PATTERN")))
                },
                Validate = result =>
                {
                    Assert.Equal("exclude", result.Key);
                    Assert.Equal("$PATTERN", result.Value);
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
                Pattern = "*.txt",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "exclude"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "*.txt")
                },
                Validate = result =>
                {
                    Assert.Equal("exclude", result.Key);
                    Assert.Equal("*.txt", result.Value);
                    Assert.Equal("--exclude=*.txt", result.ToString());
                }
            },
            new CreateTestScenario
            {
                Pattern = "docs/",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "exclude"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "docs/")
                },
                Validate = result =>
                {
                    Assert.Equal("exclude", result.Key);
                    Assert.Equal("docs/", result.Value);
                    Assert.Equal("--exclude=docs/", result.ToString());
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<ExcludeFlag>
    {
        public string Pattern { get; set; }
    }
}
