namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class EnvEscapedQuoteLiteralToken : LiteralToken
{
    public EnvEscapedQuoteLiteralToken(IEnumerable<Token> tokens, char escapeChar)
        : base(tokens, canContainVariables: true, escapeChar)
    {
    }
}
