
namespace Valleysoft.DockerfileModel.Tokens;

/// <summary>A literal with optional quote syntax, continuations, and recognized variable-reference children.</summary>
public class LiteralToken : AggregateToken, IQuotableValueToken
{
    private readonly bool canContainVariables;
    private readonly char escapeChar;
    private readonly bool preserveRawValue;
    private char? quoteChar;

    /// <summary>Parses literal syntax using the supplied escape character and variable-recognition policy.</summary>
    /// <remarks>The escape context and variable policy are retained for later value updates.</remarks>
    public LiteralToken(string value, bool canContainVariables = false, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(value, canContainVariables, escapeChar), canContainVariables, escapeChar)
    {
    }

    private LiteralToken((IEnumerable<Token> Tokens, char? QuoteChar) tokensInfo, bool canContainVariables, char escapeChar)
        : this(tokensInfo.Tokens, canContainVariables, escapeChar)
    {
        QuoteChar = tokensInfo.QuoteChar;
    }

    internal LiteralToken(IEnumerable<Token> tokens, bool canContainVariables, char escapeChar, bool preserveRawValue = false)
        : base(tokens)
    {
        this.canContainVariables = canContainVariables;
        this.escapeChar = escapeChar;
        EditingEscapeChar = escapeChar;
        this.preserveRawValue = preserveRawValue;
    }

    /// <summary>Gets text without quote wrappers, continuations, comments, or newlines; sets the literal's inner contents.</summary>
    /// <remarks>
    /// Setting preserves the existing <see cref="QuoteChar"/> and, except for raw-value literals,
    /// reparses the contents with the retained context.
    /// The value is not a general JSON-unescaped or shell-expanded string. Use <c>ToString()</c> for complete syntax.
    /// </remarks>
    public string Value
    {
        get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
        set
        {
            Guard.NotNull(value, nameof(value));
            ReplaceWithTokens(GetInnerTokens(value));
        }
    }

    /// <summary>Gets or sets the surrounding quote character, or null for no quote wrapper.</summary>
    public virtual char? QuoteChar
    {
        get => quoteChar;
        set => quoteChar = value;
    }

    protected virtual IEnumerable<Token> GetInnerTokens(string value) =>
        preserveRawValue ? new Token[] { new StringToken(value) } : GetTokens(value, canContainVariables, escapeChar).Tokens;

    private static (IEnumerable<Token> Tokens, char? QuoteChar) GetTokens(string value, bool canContainVariables, char escapeChar)
    {
        Guard.NotNull(value, nameof(value));

        if (value == string.Empty)
        {
            return (new Token[] { new StringToken(value) }, null);
        }

        Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> parser;
        if (canContainVariables)
        {
            parser = LiteralWithVariablesTokens(escapeChar, whitespaceMode: WhitespaceMode.Allowed);
        }
        else
        {
            parser = WrappedInOptionalQuotesLiteralStringWithSpaces(escapeChar, excludeVariableRefChars: false);
        }

        return parser.Parse(value);
    }
}
