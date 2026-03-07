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
        UserInstruction result = new(scenario.User, scenario.Group);

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void UserAccountProperty()
    {
        UserInstruction result = new("test");
        Assert.Equal("test", result.UserAccount);
        Assert.Equal("USER test", result.ToString());

        result.UserAccount = "testa:testb";
        Assert.Equal("testa:testb", result.UserAccount);
        Assert.Equal("USER testa:testb", result.ToString());

        Assert.Throws<ArgumentException>(() => result.UserAccount = "");
        Assert.Throws<ArgumentNullException>(() => result.UserAccount = null);
        Assert.Throws<ArgumentNullException>(() => result.UserAccountToken = null);
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
                    Assert.Equal("name", result.UserAccount);
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
                    Assert.Equal("name", result.UserAccount);
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
                    Assert.Equal("user:group", result.UserAccount);
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
                    Assert.Equal("$var", result.UserAccount);
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
                    Assert.Equal("name", result.UserAccount);
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
                User = "user",
                Group = "group",
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
        public string Group { get; set; }
    }
}
