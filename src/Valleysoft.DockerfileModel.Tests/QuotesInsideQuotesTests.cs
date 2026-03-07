using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Regression tests for GitHub issue #125: LABEL parsing fails when single quotes
/// appear inside double-quoted values (and vice versa). The parser should only exclude
/// the wrapping quote character from allowed characters inside quoted strings, not both
/// quote types.
/// </summary>
public class QuotesInsideQuotesTests
{
    #region LABEL instruction tests

    /// <summary>
    /// Core bug case from issue #125: single quote inside double-quoted LABEL value.
    /// </summary>
    [Fact]
    public void Label_SingleQuoteInsideDoubleQuotedValue()
    {
        string text = "LABEL a=\"foo'bar\"";
        LabelInstruction result = LabelInstruction.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // Token structure
        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "LABEL"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "a=\"foo'bar\"",
                token => ValidateIdentifier<LabelKeyToken>(token, "a"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "foo'bar", '\"')));

        // Semantic validation
        Assert.Single(result.Labels);
        Assert.Equal("a", result.Labels[0].Key);
        Assert.Equal("foo'bar", result.Labels[0].Value);
    }

    /// <summary>
    /// Reverse case: double quote inside single-quoted LABEL value.
    /// </summary>
    [Fact]
    public void Label_DoubleQuoteInsideSingleQuotedValue()
    {
        string text = "LABEL a='foo\"bar'";
        LabelInstruction result = LabelInstruction.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // Token structure
        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "LABEL"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "a='foo\"bar'",
                token => ValidateIdentifier<LabelKeyToken>(token, "a"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "foo\"bar", '\'')));

        // Semantic validation
        Assert.Single(result.Labels);
        Assert.Equal("a", result.Labels[0].Key);
        Assert.Equal("foo\"bar", result.Labels[0].Value);
    }

    /// <summary>
    /// Real-world case from issue #125: Azure CLI LABEL with apostrophe in description.
    /// </summary>
    [Fact]
    public void Label_AzureCliRealWorldCase()
    {
        string text = "LABEL maintainer=\"Microsoft\" org.label-schema.description=\"A great cloud needs great tools; we're excited to introduce Azure CLI, our next generation multi-platform command line experience for Azure.\"";
        LabelInstruction result = LabelInstruction.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // Semantic validation
        Assert.Equal(2, result.Labels.Count);
        Assert.Equal("maintainer", result.Labels[0].Key);
        Assert.Equal("Microsoft", result.Labels[0].Value);
        Assert.Equal("org.label-schema.description", result.Labels[1].Key);
        Assert.Equal("A great cloud needs great tools; we're excited to introduce Azure CLI, our next generation multi-platform command line experience for Azure.", result.Labels[1].Value);
    }

    /// <summary>
    /// Multiple values with mixed quote styles containing the opposite quote character.
    /// </summary>
    [Fact]
    public void Label_MultipleValuesWithMixedQuotes()
    {
        string text = "LABEL a=\"it's\" b='say \"hello\"'";
        LabelInstruction result = LabelInstruction.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // Token structure
        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "LABEL"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "a=\"it's\"",
                token => ValidateIdentifier<LabelKeyToken>(token, "a"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "it's", '\"')),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<KeyValueToken<LabelKeyToken, LiteralToken>>(token, "b='say \"hello\"'",
                token => ValidateIdentifier<LabelKeyToken>(token, "b"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "say \"hello\"", '\'')));

        // Semantic validation
        Assert.Equal(2, result.Labels.Count);
        Assert.Equal("a", result.Labels[0].Key);
        Assert.Equal("it's", result.Labels[0].Value);
        Assert.Equal("b", result.Labels[1].Key);
        Assert.Equal("say \"hello\"", result.Labels[1].Value);
    }

    #endregion

    #region ENV instruction tests

    /// <summary>
    /// ENV with single quote inside double-quoted value.
    /// </summary>
    [Fact]
    public void Env_SingleQuoteInsideDoubleQuotedValue()
    {
        string text = "ENV MY_VAR=\"it's a test\"";
        EnvInstruction result = EnvInstruction.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // Token structure
        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "ENV"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_VAR=\"it's a test\"",
                token => ValidateIdentifier<Variable>(token, "MY_VAR"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "it's a test", '\"')));

        // Semantic validation
        Assert.Single(result.Variables);
        Assert.Equal("MY_VAR", result.Variables[0].Key);
        Assert.Equal("it's a test", result.Variables[0].Value);
    }

    /// <summary>
    /// ENV with double quote inside single-quoted value.
    /// </summary>
    [Fact]
    public void Env_DoubleQuoteInsideSingleQuotedValue()
    {
        string text = "ENV MY_VAR='say \"hello\"'";
        EnvInstruction result = EnvInstruction.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // Token structure
        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "ENV"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<KeyValueToken<Variable, LiteralToken>>(token, "MY_VAR='say \"hello\"'",
                token => ValidateIdentifier<Variable>(token, "MY_VAR"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "say \"hello\"", '\'')));

        // Semantic validation
        Assert.Single(result.Variables);
        Assert.Equal("MY_VAR", result.Variables[0].Key);
        Assert.Equal("say \"hello\"", result.Variables[0].Value);
    }

    #endregion

    #region ARG instruction tests

    /// <summary>
    /// ARG with single quote inside double-quoted default value.
    /// </summary>
    [Fact]
    public void Arg_SingleQuoteInsideDoubleQuotedValue()
    {
        string text = "ARG MY_ARG=\"it's default\"";
        ArgInstruction result = ArgInstruction.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // Token structure
        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "ARG"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<ArgDeclaration>(token, "MY_ARG=\"it's default\"",
                token => ValidateIdentifier<Variable>(token, "MY_ARG"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "it's default", '\"')));

        // Semantic validation
        Assert.Single(result.Args);
        Assert.Equal("MY_ARG", result.Args[0].Key);
        Assert.Equal("it's default", result.Args[0].Value);
    }

    /// <summary>
    /// ARG with double quote inside single-quoted default value.
    /// </summary>
    [Fact]
    public void Arg_DoubleQuoteInsideSingleQuotedValue()
    {
        string text = "ARG MY_ARG='say \"hello\"'";
        ArgInstruction result = ArgInstruction.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // Token structure
        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "ARG"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<ArgDeclaration>(token, "MY_ARG='say \"hello\"'",
                token => ValidateIdentifier<Variable>(token, "MY_ARG"),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, "say \"hello\"", '\'')));

        // Semantic validation
        Assert.Single(result.Args);
        Assert.Equal("MY_ARG", result.Args[0].Key);
        Assert.Equal("say \"hello\"", result.Args[0].Value);
    }

    #endregion

    #region Dockerfile-level round-trip tests

    /// <summary>
    /// Full Dockerfile round-trip with LABEL containing quotes-inside-quotes, verifying
    /// that Dockerfile.Parse() followed by ToString() preserves the exact input.
    /// </summary>
    [Fact]
    public void Dockerfile_RoundTrip_LabelWithQuotesInsideQuotes()
    {
        string dockerfileContent = TestHelper.ConcatLines(new List<string>
        {
            "FROM scratch",
            "LABEL maintainer=\"Microsoft\" org.label-schema.description=\"A great cloud needs great tools; we're excited to introduce Azure CLI, our next generation multi-platform command line experience for Azure.\""
        });

        Dockerfile dockerfile = Dockerfile.Parse(dockerfileContent);

        // Round-trip fidelity at Dockerfile level
        Assert.Equal(dockerfileContent, dockerfile.ToString());

        // Verify the LABEL instruction was correctly parsed
        LabelInstruction labelInstruction = dockerfile.Items.OfType<LabelInstruction>().First();
        Assert.Equal(2, labelInstruction.Labels.Count);
        Assert.Equal("maintainer", labelInstruction.Labels[0].Key);
        Assert.Equal("Microsoft", labelInstruction.Labels[0].Value);
        Assert.Equal("org.label-schema.description", labelInstruction.Labels[1].Key);
        Assert.Equal("A great cloud needs great tools; we're excited to introduce Azure CLI, our next generation multi-platform command line experience for Azure.", labelInstruction.Labels[1].Value);
    }

    /// <summary>
    /// Full Dockerfile round-trip with multiple instructions using mixed quotes-inside-quotes.
    /// </summary>
    [Fact]
    public void Dockerfile_RoundTrip_MixedInstructionsWithQuotesInsideQuotes()
    {
        string dockerfileContent = TestHelper.ConcatLines(new List<string>
        {
            "FROM scratch",
            "ARG DESC=\"it's a test\"",
            "ENV GREETING='say \"hello\"'",
            "LABEL description=\"it's great\" notes='he said \"yes\"'"
        });

        Dockerfile dockerfile = Dockerfile.Parse(dockerfileContent);

        // Round-trip fidelity at Dockerfile level
        Assert.Equal(dockerfileContent, dockerfile.ToString());
    }

    #endregion

    #region Edge cases

    /// <summary>
    /// Multiple single quotes inside a double-quoted value.
    /// </summary>
    [Fact]
    public void Label_MultipleSingleQuotesInsideDoubleQuotedValue()
    {
        string text = "LABEL msg=\"it's John's dog's toy\"";
        LabelInstruction result = LabelInstruction.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // Semantic validation
        Assert.Single(result.Labels);
        Assert.Equal("msg", result.Labels[0].Key);
        Assert.Equal("it's John's dog's toy", result.Labels[0].Value);
    }

    /// <summary>
    /// Multiple double quotes inside a single-quoted value.
    /// </summary>
    [Fact]
    public void Label_MultipleDoubleQuotesInsideSingleQuotedValue()
    {
        string text = "LABEL msg='he said \"hi\" then \"bye\"'";
        LabelInstruction result = LabelInstruction.Parse(text);

        // Round-trip fidelity
        Assert.Equal(text, result.ToString());

        // Semantic validation
        Assert.Single(result.Labels);
        Assert.Equal("msg", result.Labels[0].Key);
        Assert.Equal("he said \"hi\" then \"bye\"", result.Labels[0].Value);
    }

    #endregion
}
