using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class UserInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<UserInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, UserInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        UserInstruction result = new(scenario.User);

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void UserProperty()
    {
        UserInstruction result = new("test");
        Assert.Equal("test", result.User);
        Assert.Equal("USER test", result.ToString());

        result.User = "root:root";
        Assert.Equal("root:root", result.User);
        Assert.Equal("USER root:root", result.ToString());

        Assert.Throws<ArgumentNullException>(() => result.User = null);
        Assert.Throws<ArgumentException>(() => result.User = "");
    }

    [Fact]
    public void UserTokenProperty()
    {
        UserInstruction result = UserInstruction.Parse("USER alice");
        LiteralToken token = result.UserToken;
        Assert.Equal("alice", token.Value);

        result.UserToken = new LiteralToken("bob");
        Assert.Equal("bob", result.User);
        Assert.Equal("USER bob", result.ToString());

        Assert.Throws<ArgumentNullException>(() => result.UserToken = null);
    }

    [Fact]
    public void UserGroupIsOpaqueLiteral()
    {
        UserInstruction result = UserInstruction.Parse("USER alice:staff");

        // The entire "alice:staff" is a single opaque LiteralToken
        Assert.Equal("alice:staff", result.User);
        Assert.IsType<LiteralToken>(result.UserToken);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<UserInstruction>[] testInputs = new ParseTestScenario<UserInstruction>[]
        {
            new ParseTestScenario<UserInstruction>
            {
                Text = "USER name",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "USER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "name")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("USER", result.InstructionName);
                    Assert.Equal("name", result.User);
                }
            },
            new ParseTestScenario<UserInstruction>
            {
                Text = "USER name\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "USER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "name"),
                    token => ValidateNewLine(token, "\n")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("USER", result.InstructionName);
                    Assert.Equal("name", result.User);
                }
            },
            new ParseTestScenario<UserInstruction>
            {
                Text = "USER user:group",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "USER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "user:group")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("USER", result.InstructionName);
                    Assert.Equal("user:group", result.User);
                }
            },
            new ParseTestScenario<UserInstruction>
            {
                Text = "USER $var",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "USER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateQuotableAggregate<LiteralToken>(token, "$var", null,
                        token => ValidateAggregate<VariableRefToken>(token, "$var",
                            token => ValidateString(token, "var")))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("USER", result.InstructionName);
                    Assert.Equal("$var", result.User);
                }
            },
            new ParseTestScenario<UserInstruction>
            {
                Text = "USER `\n name",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "USER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "name")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("USER", result.InstructionName);
                    Assert.Equal("name", result.User);
                }
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
                User = "name",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "USER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "name")
                }
            },
            new CreateTestScenario
            {
                User = "user:group",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "USER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "user:group")
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<UserInstruction>
    {
        public string User { get; set; }
    }
}
