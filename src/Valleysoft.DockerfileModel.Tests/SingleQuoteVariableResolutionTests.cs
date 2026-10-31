using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class SingleQuoteVariableResolutionTests
{
    private static readonly Dictionary<string, string?> Variables = new()
    {
        ["VALUE"] = "expanded"
    };

    [Theory]
    [InlineData("'$VALUE'", "'$VALUE'")]
    [InlineData("'${VALUE}'", "'${VALUE}'")]
    [InlineData("'${MISSING:?required}'", "'${MISSING:?required}'")]
    [InlineData("\"$VALUE\"", "\"expanded\"")]
    [InlineData("$VALUE", "expanded")]
    [InlineData("\"'$VALUE'\"", "\"'expanded'\"")]
    [InlineData("'\"$VALUE\"'", "'\"$VALUE\"'")]
    [InlineData("'\\$VALUE'", "'\\$VALUE'")]
    [InlineData("\"\\$VALUE\"", "\"\\$VALUE\"")]
    [InlineData("\\$VALUE", "\\$VALUE")]
    [InlineData("\"${MISSING:-'$VALUE'}\"", "\"'$VALUE'\"")]
    public void Literal_RespectsCurrentQuotes(string text, string expected)
    {
        foreach (bool updateInline in new[] { false, true })
        {
            LiteralToken token = new(text, canContainVariables: true);
            Token[] originalChildren = token.Tokens.ToArray();

            Assert.Equal(expected, token.ResolveVariables('\\', Variables,
                new ResolutionOptions { UpdateInline = updateInline }));
            Assert.Equal(updateInline ? expected : text, token.ToString());
            if (text[0] == '\'' || !updateInline)
            {
                Assert.Equal(originalChildren, token.Tokens);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData('"')]
    public void Literal_QuoteMutationRetainsReferences(char? expandableQuote)
    {
        LiteralToken token = new("'$VALUE'", canContainVariables: true);
        VariableRefToken reference = Assert.IsType<VariableRefToken>(Assert.Single(token.Tokens));
        ResolutionOptions options = new() { UpdateInline = true };

        Assert.Equal("'$VALUE'", token.ResolveVariables('\\', Variables, options));
        Assert.Same(reference, Assert.Single(token.Tokens));

        token.QuoteChar = expandableQuote;
        string expected = $"{expandableQuote}expanded{expandableQuote}";
        Assert.Equal(expected, token.ResolveVariables('\\', Variables, options));
        Assert.Equal(expected, token.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData('"')]
    public void Literal_QuoteAndValueMutationSuppressesNewReferences(char? originalQuote)
    {
        LiteralToken token = new("$VALUE", canContainVariables: true) { QuoteChar = originalQuote };
        token.QuoteChar = '\'';
        token.Value = "${VALUE}-new";
        Token[] children = token.Tokens.ToArray();

        Assert.Equal("'${VALUE}-new'", token.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("'${VALUE}-new'", token.ToString());
        Assert.Equal(children, token.Tokens);

        token.QuoteChar = '"';
        Assert.Equal("\"expanded-new\"", token.ResolveVariables('\\', Variables));
        Assert.Equal("'$VALUE'", new LiteralToken("'$VALUE'").ResolveVariables('\\', Variables));
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('`')]
    public void Literal_FormattingDoesNotMutateSuppressedReferences(char escapeChar)
    {
        string text = $"'prefix{escapeChar}{escapeChar}${{VALUE}}'";
        foreach (bool updateInline in new[] { false, true })
        {
            LiteralToken token = new(text, canContainVariables: true, escapeChar);
            Assert.Equal($"'prefix{escapeChar}${{VALUE}}'", token.ResolveVariables(escapeChar, Variables,
                new ResolutionOptions { UpdateInline = updateInline, RemoveEscapeCharacters = true }));
            Assert.Equal(text, token.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Literal_NestedQuoteFragmentsResolveIndependently(bool updateInline)
    {
        LiteralToken singleQuoted = new("'$VALUE'", canContainVariables: true);
        LiteralToken doubleQuoted = new("\"$VALUE\"", canContainVariables: true);
        LiteralToken token = new(new Token[]
        {
            new StringToken("prefix"),
            singleQuoted,
            doubleQuoted,
            new VariableRefToken("VALUE")
        }, canContainVariables: true, '\\');

        Assert.Equal("prefix'$VALUE'\"expanded\"expanded", token.ResolveVariables('\\', Variables,
            new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("'$VALUE'", singleQuoted.ToString());
        Assert.IsType<VariableRefToken>(Assert.Single(singleQuoted.Tokens));
        Assert.Equal(updateInline ? "prefix'$VALUE'\"expanded\"expanded" : "prefix'$VALUE'\"$VALUE\"$VALUE",
            token.ToString());
    }

    [Theory]
    [InlineData("ENV key='$VALUE'")]
    [InlineData("ENV key '$VALUE'")]
    [InlineData("LABEL key='$VALUE'")]
    [InlineData("LABEL key '$VALUE'")]
    [InlineData("ARG key='$VALUE'")]
    [InlineData("ADD '$VALUE' /destination")]
    [InlineData("COPY '$VALUE' /destination")]
    [InlineData("EXPOSE '$VALUE'")]
    [InlineData("STOPSIGNAL '$VALUE'")]
    [InlineData("USER '$VALUE'")]
    [InlineData("VOLUME '$VALUE'")]
    [InlineData("WORKDIR '$VALUE'")]
    [InlineData("RUN echo '$VALUE'")]
    [InlineData("CMD echo '$VALUE'")]
    [InlineData("ENTRYPOINT echo '$VALUE'")]
    public void Instructions_SingleQuotesAreLiteral(string text)
    {
        foreach (bool updateInline in new[] { false, true })
        {
            Dockerfile dockerfile = Dockerfile.Parse($"FROM scratch\nARG VALUE=expanded\n{text}");
            Instruction instruction = dockerfile.Items.OfType<Instruction>().Last();
            string original = dockerfile.ToString();
            ResolutionOptions options = new() { UpdateInline = updateInline };

            Assert.Equal(text, instruction.ResolveVariables('\\', Variables, options));
            Assert.Equal(text, dockerfile.ResolveVariables(instruction, options: options));
            Assert.Equal(text, dockerfile.ResolveVariables(options: options));
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData("FROM '$VALUE'")]
    [InlineData("FROM \"$VALUE\"")]
    [InlineData("FROM $VALUE")]
    public void From_UsesGlobalArgumentsWithQuotePolicy(string text)
    {
        foreach (bool updateInline in new[] { false, true })
        {
            Dockerfile dockerfile = Dockerfile.Parse($"ARG VALUE=expanded\n{text}");
            string expected = text.Contains('\'') ? text : text.Replace("$VALUE", "expanded");

            Assert.Equal(expected, dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
            Assert.Equal($"ARG VALUE=expanded\n{(updateInline ? expected : text)}", dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData("ENV key=\"$VALUE\"", "ENV key=\"expanded\"")]
    [InlineData("ENV key=$VALUE", "ENV key=expanded")]
    [InlineData("LABEL key=\"$VALUE\"", "LABEL key=\"expanded\"")]
    [InlineData("ARG key=\"$VALUE\"", "ARG key=\"expanded\"")]
    [InlineData("ADD \"$VALUE\" /destination", "ADD \"expanded\" /destination")]
    [InlineData("COPY \"$VALUE\" /destination", "COPY \"expanded\" /destination")]
    [InlineData("EXPOSE \"$VALUE\"", "EXPOSE \"expanded\"")]
    [InlineData("STOPSIGNAL \"$VALUE\"", "STOPSIGNAL \"expanded\"")]
    [InlineData("USER \"$VALUE\"", "USER \"expanded\"")]
    [InlineData("VOLUME \"$VALUE\"", "VOLUME \"expanded\"")]
    [InlineData("WORKDIR \"$VALUE\"", "WORKDIR \"expanded\"")]
    [InlineData("LABEL key 'a $VALUE' \"$VALUE\" $VALUE", "LABEL key 'a $VALUE' \"expanded\" expanded")]
    [InlineData("LABEL key \"a '$VALUE'\" $VALUE", "LABEL key \"a 'expanded'\" expanded")]
    [InlineData("LABEL key \\\"'$VALUE'\\\" $VALUE", "LABEL key \\\"'$VALUE'\\\" expanded")]
    public void Instructions_ExpandOutsideSingleQuotes(string text, string expected)
    {
        foreach (bool updateInline in new[] { false, true })
        {
            Dockerfile dockerfile = Dockerfile.Parse($"FROM scratch\nARG VALUE=expanded\n{text}");
            Instruction instruction = dockerfile.Items.OfType<Instruction>().Last();

            Assert.Equal(expected, instruction.ResolveVariables('\\', Variables));
            Assert.Equal(text, instruction.ToString());
            Assert.Equal(expected, dockerfile.ResolveVariables(instruction,
                options: new ResolutionOptions { UpdateInline = updateInline }));
            Assert.Equal(updateInline ? expected : text, instruction.ToString());
        }
    }
}
