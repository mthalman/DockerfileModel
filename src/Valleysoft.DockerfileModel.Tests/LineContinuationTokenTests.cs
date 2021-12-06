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

        token = new LineContinuationToken("\n", '`');
        Assert.Collection(token.Tokens, new Action<Token>[]
        {
            token => ValidateSymbol(token, '`'),
            token => ValidateNewLine(token, "\n")
        });
    }

    [Fact]
    public void Parse()
    {
        LineContinuationToken token = LineContinuationToken.Parse("\\\n");
        Assert.Equal("\\\n", token.ToString());
        Assert.Collection(token.Tokens, new Action<Token>[]
        {
            token => ValidateSymbol(token, '\\'),
            token => ValidateNewLine(token, "\n")
        });

        token = LineContinuationToken.Parse("`  \n", '`');
        Assert.Equal("`  \n", token.ToString());
        Assert.Collection(token.Tokens, new Action<Token>[]
        {
            token => ValidateSymbol(token, '`'),
            token => ValidateWhitespace(token, "  "),
            token => ValidateNewLine(token, "\n")
        });
    }
}
