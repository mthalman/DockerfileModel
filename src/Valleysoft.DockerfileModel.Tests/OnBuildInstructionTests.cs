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
}
