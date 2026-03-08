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

        // Verify initial parse preserves StringToken/WhitespaceToken structure
        Token[] initialTokens = result.TriggerInstructionToken.Tokens.ToArray();
        Assert.Collection(initialTokens,
            token => ValidateString(token, "CMD"),
            token => ValidateWhitespace(token, " "),
            token => ValidateString(token, "test"));

        result.TriggerInstruction = "RUN test2";
        Assert.Equal("RUN test2", result.TriggerInstruction);
        Assert.Equal("ONBUILD RUN test2", result.ToString());

        // After mutation, LiteralToken.Value setter re-parses through
        // WrappedInOptionalQuotesLiteralStringWithSpaces, which collapses
        // WhitespaceTokens into StringTokens. The token structure changes
        // from [String, Whitespace, String] to [String("RUN test2")].
        Token[] mutatedTokens = result.TriggerInstructionToken.Tokens.ToArray();
        Assert.Single(mutatedTokens);
        Assert.IsType<StringToken>(mutatedTokens[0]);
        Assert.Equal("RUN test2", ((StringToken)mutatedTokens[0]).Value);

        Assert.Throws<ArgumentNullException>(() => result.TriggerInstruction = null);
        Assert.Throws<ArgumentException>(() => result.TriggerInstruction = "");
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
