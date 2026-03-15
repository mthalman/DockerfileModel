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
        OnBuildInstruction result = new(scenario.Instruction);

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Instruction()
    {
        OnBuildInstruction result = new(new CmdInstruction("test"));
        Assert.Equal("CMD test", result.Instruction.ToString());
        Assert.Equal("ONBUILD CMD test", result.ToString());

        result.Instruction = new RunInstruction("test2");
        Assert.Equal("RUN test2", result.Instruction.ToString());
        Assert.Equal("ONBUILD RUN test2", result.ToString());

        Assert.Throws<ArgumentNullException>(() => result.Instruction = null);
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
                    token => ValidateAggregate<ArgInstruction>(token, "ARG name",
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "name",
                            token => ValidateIdentifier<Variable>(token, "name")))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.Equal("ARG name", result.Instruction.ToString());
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
                    token => ValidateAggregate<ArgInstruction>(token, "ARG name",
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "name",
                            token => ValidateIdentifier<Variable>(token, "name")))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.Equal("ARG name", result.Instruction.ToString());
                }
            },
            // ONBUILD with RUN shell form
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD RUN echo hello",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<RunInstruction>(token, "RUN echo hello",
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormCommand>(token, "echo hello",
                            token => ValidateLiteral(token, "echo hello")))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.Equal("RUN echo hello", result.Instruction.ToString());
                    Assert.IsType<RunInstruction>(result.Instruction);
                }
            },
            // ONBUILD with RUN containing line continuation and comment
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD RUN echo hello \\\n# test comment\nworld",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<RunInstruction>(token, "RUN echo hello \\\n# test comment\nworld",
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ShellFormCommand>(token, "echo hello \\\n# test comment\nworld"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.IsType<RunInstruction>(result.Instruction);
                    Assert.Equal("echo hello world", ((RunInstruction)result.Instruction).Command!.ToString().Replace("\\\n", "").Replace("# test comment\n", "").Trim());
                }
            },
            // ONBUILD with COPY instruction
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD COPY . /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<CopyInstruction>(token, "COPY . /app",
                        token => ValidateKeyword(token, "COPY"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "."),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "/app"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.IsType<CopyInstruction>(result.Instruction);
                }
            },
            // ONBUILD with WORKDIR instruction
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD WORKDIR /app",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<WorkdirInstruction>(token, "WORKDIR /app",
                        token => ValidateKeyword(token, "WORKDIR"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "/app"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.IsType<WorkdirInstruction>(result.Instruction);
                }
            },
            // ONBUILD with EXPOSE instruction
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD EXPOSE 8080",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExposeInstruction>(token, "EXPOSE 8080",
                        token => ValidateKeyword(token, "EXPOSE"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "8080"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.IsType<ExposeInstruction>(result.Instruction);
                }
            },
            // ONBUILD with RUN exec form
            new ParseTestScenario<OnBuildInstruction>
            {
                Text = "ONBUILD RUN [\"echo\", \"hello\"]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<RunInstruction>(token, "RUN [\"echo\", \"hello\"]",
                        token => ValidateKeyword(token, "RUN"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ExecFormCommand>(token, "[\"echo\", \"hello\"]",
                            token => ValidateSymbol(token, '['),
                            token => ValidateLiteral(token, "echo", ParseHelper.DoubleQuote),
                            token => ValidateSymbol(token, ','),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLiteral(token, "hello", ParseHelper.DoubleQuote),
                            token => ValidateSymbol(token, ']')))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("ONBUILD", result.InstructionName);
                    Assert.IsType<RunInstruction>(result.Instruction);
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
                Instruction = new CopyInstruction(new string[] { "." }, "."),
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ONBUILD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<CopyInstruction>(token, "COPY . .",
                        token => ValidateKeyword(token, "COPY"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "."),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "."))
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<OnBuildInstruction>
    {
        public Instruction Instruction { get; set; }
    }

    [Fact]
    public void OnBuildInstruction_Simple_RoundTrips()
    {
        string text = "ONBUILD RUN echo hello\n";
        OnBuildInstruction inst = OnBuildInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }
}
