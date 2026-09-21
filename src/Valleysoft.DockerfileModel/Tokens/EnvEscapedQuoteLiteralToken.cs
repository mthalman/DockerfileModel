namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class EnvEscapedQuoteLiteralToken : LiteralToken
{
    public EnvEscapedQuoteLiteralToken(IEnumerable<Token> tokens, char escapeChar, char? quoteChar = null)
        : base(tokens, canContainVariables: true, escapeChar)
    {
        base.QuoteChar = quoteChar;
    }

    internal bool HasOriginalEscapedQuoteSyntax { get; private set; } = true;

    public override char? QuoteChar
    {
        get => base.QuoteChar;
        set
        {
            if (value != base.QuoteChar)
            {
                HasOriginalEscapedQuoteSyntax = false;
            }

            base.QuoteChar = value;
        }
    }

    protected override IEnumerable<Token> GetInnerTokens(string value)
    {
        HasOriginalEscapedQuoteSyntax = false;
        return base.GetInnerTokens(value);
    }
}
