using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class UserAccountTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<UserAccount> scenario) =>
        TestHelper.RunParseTest(scenario, UserAccount.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        UserAccount result = new(scenario.User, scenario.Group);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void User()
    {
        UserAccount UserAccount = new("test", "group");
        Assert.Equal("test", UserAccount.User);
        Assert.Equal("test", UserAccount.UserToken.Value);

        UserAccount.User = "test2";
        Assert.Equal("test2", UserAccount.User);
        Assert.Equal("test2", UserAccount.UserToken.Value);

        UserAccount.UserToken.Value = "test3";
        Assert.Equal("test3", UserAccount.User);
        Assert.Equal("test3", UserAccount.UserToken.Value);

        UserAccount.UserToken = new LiteralToken("test4");
        Assert.Equal("test4", UserAccount.User);
        Assert.Equal("test4", UserAccount.UserToken.Value);
        Assert.Equal("test4:group", UserAccount.ToString());

        Assert.Throws<ArgumentException>(() => UserAccount.User = "");
        Assert.Throws<ArgumentNullException>(() => UserAccount.User = null);
        Assert.Throws<ArgumentNullException>(() => UserAccount.UserToken = null);
    }

    [Fact]
    public void Group()
    {
        UserAccount UserAccount = new("user", "test");
        Assert.Equal("test", UserAccount.Group);
        Assert.Equal("test", UserAccount.GroupToken.Value);

        UserAccount.Group = "test2";
        Assert.Equal("test2", UserAccount.Group);
        Assert.Equal("test2", UserAccount.GroupToken.Value);

        UserAccount.GroupToken.Value = "test3";
        Assert.Equal("test3", UserAccount.Group);
        Assert.Equal("test3", UserAccount.GroupToken.Value);

        UserAccount.Group = null;
        Assert.Null(UserAccount.Group);
        Assert.Null(UserAccount.GroupToken);
        Assert.Equal("user", UserAccount.ToString());

        UserAccount.GroupToken = new LiteralToken("test4");
        Assert.Equal("test4", UserAccount.Group);
        Assert.Equal("test4", UserAccount.GroupToken.Value);
        Assert.Equal("user:test4", UserAccount.ToString());

        UserAccount.GroupToken = null;
        Assert.Null(UserAccount.Group);
        Assert.Null(UserAccount.GroupToken);
        Assert.Equal("user", UserAccount.ToString());

        UserAccount.Group = "";
        Assert.Null(UserAccount.Group);
        Assert.Null(UserAccount.GroupToken);
        Assert.Equal("user", UserAccount.ToString());
    }

    [Fact]
    public void UserWithVariables()
    {
        UserAccount UserAccount = new("$var", "group");
        TestHelper.TestVariablesWithLiteral(
            () => UserAccount.UserToken, "var", canContainVariables: true);
    }

    [Fact]
    public void GroupWithVariables()
    {
        UserAccount UserAccount = new("user", "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => UserAccount.GroupToken, token => UserAccount.GroupToken = token, val => UserAccount.Group = val, "var", canContainVariables: true);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<UserAccount>[] testInputs = new ParseTestScenario<UserAccount>[]
        {
            new ParseTestScenario<UserAccount>
            {
                Text = "55:mygroup",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateLiteral(token, "55"),
                    token => ValidateSymbol(token, ':'),
                    token => ValidateLiteral(token, "mygroup")
                },
                Validate = result =>
                {
                    Assert.Equal("55", result.User);
                    Assert.Equal("mygroup", result.Group);
                }
            },
            new ParseTestScenario<UserAccount>
            {
                Text = "bin",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateLiteral(token, "bin")
                },
                Validate = result =>
                {
                    Assert.Equal("bin", result.User);
                    Assert.Null(result.Group);
                }
            },
            new ParseTestScenario<UserAccount>
            {
                EscapeChar = '`',
                Text = "us`\ner`\n:`\ngr`\noup",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateAggregate<LiteralToken>(token, "us`\ner",
                        token => ValidateString(token, "us"),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateString(token, "er")),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateSymbol(token, ':'),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateAggregate<LiteralToken>(token, "gr`\noup",
                        token => ValidateString(token, "gr"),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateString(token, "oup"))
                },
                Validate = result =>
                {
                    Assert.Equal("user", result.User);
                    Assert.Equal("group", result.Group);

                    result.Group = null;
                    Assert.Equal("us`\ner`\n", result.ToString());
                }
            },
            new ParseTestScenario<UserAccount>
            {
                Text = "$user:group$var",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateAggregate<LiteralToken>(token, "$user",
                        token => ValidateAggregate<VariableRefToken>(token, "$user",
                            token => ValidateString(token, "user"))),
                    token => ValidateSymbol(token, ':'),
                    token => ValidateAggregate<LiteralToken>(token, "group$var",
                        token => ValidateString(token, "group"),
                        token => ValidateAggregate<VariableRefToken>(token, "$var",
                            token => ValidateString(token, "var")))
                }
            },
            new ParseTestScenario<UserAccount>
            {
                Text = "user:",
                ParseExceptionPosition = new Position(1, 1, 5)
            },
            new ParseTestScenario<UserAccount>
            {
                Text = ":group",
                ParseExceptionPosition = new Position(1, 1, 1)
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
                User = "user",
                Group = "group",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateLiteral(token, "user"),
                    token => ValidateSymbol(token, ':'),
                    token => ValidateLiteral(token, "group")
                },
                Validate = result =>
                {
                    Assert.Equal("user", result.User);
                    Assert.Equal("group", result.Group);
                }
            },
            new CreateTestScenario
            {
                User = "user",
                Group = null,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateLiteral(token, "user")
                },
                Validate = result =>
                {
                    Assert.Equal("user", result.User);
                    Assert.Null(result.Group);
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<UserAccount>
    {
        public string User { get; set; }
        public string Group { get; set; }
    }
}
