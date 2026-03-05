using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class ChecksumFlagTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<ChecksumFlag> scenario) =>
        TestHelper.RunParseTest(scenario, ChecksumFlag.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        ChecksumFlag result = new(scenario.Checksum);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<ChecksumFlag>[] testInputs = new ParseTestScenario<ChecksumFlag>[]
        {
            // sha256 hash
            new ParseTestScenario<ChecksumFlag>
            {
                Text = "--checksum=sha256:abc123def456",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "checksum"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "sha256:abc123def456")
                },
                Validate = result =>
                {
                    Assert.Equal("checksum", result.Key);
                    Assert.Equal("sha256:abc123def456", result.Value);
                }
            },
            // sha384 hash
            new ParseTestScenario<ChecksumFlag>
            {
                Text = "--checksum=sha384:deadbeef1234567890abcdef",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "checksum"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "sha384:deadbeef1234567890abcdef")
                },
                Validate = result =>
                {
                    Assert.Equal("checksum", result.Key);
                    Assert.Equal("sha384:deadbeef1234567890abcdef", result.Value);
                }
            },
            // sha512 hash
            new ParseTestScenario<ChecksumFlag>
            {
                Text = "--checksum=sha512:0123456789abcdef",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "checksum"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "sha512:0123456789abcdef")
                },
                Validate = result =>
                {
                    Assert.Equal("checksum", result.Key);
                    Assert.Equal("sha512:0123456789abcdef", result.Value);
                }
            },
            // variable reference
            new ParseTestScenario<ChecksumFlag>
            {
                Text = "--checksum=$CHECKSUM",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "checksum"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateAggregate<LiteralToken>(token, "$CHECKSUM",
                        token => ValidateAggregate<VariableRefToken>(token, "$CHECKSUM",
                            token => ValidateString(token, "CHECKSUM")))
                },
                Validate = result =>
                {
                    Assert.Equal("checksum", result.Key);
                    Assert.Equal("$CHECKSUM", result.Value);
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
                Checksum = "sha256:abc123def456",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "checksum"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "sha256:abc123def456")
                },
                Validate = result =>
                {
                    Assert.Equal("checksum", result.Key);
                    Assert.Equal("sha256:abc123def456", result.Value);
                    Assert.Equal("--checksum=sha256:abc123def456", result.ToString());
                }
            },
            new CreateTestScenario
            {
                Checksum = "sha384:deadbeef1234567890abcdef",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "checksum"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "sha384:deadbeef1234567890abcdef")
                },
                Validate = result =>
                {
                    Assert.Equal("checksum", result.Key);
                    Assert.Equal("sha384:deadbeef1234567890abcdef", result.Value);
                    Assert.Equal("--checksum=sha384:deadbeef1234567890abcdef", result.ToString());
                }
            },
            new CreateTestScenario
            {
                Checksum = "sha512:0123456789abcdef",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "checksum"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "sha512:0123456789abcdef")
                },
                Validate = result =>
                {
                    Assert.Equal("checksum", result.Key);
                    Assert.Equal("sha512:0123456789abcdef", result.Value);
                    Assert.Equal("--checksum=sha512:0123456789abcdef", result.ToString());
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<ChecksumFlag>
    {
        public string Checksum { get; set; }
    }
}
