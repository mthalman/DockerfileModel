using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class LineContinuationTokenTests
{
    [Fact]
    public void Create()
    {
        LineContinuationToken token = new();
        Assert.Collection(token.Tokens, new Action<Token>[]
        {
            token => ValidateSymbol(token, '\\'),
            token => ValidateNewLine(token, Environment.NewLine)
        });

        token = new LineContinuationToken('`');
        Assert.Collection(token.Tokens, new Action<Token>[]
        {
            token => ValidateSymbol(token, '`'),
            token => ValidateNewLine(token, Environment.NewLine)
        });
    }

    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '\\')]
    [InlineData("\n", '`')]
    [InlineData("\r\n", '`')]
    public void CreateWithExplicitNewLine(string newline, char escapeChar)
    {
        LineContinuationToken token = new(newline, escapeChar);
        Assert.Equal($"{escapeChar}{newline}", token.ToString());
        Assert.Collection(token.Tokens, new Action<Token>[]
        {
            token => ValidateSymbol(token, escapeChar),
            token => ValidateNewLine(token, newline)
        });
    }

    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '\\')]
    [InlineData("\n", '`')]
    [InlineData("\r\n", '`')]
    public void Parse(string newline, char escapeChar)
    {
        string text = $"{escapeChar}{newline}";
        LineContinuationToken token = LineContinuationToken.Parse(text, escapeChar);
        Assert.Equal(text, token.ToString());
        Assert.Collection(token.Tokens, new Action<Token>[]
        {
            token => ValidateSymbol(token, escapeChar),
            token => ValidateNewLine(token, newline)
        });

        text = $"{escapeChar}  {newline}";
        token = LineContinuationToken.Parse(text, escapeChar);
        Assert.Equal(text, token.ToString());
        Assert.Collection(token.Tokens, new Action<Token>[]
        {
            token => ValidateSymbol(token, escapeChar),
            token => ValidateWhitespace(token, "  "),
            token => ValidateNewLine(token, newline)
        });
    }
}
