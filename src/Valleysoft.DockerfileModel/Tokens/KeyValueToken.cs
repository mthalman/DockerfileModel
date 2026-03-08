using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel.Tokens;

public class KeyValueToken<TKey, TValue> : AggregateToken, IKeyValuePair
    where TKey : Token, IValueToken
    where TValue : Token
{
    public const char DefaultSeparator = '=';

    public KeyValueToken(TKey key, TValue value, bool isFlag = false, char separator = DefaultSeparator)
        : this(
            ConcatTokens(
                isFlag ? new Token[] { new SymbolToken('-'), new SymbolToken('-') } : Enumerable.Empty<Token>(),
                new Token[]
                {
                    key,
                    Char.IsWhiteSpace(separator) ? new WhitespaceToken(separator.ToString()) : new SymbolToken(separator),
                    value
                }))
    {
    }

    internal KeyValueToken(IEnumerable<Token> tokens)
        : base(tokens)
    {
    }

    public string Key
    {
        get => KeyToken.Value;
        set
        {
            Requires.NotNull(value, nameof(value));
            KeyToken.Value = value;
        }
    }

    public TKey KeyToken
    {
        get => Tokens.OfType<TKey>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(KeyToken, value);
        }
    }

    string? IKeyValuePair.Value
    {
        get => Value;
        set => Value = value!;
    }

    public string Value
    {
        get
        {
            TValue? token = Tokens.After(KeyToken).OfType<TValue>().FirstOrDefault();
            return token is null ? string.Empty : token.ToString(TokenStringOptions.CreateOptionsForValueString());
        }
        set
        {
            Requires.NotNull(value, nameof(value));

            TValue? existingToken = Tokens.After(KeyToken).OfType<TValue>().FirstOrDefault();
            if (existingToken is IValueToken valueToken)
            {
                valueToken.Value = value;
            }
            else if (existingToken is null)
            {
                throw new InvalidOperationException(
                    $"No value token exists. Use the {nameof(ValueToken)} setter to insert a new value token.");
            }
            else
            {
                throw new NotSupportedException($"Setting the value is not supported for values of type '{typeof(TValue)}'.");
            }
        }
    }

    public TValue? ValueToken
    {
        get => Tokens.After(KeyToken).OfType<TValue>().FirstOrDefault();
        set
        {
            TValue? currentToken = Tokens.After(KeyToken).OfType<TValue>().FirstOrDefault();
            SetToken(currentToken, value,
                addToken: token =>
                {
                    // Insert the value token after the separator (SymbolToken for '=')
                    Token? separator = Tokens.After(KeyToken).OfType<SymbolToken>().FirstOrDefault();
                    if (separator is not null)
                    {
                        int separatorIndex = TokenList.IndexOf(separator);
                        TokenList.Insert(separatorIndex + 1, token);
                    }
                    else
                    {
                        TokenList.Add(token);
                    }
                });
        }
    }

    // Breaking change: the optionalValue parameter was added intentionally, changing this public method's signature.
    public static KeyValueToken<TKey, TValue> Parse(string text, Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
        char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar, bool excludeLeadingWhitespaceInValue = false,
        bool excludeTrailingWhitespaceInSeparator = false, bool optionalValue = false) =>
        Parse(text, keyTokenParser, valueTokenParser, tokens => new KeyValueToken<TKey, TValue>(tokens), separator, escapeChar,
            excludeLeadingWhitespaceInValue: excludeLeadingWhitespaceInValue,
            excludeTrailingWhitespaceInSeparator: excludeTrailingWhitespaceInSeparator,
            optionalValue: optionalValue);

    // Breaking change: the optionalValue parameter was added intentionally, changing this public method's signature.
    public static Parser<KeyValueToken<TKey, TValue>> GetParser(
        Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
        char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar, bool excludeLeadingWhitespaceInValue = false,
        bool excludeTrailingWhitespaceInSeparator = false, bool optionalValue = false) =>
        GetParser(keyTokenParser, valueTokenParser, tokens => new KeyValueToken<TKey, TValue>(tokens), separator, escapeChar,
            excludeLeadingWhitespaceInValue: excludeLeadingWhitespaceInValue,
            excludeTrailingWhitespaceInSeparator: excludeTrailingWhitespaceInSeparator,
            optionalValue: optionalValue);

    protected static T Parse<T>(string text, Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
        Func<IEnumerable<Token>, T> createToken, char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar,
        bool excludeLeadingWhitespaceInValue = false, bool excludeTrailingWhitespaceInSeparator = false, bool optionalValue = false)
        where T : KeyValueToken<TKey, TValue> =>
        createToken(GetTokens(text, GetInnerParser(separator, keyTokenParser, valueTokenParser, escapeChar,
            excludeLeadingWhitespaceInValue: excludeLeadingWhitespaceInValue,
            excludeTrailingWhitespaceInSeparator: excludeTrailingWhitespaceInSeparator,
            optionalValue: optionalValue)));

    protected static Parser<T> GetParser<T>(
        Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser, Func<IEnumerable<Token>, T> createToken,
        char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar, bool excludeLeadingWhitespaceInValue = false,
        bool excludeTrailingWhitespaceInSeparator = false, bool optionalValue = false)
        where T : KeyValueToken<TKey, TValue> =>
        from tokens in GetInnerParser(separator, keyTokenParser, valueTokenParser, escapeChar,
            excludeLeadingWhitespaceInValue: excludeLeadingWhitespaceInValue,
            excludeTrailingWhitespaceInSeparator: excludeTrailingWhitespaceInSeparator,
            optionalValue: optionalValue)
        select createToken(tokens);

    // The two branches of the optionalValue conditional share the same flag/keyword/separatorToken
    // parsing pipeline but differ in whether the value is required or optional. Sprache's LINQ query
    // syntax does not allow factoring out the common prefix without losing the required-vs-optional
    // distinction on the value parser, so the duplication is intentional for clarity.
    private static Parser<IEnumerable<Token>> GetInnerParser(char separator, Parser<TKey> keyTokenParser,
        Parser<TValue> valueTokenParser, char escapeChar, bool excludeLeadingWhitespaceInValue, bool excludeTrailingWhitespaceInSeparator,
        bool optionalValue = false) =>
        optionalValue
            ? from flag in ArgTokens(FlagParser(escapeChar), escapeChar).Optional()
              from keyword in ArgTokens(keyTokenParser.AsEnumerable(), escapeChar)
              from separatorToken in ArgTokens(SeparatorParser(separator).AsEnumerable().FilterNulls(), escapeChar, excludeTrailingWhitespaceInSeparator)
              from value in ArgTokens(valueTokenParser.AsEnumerable(), escapeChar, excludeTrailingWhitespace: true, excludeLeadingWhitespaceInValue).Optional()
              select ConcatTokens(flag.GetOrDefault(), keyword, separatorToken, value.GetOrDefault())
            : from flag in ArgTokens(FlagParser(escapeChar), escapeChar).Optional()
              from keyword in ArgTokens(keyTokenParser.AsEnumerable(), escapeChar)
              from separatorToken in ArgTokens(SeparatorParser(separator).AsEnumerable().FilterNulls(), escapeChar, excludeTrailingWhitespaceInSeparator)
              from value in ArgTokens(valueTokenParser.AsEnumerable(), escapeChar, excludeTrailingWhitespace: true, excludeLeadingWhitespaceInValue)
              select ConcatTokens(flag.GetOrDefault(), keyword, separatorToken, value);

    private static Parser<IEnumerable<Token>> FlagParser(char escapeChar) =>
        ArgTokens(Symbol('-').AsEnumerable(), escapeChar).Repeat(2).Flatten();

    private static Parser<Token?> SeparatorParser(char separator) =>
        Char.IsWhiteSpace(separator) ?
            Sprache.Parse.Return<Token?>(null) :
            Symbol(separator).Cast<SymbolToken, Token>();
}
