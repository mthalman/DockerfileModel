namespace Valleysoft.DockerfileModel.Tokens;

/// <summary>
/// Identifier token representing a heredoc delimiter name (e.g. "EOF", "SCRIPT").
/// </summary>
public class HeredocDelimiterToken : IdentifierToken
{
    internal HeredocDelimiterToken(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public HeredocDelimiterToken(string value) : base(GetTokens(value))
    {
    }

    protected override IEnumerable<Token> GetInnerTokens(string value) =>
        GetTokens(value);

    private static IEnumerable<Token> GetTokens(string value)
    {
        Requires.NotNullOrEmpty(value, nameof(value));
        return new Token[] { new StringToken(value) };
    }
}
