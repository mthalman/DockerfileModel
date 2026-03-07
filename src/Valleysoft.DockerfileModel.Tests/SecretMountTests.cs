using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class SecretMountTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<SecretMount> scenario) =>
        TestHelper.RunParseTest(scenario, SecretMount.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        SecretMount result = new(scenario.Id, scenario.DestinationPath, scenario.EnvironmentVariable);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void CreateInvalid()
    {
        Assert.Throws<InvalidOperationException>(() => new SecretMount("id", "dst", "var"));
    }

    [Fact]
    public void Id()
    {
        SecretMount secretMount = new("test");
        Assert.Equal("test", secretMount.Id);
        Assert.Equal("test", secretMount.IdToken.Value);

        secretMount.Id = "test2";
        Assert.Equal("test2", secretMount.Id);
        Assert.Equal("test2", secretMount.IdToken.Value);

        secretMount.IdToken.ValueToken.Value = "test3";
        Assert.Equal("test3", secretMount.Id);
        Assert.Equal("test3", secretMount.IdToken.Value);

        secretMount.IdToken = new KeyValueToken<KeywordToken, LiteralToken>(
            new KeywordToken("id"), new LiteralToken("test4"));
        Assert.Equal("test4", secretMount.Id);
        Assert.Equal("test4", secretMount.IdToken.Value);

        Assert.Throws<ArgumentNullException>(() => secretMount.Id = null);
        Assert.Throws<ArgumentNullException>(() => secretMount.IdToken = null);
    }

    [Fact]
    public void IdWithVariables()
    {
        SecretMount secretMount = new("$var");
        TestHelper.TestVariablesWithLiteral(() => secretMount.IdToken.ValueToken, "var", canContainVariables: true);
    }

    [Fact]
    public void DestinationPath()
    {
        SecretMount secretMount = new("foo", "test");
        Assert.Equal("test", secretMount.DestinationPath);
        Assert.Equal("test", secretMount.DestinationPathToken.Value);

        secretMount.DestinationPath = "test2";
        Assert.Equal("test2", secretMount.DestinationPath);
        Assert.Equal("test2", secretMount.DestinationPathToken.Value);

        secretMount.DestinationPathToken.ValueToken.Value = "test3";
        Assert.Equal("test3", secretMount.DestinationPath);
        Assert.Equal("test3", secretMount.DestinationPathToken.Value);

        secretMount.DestinationPathToken = new KeyValueToken<KeywordToken, LiteralToken>(
            new KeywordToken("dst"), new LiteralToken("test4"));
        Assert.Equal("test4", secretMount.DestinationPath);
        Assert.Equal("test4", secretMount.DestinationPathToken.Value);
        Assert.Equal("type=secret,id=foo,dst=test4", secretMount.ToString());

        secretMount.DestinationPath = null;
        Assert.Equal("type=secret,id=foo", secretMount.ToString());
        Assert.Null(secretMount.DestinationPath);
        Assert.Null(secretMount.DestinationPathToken);

        secretMount.DestinationPath = "test5";

        Assert.Throws<InvalidOperationException>(() => secretMount.EnvironmentVariable = "foo");
        Assert.Throws<InvalidOperationException>(() => secretMount.EnvironmentVariableToken =
            new KeyValueToken<KeywordToken, LiteralToken>(new KeywordToken("env"), new LiteralToken("foo")));

        secretMount.DestinationPathToken = null;
        Assert.Equal("type=secret,id=foo", secretMount.ToString());
        Assert.Null(secretMount.DestinationPath);
        Assert.Null(secretMount.DestinationPathToken);
    }

    [Fact]
    public void DestinationPathWithVariables()
    {
        SecretMount secretMount = new("id", "$var");
        TestHelper.TestVariablesWithLiteral(
            () => secretMount.DestinationPathToken.ValueToken, "var", canContainVariables: true);
    }

    [Fact]
    public void EnvironmentVariable()
    {
        SecretMount secretMount = new("foo", environmentVariable: "test");
        Assert.Equal("test", secretMount.EnvironmentVariable);
        Assert.Equal("test", secretMount.EnvironmentVariableToken.Value);

        secretMount.EnvironmentVariable = "test2";
        Assert.Equal("test2", secretMount.EnvironmentVariable);
        Assert.Equal("test2", secretMount.EnvironmentVariableToken.Value);

        secretMount.EnvironmentVariableToken.ValueToken.Value = "test3";
        Assert.Equal("test3", secretMount.EnvironmentVariable);
        Assert.Equal("test3", secretMount.EnvironmentVariableToken.Value);

        secretMount.EnvironmentVariableToken = new KeyValueToken<KeywordToken, LiteralToken>(
            new KeywordToken("env"), new LiteralToken("test4"));
        Assert.Equal("test4", secretMount.EnvironmentVariable);
        Assert.Equal("test4", secretMount.EnvironmentVariableToken.Value);
        Assert.Equal("type=secret,id=foo,env=test4", secretMount.ToString());

        secretMount.EnvironmentVariable = null;
        Assert.Equal("type=secret,id=foo", secretMount.ToString());
        Assert.Null(secretMount.EnvironmentVariable);
        Assert.Null(secretMount.EnvironmentVariableToken);

        secretMount.EnvironmentVariable = "test5";

        Assert.Throws<InvalidOperationException>(() => secretMount.DestinationPath = "foo");
        Assert.Throws<InvalidOperationException>(() => secretMount.DestinationPathToken =
            new KeyValueToken<KeywordToken, LiteralToken>(new KeywordToken("dst"), new LiteralToken("foo")));

        secretMount.EnvironmentVariableToken = null;
        Assert.Equal("type=secret,id=foo", secretMount.ToString());
        Assert.Null(secretMount.EnvironmentVariable);
        Assert.Null(secretMount.EnvironmentVariableToken);
    }

    [Fact]
    public void EnvironmentVariableWithVariables()
    {
        SecretMount secretMount = new("id", environmentVariable: "$var");
        TestHelper.TestVariablesWithLiteral(
            () => secretMount.EnvironmentVariableToken.ValueToken, "var", canContainVariables: true);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<SecretMount>[] testInputs = new ParseTestScenario<SecretMount>[]
        {
            new ParseTestScenario<SecretMount>
            {
                Text = "type=secret,id=foo",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "foo"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("foo", result.Id);
                    Assert.Null(result.DestinationPath);
                }
            },
            new ParseTestScenario<SecretMount>
            {
                Text = "type=secret,id=foo,dst=test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "foo"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "dst", "test"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("foo", result.Id);
                    Assert.Equal("test", result.DestinationPath);
                }
            },
            new ParseTestScenario<SecretMount>
            {
                Text = "type=secret,id=foo,env=test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "foo"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "env", "test"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("foo", result.Id);
                    Assert.Equal("test", result.EnvironmentVariable);
                }
            },
            new ParseTestScenario<SecretMount>
            {
                EscapeChar = '`',
                Text = "typ`\ne`\n=`\nsecret`\n,`\nid=foo",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateAggregate<KeyValueToken<KeywordToken, LiteralToken>>(token, "typ`\ne`\n=`\nsecret",
                        token => ValidateAggregate<KeywordToken>(token, "typ`\ne",
                            token => ValidateString(token, "typ"),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateString(token, "e")),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateLiteral(token, "secret")),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateKeyValue(token, "id", "foo")
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("foo", result.Id);
                    Assert.Null(result.DestinationPath);
                }
            },
            new ParseTestScenario<SecretMount>
            {
                Text = "type=secret,id=$secretid",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateAggregate<KeyValueToken<KeywordToken, LiteralToken>>(token, "id=$secretid",
                        token => ValidateKeyword(token, "id"),
                        token => ValidateSymbol(token, '='),
                        token => ValidateAggregate<LiteralToken>(token, "$secretid",
                            token => ValidateAggregate<VariableRefToken>(token, "$secretid",
                                token => ValidateString(token, "secretid"))))
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("$secretid", result.Id);
                    Assert.Null(result.DestinationPath);
                }
            },
            new ParseTestScenario<SecretMount>
            {
                Text = "type=foo",
                ParseExceptionPosition = new Position(1, 1, 1)
            },
            new ParseTestScenario<SecretMount>
            {
                Text = "type=secret",
                ParseExceptionPosition = new Position(1, 1, 12)
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public static IEnumerable<object[]> CreateTestInput()
    {
        CreateTestScenario[] testInputs = new CreateTestScenario[]
        {
            new CreateTestScenario
            {
                Id = "foo",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "foo"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("foo", result.Id);
                    Assert.Null(result.DestinationPath);
                }
            },
            new CreateTestScenario
            {
                Id = "foo",
                DestinationPath = "test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "foo"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "dst", "test"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("foo", result.Id);
                    Assert.Equal("test", result.DestinationPath);
                }
            },
            new CreateTestScenario
            {
                Id = "foo",
                EnvironmentVariable = "test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyValue(token, "type", "secret"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "id", "foo"),
                    token => ValidateSymbol(token, ','),
                    token => ValidateKeyValue(token, "env", "test"),
                },
                Validate = result =>
                {
                    Assert.Equal("secret", result.Type);
                    Assert.Equal("foo", result.Id);
                    Assert.Equal("test", result.EnvironmentVariable);
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<SecretMount>
    {
        public string Id { get; set; }
        public string DestinationPath { get; set; }
        public string EnvironmentVariable { get; set; }
    }
}
