namespace Valleysoft.DockerfileModel.Tokens;

public abstract class IdentifierToken : AggregateToken, IQuotableValueToken
{
    protected IdentifierToken(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public string Value
    {
        get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
        set
        {
            Guard.NotNullOrEmpty(value, nameof(value));
            ReplaceWithTokens(GetInnerTokens(value));
        }
    }

    private char? quoteChar;

    public char? QuoteChar
    {
        get => quoteChar;
        set
        {
            ResolutionGuard.BeforeMutation(this);
            quoteChar = value;
        }
    }

    protected abstract IEnumerable<Token> GetInnerTokens(string value);

    protected static (IEnumerable<Token> Tokens, char? QuoteChar) GetTokens(string value,
        TextParser<(IEnumerable<Token> Token, char? QuoteChar)> parser)
    {
        Guard.NotNull(value, nameof(value));
        return parser.Parse(value);
    }
}
