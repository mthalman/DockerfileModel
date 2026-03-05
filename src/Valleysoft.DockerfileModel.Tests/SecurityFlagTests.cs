using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class SecurityFlagTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(SecurityFlagParseTestScenario scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            SecurityFlag result = SecurityFlag.Parse(scenario.Text, scenario.EscapeChar);
            Assert.Equal(scenario.Text, result.ToString());
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }
        else
        {
            ParseException exception = Assert.Throws<ParseException>(
                () => SecurityFlag.Parse(scenario.Text, scenario.EscapeChar));
            Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
            Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
        }
    }

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        SecurityFlag result = new(scenario.Security);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        SecurityFlagParseTestScenario[] testInputs = new SecurityFlagParseTestScenario[]
        {
            new SecurityFlagParseTestScenario
            {
                Text = "--security=insecure",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "security"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "insecure")
                },
                Validate = result =>
                {
                    Assert.Equal("security", result.Key);
                    Assert.Equal("insecure", result.Value);
                }
            },
            new SecurityFlagParseTestScenario
            {
                Text = "--security=sandbox",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "security"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "sandbox")
                },
                Validate = result =>
                {
                    Assert.Equal("security", result.Key);
                    Assert.Equal("sandbox", result.Value);
                }
            },
            new SecurityFlagParseTestScenario
            {
                Text = "--security=$SEC",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "security"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateAggregate<LiteralToken>(token, "$SEC",
                        token => ValidateAggregate<VariableRefToken>(token, "$SEC",
                            token => ValidateString(token, "SEC")))
                },
                Validate = result =>
                {
                    Assert.Equal("security", result.Key);
                    Assert.Equal("$SEC", result.Value);
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
                Security = "insecure",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "security"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "insecure")
                },
                Validate = result =>
                {
                    Assert.Equal("security", result.Key);
                    Assert.Equal("insecure", result.Value);
                    Assert.Equal("--security=insecure", result.ToString());
                }
            },
            new CreateTestScenario
            {
                Security = "sandbox",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "security"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "sandbox")
                },
                Validate = result =>
                {
                    Assert.Equal("security", result.Key);
                    Assert.Equal("sandbox", result.Value);
                    Assert.Equal("--security=sandbox", result.ToString());
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class SecurityFlagParseTestScenario : ParseTestScenario<SecurityFlag>
    {
        public char EscapeChar { get; set; }
    }

    public class CreateTestScenario : TestScenario<SecurityFlag>
    {
        public string Security { get; set; }
    }
}
