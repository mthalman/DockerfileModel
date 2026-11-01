namespace Valleysoft.DockerfileModel.Tokens;

/// <summary>A literal with optional quote syntax, continuations, and recognized variable-reference children.</summary>
public class LiteralToken : AggregateToken, IQuotableValueToken
{
    private readonly bool canContainVariables;
    private readonly char escapeChar;
    private readonly bool preserveRawValue;
    private TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)>? wordParser;
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

    internal LiteralToken(IEnumerable<Token> tokens, bool canContainVariables, char escapeChar, bool preserveRawValue = false,
        TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)>? wordParser = null, bool useJsonStringSyntax = false)
        : base(tokens)
    {
        this.canContainVariables = canContainVariables;
        this.escapeChar = escapeChar;
        EditingEscapeChar = escapeChar;
        this.preserveRawValue = preserveRawValue;
        this.wordParser = wordParser;
        UseJsonStringSyntax = useJsonStringSyntax;
    }

    internal bool UseJsonStringSyntax { get; set; }
    internal bool HasWordContext => wordParser is not null;

    /// <summary>Gets text without quote wrappers, continuations, comments, or newlines; sets the literal's inner contents.</summary>
    /// <remarks>
    /// Setting preserves an existing quote wrapper and, except for raw-value literals,
    /// reparses the contents with the retained context.
    /// For an unwrapped parsed or structurally adopted word, assigning its current value preserves fragment syntax;
    /// replacements receive a quote wrapper when required to retain one operand and its contents.
    /// The value is not a general JSON-unescaped or shell-expanded string. Use <c>ToString()</c> for complete syntax.
    /// </remarks>
    public string Value
    {
        get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
        set
        {
            Guard.NotNull(value, nameof(value));
            if (wordParser is not null && QuoteChar is null && value == Value)
            {
                return;
            }
            Token[] contents = GetInnerTokens(value).ToArray();
            if (wordParser is not null && QuoteChar is null)
            {
                string expectedValue = new LiteralToken(contents, canContainVariables, escapeChar).Value;
                foreach (char? quote in new char?[] { null, '"', '\'' })
                {
                    string candidate = $"{quote}{string.Concat(contents.Select(token => token.ToString()))}{quote}";
                    Result<(IEnumerable<Token> Tokens, char? QuoteChar)> parsed = wordParser.TryParse(candidate);
                    if (parsed.HasValue &&
                        new LiteralToken(parsed.Value.Tokens, canContainVariables, escapeChar).Value == expectedValue)
                    {
                        ReplaceWithTokens(contents);
                        QuoteChar = quote;
                        return;
                    }
                }
                throw new ArgumentException("The value cannot be serialized as one operand without changing its contents.", nameof(value));
            }
            ReplaceWithTokens(contents);
        }
    }

    internal void RetainWordContext(LiteralToken parsed) => wordParser = parsed.wordParser;

    /// <summary>Gets or sets the surrounding quote character, or null for no quote wrapper.</summary>
    /// <remarks>
    /// Changing a parsed or adopted mixed word's quote unwraps its fragment quotes while retaining variable-reference children,
    /// adding braces where a removed wrapper delimited a variable name.
    /// Assigning the existing quote preserves the original fragment syntax.
    /// Single quotes suppress variable resolution outside an enclosing double-quoted context.
    /// Reference children are retained so later quote changes can re-enable resolution without reparsing the value.
    /// </remarks>
    public virtual char? QuoteChar
    {
        get => quoteChar;
        set
        {
            if (HasWordContext && quoteChar != value && Tokens.OfType<LiteralToken>().Any())
            {
                ReplaceWithTokens(StringParsers.UnwrapLiteralFragments(Tokens,
                    aggregate => aggregate.TokenList, variable => variable.TokenList));
            }
            quoteChar = value;
        }
    }

    protected virtual IEnumerable<Token> GetInnerTokens(string value)
    {
        if (preserveRawValue)
        {
            return new Token[] { new StringToken(value) };
        }
        if (UseJsonStringSyntax)
        {
            IEnumerable<Token> tokens = CommandParsers.JsonStringTokens(escapeChar, canContainVariables)
                .Try().Many().Flatten().AtEnd().Parse(value);
            return TokenHelper.CollapseTokens(tokens,
                token => token is StringToken || token.GetType() == typeof(WhitespaceToken),
                text => new StringToken(text));
        }
        return GetTokens(value, canContainVariables, escapeChar).Tokens;
    }

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
