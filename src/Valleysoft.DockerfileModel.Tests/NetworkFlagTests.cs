using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class NetworkFlagTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(NetworkFlagParseTestScenario scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            NetworkFlag result = NetworkFlag.Parse(scenario.Text, scenario.EscapeChar);
            Assert.Equal(scenario.Text, result.ToString());
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }
        else
        {
            ParseException exception = Assert.Throws<ParseException>(
                () => NetworkFlag.Parse(scenario.Text, scenario.EscapeChar));
            Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
            Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
        }
    }

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        NetworkFlag result = new(scenario.Network);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        NetworkFlagParseTestScenario[] testInputs = new NetworkFlagParseTestScenario[]
        {
            new NetworkFlagParseTestScenario
            {
                Text = "--network=default",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "network"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "default")
                },
                Validate = result =>
                {
                    Assert.Equal("network", result.Key);
                    Assert.Equal("default", result.Value);
                }
            },
            new NetworkFlagParseTestScenario
            {
                Text = "--network=none",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "network"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "none")
                },
                Validate = result =>
                {
                    Assert.Equal("network", result.Key);
                    Assert.Equal("none", result.Value);
                }
            },
            new NetworkFlagParseTestScenario
            {
                Text = "--network=host",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "network"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "host")
                },
                Validate = result =>
                {
                    Assert.Equal("network", result.Key);
                    Assert.Equal("host", result.Value);
                }
            },
            new NetworkFlagParseTestScenario
            {
                Text = "--network=$NET",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "network"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateAggregate<LiteralToken>(token, "$NET",
                        token => ValidateAggregate<VariableRefToken>(token, "$NET",
                            token => ValidateString(token, "NET")))
                },
                Validate = result =>
                {
                    Assert.Equal("network", result.Key);
                    Assert.Equal("$NET", result.Value);
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
                Network = "host",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "network"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "host")
                },
                Validate = result =>
                {
                    Assert.Equal("network", result.Key);
                    Assert.Equal("host", result.Value);
                    Assert.Equal("--network=host", result.ToString());
                }
            },
            new CreateTestScenario
            {
                Network = "none",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "network"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "none")
                },
                Validate = result =>
                {
                    Assert.Equal("network", result.Key);
                    Assert.Equal("none", result.Value);
                    Assert.Equal("--network=none", result.ToString());
                }
            },
            new CreateTestScenario
            {
                Network = "default",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "network"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "default")
                },
                Validate = result =>
                {
                    Assert.Equal("network", result.Key);
                    Assert.Equal("default", result.Value);
                    Assert.Equal("--network=default", result.ToString());
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class NetworkFlagParseTestScenario : ParseTestScenario<NetworkFlag>
    {
        public char EscapeChar { get; set; }
    }

    public class CreateTestScenario : TestScenario<NetworkFlag>
    {
        public string Network { get; set; }
    }
}
