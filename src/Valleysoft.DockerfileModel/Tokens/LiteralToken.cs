
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
    /// For an unwrapped mixed word, assigning its current value preserves fragment syntax;
    /// a replacement containing whitespace receives a quote wrapper to retain one operand.
    /// The value is not a general JSON-unescaped or shell-expanded string. Use <c>ToString()</c> for complete syntax.
    /// </remarks>
    public string Value
    {
        get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
        set
        {
            Guard.NotNull(value, nameof(value));
            bool mixedWord = QuoteChar is null && Tokens.OfType<LiteralToken>().Any();
            if (mixedWord && value == Value)
            {
                return;
            }
            Token[] contents = GetInnerTokens(value).ToArray();
            char? replacementQuote = QuoteChar;
            if (mixedWord && value.Any(char.IsWhiteSpace))
            {
                LiteralToken replacement = new(contents, canContainVariables, escapeChar);
                foreach (char quote in new[] { '"', '\'' })
                {
                    replacement.QuoteChar = quote;
                    Result<LiteralToken> parsed = LiteralWithVariables(escapeChar,
                        whitespaceMode: WhitespaceMode.AllowedInQuotes).AtEnd().TryParse(replacement.ToString());
                    if (parsed.HasValue && parsed.Value.Value == replacement.Value)
                    {
                        replacementQuote = quote;
                        break;
                    }
                }
                if (replacementQuote is null)
                {
                    throw new ArgumentException("The value cannot be quoted as one operand without changing its contents.", nameof(value));
                }
            }
            ReplaceWithTokens(contents);
            QuoteChar = replacementQuote;
        }
    }

    /// <summary>Gets or sets the surrounding quote character, or null for no quote wrapper.</summary>
    /// <remarks>
    /// Changing a mixed word's quote unwraps its fragment quotes while retaining variable-reference children,
    /// adding braces where a removed wrapper delimited a variable name.
    /// Assigning the existing quote preserves the original fragment syntax.
    /// </remarks>
    public virtual char? QuoteChar
    {
        get => quoteChar;
        set
        {
            if (quoteChar != value && Tokens.OfType<LiteralToken>().Any())
            {
                ReplaceWithTokens(StringParsers.UnwrapLiteralFragments(Tokens,
                    aggregate => aggregate.TokenList, variable => variable.TokenList));
            }
            quoteChar = value;
        }
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

        TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)> parser;
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
