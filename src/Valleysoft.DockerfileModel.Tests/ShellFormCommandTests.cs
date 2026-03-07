using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class ShellFormCommandTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<ShellFormCommand> scenario) =>
        TestHelper.RunParseTest(scenario, ShellFormCommand.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        ShellFormCommand result = new(scenario.Command);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Value()
    {
        ShellFormCommand result = new("echo hello");
        Assert.Equal("echo hello", result.Value);
        Assert.Equal("echo hello", result.ValueToken.Value);

        result.Value = "echo bye";
        Assert.Equal("echo bye", result.Value);
        Assert.Equal("echo bye", result.ValueToken.Value);

        result.ValueToken.Value = "echo hola";
        Assert.Equal("echo hola", result.Value);
        Assert.Equal("echo hola", result.ValueToken.Value);

        Assert.Throws<ArgumentNullException>(() => result.Value = null);
        Assert.Throws<ArgumentException>(() => result.Value = "");
    }

    [Fact]
    public void ValueWithVariables()
    {
        ShellFormCommand result = new("$var");
        TestHelper.TestVariablesWithLiteral(() => result.ValueToken, "$var", canContainVariables: false);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<ShellFormCommand>[] testInputs = new ParseTestScenario<ShellFormCommand>[]
        {
            new ParseTestScenario<ShellFormCommand>
            {
                Text = "echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateQuotableAggregate<LiteralToken>(token, "echo hello", null,
                        token => ValidateString(token, "echo"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "hello"))
                },
                Validate = result =>
                {
                    Assert.Equal(CommandType.ShellForm, result.CommandType);
                    Assert.Equal("echo hello", result.ToString());
                    Assert.Equal("echo hello", result.Value);
                }
            },
            new ParseTestScenario<ShellFormCommand>
            {
                Text = "echo `\n#test comment\nhello",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateQuotableAggregate<LiteralToken>(token, "echo `\n#test comment\nhello", null,
                        token => ValidateString(token, "echo"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateAggregate<CommentToken>(token, "#test comment\n",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateString(token, "test comment"),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateString(token, "hello"))
                },
                Validate = result =>
                {
                    Assert.Equal(CommandType.ShellForm, result.CommandType);
                    Assert.Equal("echo `\n#test comment\nhello", result.ToString());
                    Assert.Equal("echo hello", result.Value);
                }
            },
            new ParseTestScenario<ShellFormCommand>
            {
                Text = "echo`\n  `\n  hello",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateAggregate<LiteralToken>(token, "echo`\n  `\n  hello",
                        token => ValidateString(token, "echo"),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateString(token, "hello")),
                }
            },
            new ParseTestScenario<ShellFormCommand>
            {
                Text = "ec`\nho `test",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateAggregate<LiteralToken>(token, "ec`\nho `test",
                        token => ValidateString(token, "ec"),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateString(token, "ho"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "`test"))
                }
            },
            new ParseTestScenario<ShellFormCommand>
            {
                Text = "\"ec`\nh`\"o `test\"",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateQuotableAggregate<LiteralToken>(token, "\"ec`\nh`\"o `test\"", null,
                        token => ValidateString(token, "\"ec"),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateString(token, "h`\"o"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "`test\""))
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
                Command = "echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateQuotableAggregate<LiteralToken>(token, "echo hello", null,
                        token => ValidateString(token, "echo"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "hello"))
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<ShellFormCommand>
    {
        public string Command { get; set; }
    }
}
