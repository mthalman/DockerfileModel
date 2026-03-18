using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class LiteralTokenTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(LiteralTokenParseTestScenario scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            LiteralToken result = new(scenario.Text, scenario.ParseVariableRefs, scenario.EscapeChar);
            Assert.Equal(scenario.Text, result.ToString());
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }
        else
        {
            ParseException exception = Assert.Throws<ParseException>(
                () => ExposeInstruction.Parse(scenario.Text, scenario.EscapeChar));
            Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
            Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
        }
    }

    [Fact]
    public void ValueVariable()
    {
        LiteralToken token = new("tag", canContainVariables: true)
        {
            Value = "$REPO:tag"
        };
        token.ResolveVariables(Dockerfile.DefaultEscapeChar, new Dictionary<string, string>
        {
            { "REPO", "test" }
        }, options: new ResolutionOptions { UpdateInline = true});

        Assert.Equal("test:tag", token.Value);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        LiteralTokenParseTestScenario[] testInputs = new LiteralTokenParseTestScenario[]
        {
            new LiteralTokenParseTestScenario
            {
                Text = "test-$var",
                ParseVariableRefs = true,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test-"),
                        token => ValidateAggregate<VariableRefToken>(token, "$var",
                            token => ValidateString(token, "var"))
                }
            },
            new LiteralTokenParseTestScenario
            {
                Text = "test-$var this",
                ParseVariableRefs = true,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test-"),
                    token => ValidateAggregate<VariableRefToken>(token, "$var",
                        token => ValidateString(token, "var")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateString(token, "this")
                }
            },
            new LiteralTokenParseTestScenario
            {
                Text = "\"test this\"",
                ParseVariableRefs = false,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test this")
                },
                Validate = result =>
                {
                    Assert.Equal('\"', result.QuoteChar);
                }
            },
            new LiteralTokenParseTestScenario
            {
                Text = "\"test\"",
                ParseVariableRefs = false,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test")
                },
                Validate = result =>
                {
                    Assert.Equal('\"', result.QuoteChar);
                }
            },
            new LiteralTokenParseTestScenario
            {
                Text = "test this",
                ParseVariableRefs = false,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test this")
                }
            },
            new LiteralTokenParseTestScenario
            {
                Text = "\"test this\" \"and this\"",
                ParseVariableRefs = false,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "\"test this\" \"and this\"")
                },
                Validate = result =>
                {
                    Assert.Null(result.QuoteChar);
                }
            },
            new LiteralTokenParseTestScenario
            {
                Text = "test this $var",
                ParseVariableRefs = false,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test this $var")
                }
            },
            new LiteralTokenParseTestScenario
            {
                Text = "\"test this $var\"",
                ParseVariableRefs = false,
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateString(token, "test this $var")
                },
                Validate = result =>
                {
                    Assert.Equal('\"', result.QuoteChar);
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    /// <summary>
    /// Fixed: LiteralToken.Value no longer includes newline characters.
    /// NewLineToken children are excluded by CreateOptionsForValueString().
    /// See https://github.com/mthalman/DockerfileModel/issues/282,
    ///     https://github.com/mthalman/DockerfileModel/issues/283
    /// </summary>
    [Fact]
    public void LiteralToken_ValueWithNewline_ExcludesNewlineFromValue()
    {
        // When a LiteralToken contains a NewLineToken as a child,
        // LiteralToken.Value (which uses CreateOptionsForValueString) now strips newlines
        // via the ExcludeNewLines option.
        Tokens.LiteralToken lit = new("hello\nworld");
        string value = lit.Value;
        // The newline is NOT in the value (but ToString() still includes it for round-trip)
        Assert.DoesNotContain("\n", value);
        Assert.Equal("helloworld", value);
        Assert.Contains("\n", lit.ToString());
    }
}

public class LiteralTokenParseTestScenario : ParseTestScenario<LiteralToken>
{
    public bool ParseVariableRefs { get; set; }
}
