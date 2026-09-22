using Valleysoft.DockerfileModel.Parsing;
using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class CmdInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<CmdInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, CmdInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        CmdInstruction result;
        if (scenario.Command != null)
        {
            result = new CmdInstruction(scenario.Command);
        }
        else
        {
            Assert.NotNull(scenario.Commands);
            result = new CmdInstruction(scenario.Commands);
        }

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Parse_MalformedJsonFallsBackToShellForm()
    {
        CmdInstruction result = CmdInstruction.Parse("CMD [\"echo\"");

        Assert.Equal("CMD [\"echo\"", result.ToString());
        Assert.Equal(CommandType.ShellForm, result.Command!.CommandType);
        Assert.IsType<ShellFormCommand>(result.Command);
        Assert.Equal("[\"echo\"", ((ShellFormCommand)result.Command).Value);
    }

    [Fact]
    public void Parse_SingleQuotedJsonArrayFallsBackToShellForm()
    {
        CmdInstruction result = CmdInstruction.Parse("CMD ['echo']");

        Assert.Equal("CMD ['echo']", result.ToString());
        Assert.Equal(CommandType.ShellForm, result.Command!.CommandType);
        Assert.IsType<ShellFormCommand>(result.Command);
        Assert.Equal("['echo']", ((ShellFormCommand)result.Command).Value);
    }

    [Fact]
    public void Parse_BareBracketShellLiteralFallsBackToShellForm()
    {
        CmdInstruction result = CmdInstruction.Parse("CMD [foo]");

        Assert.Equal("CMD [foo]", result.ToString());
        Assert.Equal(CommandType.ShellForm, result.Command!.CommandType);
        Assert.IsType<ShellFormCommand>(result.Command);
        Assert.Equal("[foo]", ((ShellFormCommand)result.Command).Value);
    }

    [Fact]
    public void Parse_InvalidJsonColonArrayFallsBackToShellForm()
    {
        CmdInstruction result = CmdInstruction.Parse("CMD [\"echo\" : \"x\"]");

        Assert.Equal("CMD [\"echo\" : \"x\"]", result.ToString());
        Assert.Equal(CommandType.ShellForm, result.Command!.CommandType);
        Assert.IsType<ShellFormCommand>(result.Command);
        Assert.Equal("[\"echo\" : \"x\"]", ((ShellFormCommand)result.Command).Value);
    }

    [Fact]
    public void Parse_NestedJsonArrayRejectsNonStringElements()
    {
        Assert.Throws<ParseException>(() => CmdInstruction.Parse("CMD [\"echo\", [\"nested json\"]]"));
    }

    [Fact]
    public void Parse_NestedJsonArrayPreservesExecFormErrorPosition()
    {
        ParseException exception = Assert.Throws<ParseException>(
            () => CmdInstruction.Parse("CMD [\"echo\", [\"nested json\"]]"));

        Assert.Equal(1, exception.ErrorPosition.Line);
        Assert.Equal(12, exception.ErrorPosition.Column);
    }

    [Theory]
    [InlineData("CMD [\"echo\", \\\n 1]")]
    [InlineData("CMD [\"echo\", \\\n [\"nested json\"]]")]
    [InlineData("CMD [\"echo\", 1] \\\n# comment\n")]
    [InlineData("CMD [\"echo\", 1]\n# comment\n")]
    public void Parse_JsonArrayWithNonStringElementAndTrailingTriviaRejects(string text)
    {
        Assert.Throws<ParseException>(() => CmdInstruction.Parse(text));
    }

    [Theory]
    [InlineData("CMD [\"echo\", 1]")]
    [InlineData("CMD [\"echo\", true]")]
    [InlineData("CMD [\"echo\", null]")]
    [InlineData("CMD [\"echo\", {\"arg\":\"value\"}]")]
    public void Parse_ValidJsonArrayWithNonStringElementRejects(string text)
    {
        Assert.Throws<ParseException>(() => CmdInstruction.Parse(text));
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<CmdInstruction>[] testInputs = new ParseTestScenario<CmdInstruction>[]
        {
            new ParseTestScenario<CmdInstruction>
            {
                Text = "CMD echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("CMD", result.InstructionName);
                    TestHelper.AssertCommandType(result.Command, CommandType.ShellForm);
                    TestHelper.AssertCommandText(result.Command, "echo hello");
                    Assert.IsType<ShellFormCommand>(result.Command);
                    ShellFormCommand cmd = (ShellFormCommand)result.Command;
                    Assert.Equal("echo hello", cmd.Value);
                }
            },
            new ParseTestScenario<CmdInstruction>
            {
                Text = "CMD $TEST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "$TEST",
                        token => ValidateLiteral(token, "$TEST"))
                }
            },
            new ParseTestScenario<CmdInstruction>
            {
                Text = "CMD echo $TEST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo $TEST",
                        token => ValidateLiteral(token, "echo $TEST"))
                }
            },
            new ParseTestScenario<CmdInstruction>
            {
                Text = "CMD T\\$EST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "T\\$EST",
                        token => ValidateLiteral(token, "T\\$EST"))
                }
            },
            new ParseTestScenario<CmdInstruction>
            {
                Text = "CMD echo #not-a-comment",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo #not-a-comment",
                        token => ValidateLiteral(token, "echo #not-a-comment"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("CMD", result.InstructionName);
                    TestHelper.AssertCommandType(result.Command, CommandType.ShellForm);
                    TestHelper.AssertCommandText(result.Command, "echo #not-a-comment");
                    Assert.IsType<ShellFormCommand>(result.Command);
                    ShellFormCommand cmd = (ShellFormCommand)result.Command;
                    Assert.Equal("echo #not-a-comment", cmd.Value);
                }
            },
            new ParseTestScenario<CmdInstruction>
            {
                Text = "CMD #FF0000",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "#FF0000",
                        token => ValidateLiteral(token, "#FF0000"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("CMD", result.InstructionName);
                    TestHelper.AssertCommandType(result.Command, CommandType.ShellForm);
                    TestHelper.AssertCommandText(result.Command, "#FF0000");
                    Assert.IsType<ShellFormCommand>(result.Command);
                    ShellFormCommand cmd = (ShellFormCommand)result.Command;
                    Assert.Equal("#FF0000", cmd.Value);
                }
            },
            new ParseTestScenario<CmdInstruction>
            {
                Text = "CMD echo `\n#test comment\nhello",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo `\n#test comment\nhello",
                        token => ValidateQuotableAggregate<LiteralToken>(token, "echo `\n#test comment\nhello", null,
                            token => ValidateString(token, "echo "),
                            token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                                token => ValidateSymbol(token, '`'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateAggregate<CommentToken>(token, "#test comment\n",
                                token => ValidateSymbol(token, '#'),
                                token => ValidateString(token, "test comment"),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "hello")))
                },
                Validate = result =>
                {
                    Assert.Single(result.Comments);
                    Assert.Equal("test comment", result.Comments.First());
                    Assert.Equal("CMD", result.InstructionName);
                    TestHelper.AssertCommandType(result.Command, CommandType.ShellForm);
                    TestHelper.AssertCommandText(result.Command, "echo `\n#test comment\nhello");
                    Assert.IsType<ShellFormCommand>(result.Command);
                    ShellFormCommand cmd = (ShellFormCommand)result.Command;
                    Assert.Equal("echo hello", cmd.Value);
                }
            },
            new ParseTestScenario<CmdInstruction>
            {
                Text = "CMD []",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("CMD", result.InstructionName);
                    TestHelper.AssertCommandType(result.Command, CommandType.ExecForm);
                    TestHelper.AssertCommandText(result.Command, "[]");
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Empty(cmd.Values);
                }
            },
            // Empty exec form array with interior whitespace
            new ParseTestScenario<CmdInstruction>
            {
                Text = "CMD [ ]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[ ]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("CMD", result.InstructionName);
                    TestHelper.AssertCommandType(result.Command, CommandType.ExecForm);
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Empty(cmd.Values);
                }
            },
            new ParseTestScenario<CmdInstruction>
            {
                Text = "CMD [\"/bin/bash\", \"-c\", \"echo hello\"]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"/bin/bash\", \"-c\", \"echo hello\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/bin/bash", StringParsers.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", StringParsers.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo hello", StringParsers.DoubleQuote),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("CMD", result.InstructionName);
                    TestHelper.AssertCommandType(result.Command, CommandType.ExecForm);
                    TestHelper.AssertCommandText(result.Command, "[\"/bin/bash\", \"-c\", \"echo hello\"]");
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Equal(
                        new string[]
                        {
                            "/bin/bash",
                            "-c",
                            "echo hello"
                        },
                        cmd.Values.ToArray());
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
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                        token => ValidateLiteral(token, "echo hello"))
                }
            },
            new CreateTestScenario
            {
                Commands = Array.Empty<string>(),
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    TestHelper.AssertCommandType(result.Command, CommandType.ExecForm);
                    Assert.IsType<ExecFormCommand>(result.Command);
                    ExecFormCommand cmd = (ExecFormCommand)result.Command;
                    Assert.Empty(cmd.Values);
                }
            },
            new CreateTestScenario
            {
                Commands = new string[]
                {
                    "/bin/bash",
                    "-c",
                    "echo hello"
                },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"/bin/bash\", \"-c\", \"echo hello\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/bin/bash", StringParsers.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-c", StringParsers.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "echo hello", StringParsers.DoubleQuote),
                        token => ValidateSymbol(token, ']'))
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<CmdInstruction>
    {
        public string? Command { get; set; }
        public IEnumerable<string>? Commands { get; set; }
    }
}
