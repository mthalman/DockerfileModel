using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class GenericInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(InstructionParseTestScenario scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            GenericInstruction result = GenericInstruction.Parse(scenario.Text, scenario.EscapeChar);
            ValidateAggregate<GenericInstruction>(result, scenario.Text, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }
        else
        {
            ParseException exception = Assert.Throws<ParseException>(
                () => GenericInstruction.Parse(scenario.Text, scenario.EscapeChar));
            Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
            Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
        }
    }

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        GenericInstruction result = new(scenario.InstructionName, scenario.Args);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate(result);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        InstructionParseTestScenario[] testInputs = new InstructionParseTestScenario[]
        {
            new InstructionParseTestScenario
            {
                Text = @"run echo ""hello world""",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "run"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, @"echo ""hello world""")
                },
                Validate = result =>
                {
                    Assert.Equal("run", result.InstructionName);
                    Assert.Equal(@"echo ""hello world""", result.ArgLines.Single());
                }
            },
            new InstructionParseTestScenario
            {
                Text = "run echo \"hello world\"\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "run"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, @"echo ""hello world"""),
                    token => ValidateNewLine(token, "\n")
                },
                Validate = result =>
                {
                    Assert.Equal("run", result.InstructionName);
                    Assert.Equal(@"echo ""hello world""", result.ArgLines.Single());
                }
            },
            new InstructionParseTestScenario
            {
                Text = $"run echo \"hello world\"  \\\r\n  && ls -a",
                EscapeChar = '\\',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "run"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, @"echo ""hello world"""),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateAggregate<LineContinuationToken>(token, "\\\r\n",
                        token => ValidateSymbol(token, '\\'),
                        token => ValidateNewLine(token, "\r\n")),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateLiteral(token, "&& ls -a"),
                },
                Validate = result =>
                {
                    Assert.Equal("run", result.InstructionName);
                    IList<string> argLines = result.ArgLines;
                    Assert.Equal(2, argLines.Count);
                    Assert.Equal(@"echo ""hello world""", argLines[0]);
                    Assert.Equal(@"&& ls -a", argLines[1]);
                }
            },
            new InstructionParseTestScenario
            {
                Text = $"run echo \"hello world\"  \\\r\n \\\n  && ls -a",
                EscapeChar = '\\',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "run"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, @"echo ""hello world"""),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateAggregate<LineContinuationToken>(token, "\\\r\n",
                        token => ValidateSymbol(token, '\\'),
                        token => ValidateNewLine(token, "\r\n")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LineContinuationToken>(token, "\\\n",
                        token => ValidateSymbol(token, '\\'),
                        token => ValidateNewLine(token, "\n")),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateLiteral(token, "&& ls -a"),
                },
                Validate = result =>
                {
                    Assert.Equal("run", result.InstructionName);
                    IList<string> argLines = result.ArgLines;
                    Assert.Equal(2, argLines.Count);
                    Assert.Equal(@"echo ""hello world""", argLines[0]);
                    Assert.Equal(@"&& ls -a", argLines[1]);
                }
            },
            new InstructionParseTestScenario
            {
                Text = "echo hello",
                EscapeChar = '\\',
                ParseExceptionPosition = new Position(1, 1, 2)
            },
            new InstructionParseTestScenario
            {
                Text = $"ENV \\\n  # comment1\n  # comment 2\n  VAR=value",
                EscapeChar = '\\',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LineContinuationToken>(token, "\\\n",
                        token => ValidateSymbol(token, '\\'),
                        token => ValidateNewLine(token, "\n")),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateAggregate<CommentToken>(token, "# comment1\n",
                        token => ValidateSymbol(token, '#'),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "comment1"),
                        token => ValidateNewLine(token, "\n")),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateAggregate<CommentToken>(token, "# comment 2\n",
                        token => ValidateSymbol(token, '#'),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "comment 2"),
                        token => ValidateNewLine(token, "\n")),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateLiteral(token, "VAR=value")
                },
                Validate = result =>
                {
                    Assert.Collection(result.Comments,
                        comment => Assert.Equal("comment1", comment),
                        comment => Assert.Equal("comment 2", comment));
                    Assert.Collection(result.ArgLines,
                        argLine => Assert.Equal("VAR=value", argLine));
                }
            },
            new InstructionParseTestScenario
            {
                Text = $"ENV \\ \n  VAR=value",
                EscapeChar = '\\',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LineContinuationToken>(token, "\\ \n",
                        token => ValidateSymbol(token, '\\'),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateNewLine(token, "\n")),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateLiteral(token, "VAR=value")
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
                InstructionName = "ENV",
                Args = "VAL=1",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ENV"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "VAL=1")
                },
                Validate = result =>
                {
                    Assert.Equal("ENV", result.InstructionName);
                    Assert.Equal("VAL=1", result.ArgLines.Single());

                    result.ArgLines[0] = "VAL=2";
                    Assert.Equal($"{result.InstructionName} VAL=2", result.ToString());
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class InstructionParseTestScenario : ParseTestScenario<GenericInstruction>
    {
        public char EscapeChar { get; set; }
    }

    public class CreateTestScenario : TestScenario<GenericInstruction>
    {
        public string InstructionName { get; set; }
        public string Args { get; set; }
    }
}
