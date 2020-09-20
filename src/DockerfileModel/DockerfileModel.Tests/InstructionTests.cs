using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class InstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(InstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                Instruction result = Instruction.Parse(scenario.Text, scenario.EscapeChar);
                ValidateAggregate<Instruction>(result, scenario.Text, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => Instruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            Instruction result = Instruction.Create(scenario.InstructionName, scenario.Args);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate(result);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            var testInputs = new InstructionParseTestScenario[]
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
                        Assert.Equal(@"echo ""hello world""", result.ArgLines.Single().Value);

                        result.InstructionName = "ARG";
                        result.ArgLines.Single().Value = "MY_ARG";
                        Assert.Equal($"{result.InstructionName} MY_ARG", result.ToString());
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
                        Assert.Equal(@"echo ""hello world""", result.ArgLines.Single().Value);

                        result.InstructionName = "ARG";
                        result.ArgLines.Single().Value = "MY_ARG";
                        Assert.Equal($"{result.InstructionName} MY_ARG\n", result.ToString());
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
                        token => ValidateLineContinuation(token, "\\"),
                        token => ValidateNewLine(token, "\r\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateLiteral(token, "&& ls -a"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("run", result.InstructionName);
                        var argLines = result.ArgLines.ToArray();
                        Assert.Equal(2, argLines.Length);
                        Assert.Equal(@"echo ""hello world""", argLines[0].Value);
                        Assert.Equal(@"&& ls -a", argLines[1].Value);

                        result.InstructionName = "ARG";
                        argLines[0].Value = @"echo ""hello WORLD""";
                        argLines[1].Value = "&& ls";
                        Assert.Equal(
                            $"{result.InstructionName} {argLines[0].Value}  \\\r\n  {argLines[1].Value}",
                            result.ToString());
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
                        token => ValidateLineContinuation(token, "\\"),
                        token => ValidateNewLine(token, "\r\n"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, "\\"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateLiteral(token, "&& ls -a"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("run", result.InstructionName);
                        var argLines = result.ArgLines.ToArray();
                        Assert.Equal(2, argLines.Length);
                        Assert.Equal(@"echo ""hello world""", argLines[0].Value);
                        Assert.Equal(@"&& ls -a", argLines[1].Value);

                        result.InstructionName = "ARG";
                        argLines[0].Value = @"echo ""hello WORLD""";
                        argLines[1].Value = "&& ls";
                        Assert.Equal(
                            $"{result.InstructionName} {argLines[0].Value}  \\\r\n \\\n  {argLines[1].Value}",
                            result.ToString());
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
                        token => ValidateLineContinuation(token, "\\"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateAggregate<CommentToken>(token, "# comment1",
                            token => ValidatePunctuation(token, "#"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLiteral(token, "comment1")),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateAggregate<CommentToken>(token, "# comment 2",
                            token => ValidatePunctuation(token, "#"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLiteral(token, "comment 2")),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateLiteral(token, "VAR=value")
                    },
                    Validate = result =>
                    {
                        Assert.Collection(result.Comments,
                            token => ValidateAggregate<CommentToken>(token, "# comment1",
                                token => ValidatePunctuation(token, "#"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLiteral(token, "comment1")),
                            token => ValidateAggregate<CommentToken>(token, "# comment 2",
                                token => ValidatePunctuation(token, "#"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLiteral(token, "comment 2")));
                        Assert.Collection(result.ArgLines,
                            token => ValidateLiteral(token, "VAR=value"));
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
                        token => ValidateLineContinuation(token, "\\"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateLiteral(token, "VAR=value")
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public static IEnumerable<object[]> CreateTestInput()
        {
            var testInputs = new CreateTestScenario[]
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
                        Assert.Equal("VAL=1", result.ArgLines.Single().Value);

                        result.ArgLines.Single().Value = "VAL=2";
                        Assert.Equal($"{result.InstructionName} VAL=2", result.ToString());
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class InstructionParseTestScenario : ParseTestScenario<Instruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<Instruction>
        {
            public string InstructionName { get; set; }
            public string Args { get; set; }
        }
    }
}
