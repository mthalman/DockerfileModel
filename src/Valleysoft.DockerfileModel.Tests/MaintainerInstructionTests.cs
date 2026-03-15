using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class MaintainerInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<MaintainerInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, MaintainerInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        MaintainerInstruction result = new(scenario.Maintainer);

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void MaintainerWithVariables()
    {
        MaintainerInstruction result = new("$test");
        TestHelper.TestVariablesWithLiteral(() => result.MaintainerToken, "test", canContainVariables: true);
    }

    [Fact]
    public void Maintainer()
    {
        MaintainerInstruction result = new("test");
        Assert.Equal("test", result.Maintainer);
        Assert.Equal("test", result.MaintainerToken.Value);
        Assert.Equal("MAINTAINER test", result.ToString());

        result.Maintainer = "test2";
        Assert.Equal("test2", result.Maintainer);
        Assert.Equal("test2", result.MaintainerToken.Value);
        Assert.Equal("MAINTAINER test2", result.ToString());

        result.MaintainerToken.Value = "test3";
        Assert.Equal("test3", result.Maintainer);
        Assert.Equal("test3", result.MaintainerToken.Value);
        Assert.Equal("MAINTAINER test3", result.ToString());

        result.Maintainer = "";
        result.MaintainerToken.QuoteChar = '\"';
        Assert.Equal("", result.Maintainer);
        Assert.Equal("", result.MaintainerToken.Value);
        Assert.Equal("MAINTAINER \"\"", result.ToString());
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<MaintainerInstruction>[] testInputs = new ParseTestScenario<MaintainerInstruction>[]
        {
            new ParseTestScenario<MaintainerInstruction>
            {
                Text = "MAINTAINER name",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "MAINTAINER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "name")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("MAINTAINER", result.InstructionName);
                    Assert.Equal("name", result.Maintainer);
                }
            },
            new ParseTestScenario<MaintainerInstruction>
            {
                Text = "MAINTAINER \"name\"",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "MAINTAINER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "name", '\"')
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("MAINTAINER", result.InstructionName);
                    Assert.Equal("name", result.Maintainer);
                }
            },
            new ParseTestScenario<MaintainerInstruction>
            {
                Text = "MAINTAINER \"\"",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "MAINTAINER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "", '\"')
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("MAINTAINER", result.InstructionName);
                    Assert.Equal("", result.Maintainer);
                }
            },
            new ParseTestScenario<MaintainerInstruction>
            {
                Text = "MAINTAINER $var",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "MAINTAINER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateQuotableAggregate<LiteralToken>(token, "$var", null,
                        token => ValidateAggregate<VariableRefToken>(token, "$var",
                            token => ValidateString(token, "var")))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("MAINTAINER", result.InstructionName);
                    Assert.Equal("$var", result.Maintainer);
                }
            },
            new ParseTestScenario<MaintainerInstruction>
            {
                Text = "MAINTAINER `\n name",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "MAINTAINER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "name")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("MAINTAINER", result.InstructionName);
                    Assert.Equal("name", result.Maintainer);
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
                Maintainer = "test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "MAINTAINER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "test")
                }
            },
            new CreateTestScenario
            {
                Maintainer = "",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "MAINTAINER"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "", '\"')
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<MaintainerInstruction>
    {
        public string Maintainer { get; set; }
    }

    /// <summary>
    /// Bug: MaintainerInstruction.Maintainer includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/283
    /// </summary>
    [Fact]
    public void MaintainerInstruction_Maintainer_DoesNotIncludeNewline()
    {
        string text = "MAINTAINER test@example.com\n";
        MaintainerInstruction inst = MaintainerInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
        Assert.Equal("test@example.com", inst.Maintainer);
    }

    [Fact]
    public void MaintainerInstruction_Maintainer_NoNewlineInInput_Works()
    {
        string text = "MAINTAINER test@example.com";
        MaintainerInstruction inst = MaintainerInstruction.Parse(text);
        Assert.Equal("test@example.com", inst.Maintainer);
    }

    /// <summary>
    /// Bug: MaintainerInstruction.Maintainer includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/283
    /// </summary>
    [Fact]
    public void MaintainerInstruction_Maintainer_WithNewlineInInput_IncludesNewline_BUG()
    {
        // BUG: Maintainer property includes trailing newline
        string text = "MAINTAINER test@example.com\n";
        MaintainerInstruction inst = MaintainerInstruction.Parse(text);
        string maintainer = inst.Maintainer;
        Assert.Contains("\n", maintainer); // Bug confirmed
    }

    /// <summary>
    /// Bug: MaintainerInstruction.Maintainer includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/283
    /// </summary>
    [Fact]
    public void MaintainerInstruction_MaintainerWithSpaces_WithNewline_IncludesNewline_BUG()
    {
        // Maintainer can contain spaces (WhitespaceMode.Allowed) — also has the bug
        string text = "MAINTAINER John Doe <john@example.com>\n";
        MaintainerInstruction inst = MaintainerInstruction.Parse(text);
        string maintainer = inst.Maintainer;
        Assert.Contains("\n", maintainer); // Bug confirmed
    }

    /// <summary>
    /// Bug: MaintainerInstruction.Maintainer includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/283
    /// </summary>
    [Fact]
    public void MaintainerInstruction_RoundTrip_WorksButMaintainerPropertyBroken()
    {
        string text = "MAINTAINER maintainer@example.com\n";
        MaintainerInstruction inst = MaintainerInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
        Assert.NotEqual("maintainer@example.com", inst.Maintainer); // BUG
        Assert.Equal("maintainer@example.com\n", inst.Maintainer);
    }
}
