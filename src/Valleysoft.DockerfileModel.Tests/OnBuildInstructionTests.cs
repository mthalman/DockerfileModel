using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class OnBuildInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<OnBuildInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, OnBuildInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        OnBuildInstruction result = new(scenario.TriggerInstruction);

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void TriggerInstruction()
    {
        OnBuildInstruction result = new("CMD test");
        Assert.Equal("CMD test", result.TriggerInstruction);
        Assert.Equal("ONBUILD CMD test", result.ToString());

        result.TriggerInstruction = "RUN test2";
        Assert.Equal("RUN test2", result.TriggerInstruction);
        Assert.Equal("ONBUILD RUN test2", result.ToString());

        Assert.Throws<ArgumentNullException>(() => result.TriggerInstruction = null);
        Assert.Throws<ArgumentException>(() => result.TriggerInstruction = "");
        Assert.Throws<ArgumentException>(() => result.TriggerInstruction = " ");
    }

    [Fact]
    public void TriggerInstruction_RejectsEmptyAndWhitespace()
    {
        Assert.Throws<ArgumentException>(() => new OnBuildInstruction(""));
        Assert.Throws<ArgumentException>(() => new OnBuildInstruction(" "));
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<OnBuildInstruction>[] testInputs = new ParseTestScenario<OnBuildInstruction>[]
        {
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD ARG name",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "ARG name",
                        token => ValidateString(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "name"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.Equal("ARG name", result.TriggerInstruction);
                }
            },
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD `\n ARG name",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "ARG name",
                        token => ValidateString(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "name"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.Equal("ARG name", result.TriggerInstruction);
                }
            },
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD ARG `\n# my comment\nname",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "ARG `\n# my comment\nname",
                        token => ValidateString(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateAggregate<CommentToken>(token, "# my comment\n",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateString(token, "my comment"),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateString(token, "name"))
                },
                Validate = result =>
                {
                    Assert.Collection(result.Comments,
                        comment => Assert.Equal("my comment", comment));
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.Equal("ARG name", result.TriggerInstruction);
                }
            },
            // Indented comment on a continuation line within trigger text:
            // leading whitespace before '#' is absorbed into the CommentToken so it
            // does NOT leak into TriggerInstruction.
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD ARG \\\n  # comment\nname",
                EscapeChar = '\\',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "ARG \\\n  # comment\nname",
                        token => ValidateString(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, '\\', "\n"),
                        token => ValidateAggregate<CommentToken>(token, "  # comment\n",
                            token => ValidateWhitespace(token, "  "),
                            token => ValidateSymbol(token, '#'),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateString(token, "comment"),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateString(token, "name"))
                },
                Validate = result =>
                {
                    Assert.Collection(result.Comments,
                        comment => Assert.Equal("comment", comment));
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.Equal("ARG name", result.TriggerInstruction);
                }
            },
            // $ is treated as a regular character in ONBUILD trigger text (no
            // VariableRefToken decomposition) — matching BuildKit behavior.
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD RUN echo $VAR",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "RUN echo $VAR",
                        token => ValidateString(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "echo"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "$VAR"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.Equal("RUN echo $VAR", result.TriggerInstruction);
                    // Verify no VariableRefToken exists in the trigger literal
                    Assert.Empty(result.TriggerInstructionToken.Tokens.OfType<VariableRefToken>());
                }
            },
            // Trigger text that is only a line continuation + comment should fail to parse
            // because the trigger value (excluding comments/continuations) would be empty.
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD \\\n# comment\n",
                EscapeChar = '\\',
                ParseExceptionPosition = new Position(0, 3, 1)
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
                TriggerInstruction = "COPY . .",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "COPY . .",
                        token => ValidateString(token, "COPY"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "."),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateString(token, "."))
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<OnBuildInstruction>
    {
        public string TriggerInstruction { get; set; }
    }
}
