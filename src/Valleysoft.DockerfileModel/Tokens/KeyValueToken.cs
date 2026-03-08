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
        get
        {
            // When there is no separator token after the key (e.g., boolean flags like --link),
            // the key-value pair has no value at all, so return null.
            // When a separator is present but the value token is absent (e.g., ENV key=),
            // the Value property returns string.Empty, which is the correct semantic.
            bool hasSeparator = Tokens.After(KeyToken).Any(t => t is SymbolToken or WhitespaceToken);
            if (!hasSeparator && ValueToken is null)
            {
                return null;
            }

            return Value;
        }
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
                // When no value token exists, setting to empty string is a no-op
                // since the getter already returns string.Empty in that case.
                if (value == string.Empty)
                {
                    return;
                }

                // When TValue is LiteralToken, auto-insert a new LiteralToken so that
                // mutation paths (e.g. IKeyValuePair.Value setter on ENV/LABEL instructions
                // parsed with optionalValue: true) work without requiring callers to first
                // insert a ValueToken manually.
                if (typeof(TValue) == typeof(LiteralToken))
                {
                    // Limitation: This LiteralToken is constructed with the default escape char
                    // because the original instruction's escapeChar is not available here. This is
                    // correct for the vast majority of Dockerfiles (which use backslash). A full
                    // fix would require threading escapeChar through the property chain, which is
                    // a larger refactor.
                    ValueToken = (TValue)(Token)new LiteralToken(value, canContainVariables: true);
                    return;
                }

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
                },
                removeToken: token =>
                {
                    // Remove any whitespace, line continuation, and newline tokens between the separator and the value token.
                    // Line continuations (e.g., backslash + newline) can appear between the separator and value when
                    // the instruction spans multiple lines. Leaving them behind would break round-tripping.
                    Token? separator = Tokens.After(KeyToken).OfType<SymbolToken>().FirstOrDefault();
                    int startIndex = separator is not null ? TokenList.IndexOf(separator) + 1 : TokenList.IndexOf(KeyToken) + 1;
                    int endIndex = TokenList.IndexOf(token);
                    for (int i = endIndex - 1; i >= startIndex; i--)
                    {
                        if (TokenList[i] is WhitespaceToken or LineContinuationToken)
                        {
                            TokenList.RemoveAt(i);
                        }
                    }
                    TokenList.Remove(token);
                });
        }
    }

    // Breaking change: the optionalValue parameter was added intentionally, changing this public method's signature.
    public static KeyValueToken<TKey, TValue> Parse(string text, Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
        char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar, bool excludeLeadingWhitespaceInValue = false,
        bool excludeTrailingWhitespaceInSeparator = false, bool optionalValue = false, bool optionalSeparator = false) =>
        Parse(text, keyTokenParser, valueTokenParser, tokens => new KeyValueToken<TKey, TValue>(tokens), separator, escapeChar,
            excludeLeadingWhitespaceInValue: excludeLeadingWhitespaceInValue,
            excludeTrailingWhitespaceInSeparator: excludeTrailingWhitespaceInSeparator,
            optionalValue: optionalValue,
            optionalSeparator: optionalSeparator);

    // Breaking change: the optionalValue parameter was added intentionally, changing this public method's signature.
    public static Parser<KeyValueToken<TKey, TValue>> GetParser(
        Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
        char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar, bool excludeLeadingWhitespaceInValue = false,
        bool excludeTrailingWhitespaceInSeparator = false, bool optionalValue = false, bool optionalSeparator = false) =>
        GetParser(keyTokenParser, valueTokenParser, tokens => new KeyValueToken<TKey, TValue>(tokens), separator, escapeChar,
            excludeLeadingWhitespaceInValue: excludeLeadingWhitespaceInValue,
            excludeTrailingWhitespaceInSeparator: excludeTrailingWhitespaceInSeparator,
            optionalValue: optionalValue,
            optionalSeparator: optionalSeparator);

    protected static T Parse<T>(string text, Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
        Func<IEnumerable<Token>, T> createToken, char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar,
        bool excludeLeadingWhitespaceInValue = false, bool excludeTrailingWhitespaceInSeparator = false, bool optionalValue = false,
        bool optionalSeparator = false)
        where T : KeyValueToken<TKey, TValue> =>
        createToken(GetTokens(text, GetInnerParser(separator, keyTokenParser, valueTokenParser, escapeChar,
            excludeLeadingWhitespaceInValue: excludeLeadingWhitespaceInValue,
            excludeTrailingWhitespaceInSeparator: excludeTrailingWhitespaceInSeparator,
            optionalValue: optionalValue,
            optionalSeparator: optionalSeparator)));

    protected static Parser<T> GetParser<T>(
        Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser, Func<IEnumerable<Token>, T> createToken,
        char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar, bool excludeLeadingWhitespaceInValue = false,
        bool excludeTrailingWhitespaceInSeparator = false, bool optionalValue = false, bool optionalSeparator = false)
        where T : KeyValueToken<TKey, TValue> =>
        from tokens in GetInnerParser(separator, keyTokenParser, valueTokenParser, escapeChar,
            excludeLeadingWhitespaceInValue: excludeLeadingWhitespaceInValue,
            excludeTrailingWhitespaceInSeparator: excludeTrailingWhitespaceInSeparator,
            optionalValue: optionalValue,
            optionalSeparator: optionalSeparator)
        select createToken(tokens);

    // The branches share the same flag/keyword parsing pipeline but differ in whether separator
    // and/or value are required or optional. Sprache's LINQ query syntax does not allow factoring
    // out the common prefix without losing the required-vs-optional distinction, so the duplication
    // is intentional for clarity.
    //
    // When optionalSeparator is true, both the separator and value are optional (if there is no
    // separator, there can be no value). This supports boolean flags like --link that have a key
    // but no separator or value.
    private static Parser<IEnumerable<Token>> GetInnerParser(char separator, Parser<TKey> keyTokenParser,
        Parser<TValue> valueTokenParser, char escapeChar, bool excludeLeadingWhitespaceInValue, bool excludeTrailingWhitespaceInSeparator,
        bool optionalValue = false, bool optionalSeparator = false) =>
        optionalSeparator
            ? from flag in ArgTokens(FlagParser(escapeChar), escapeChar).Optional()
              from keyword in ArgTokens(keyTokenParser.AsEnumerable(), escapeChar)
              from separatorAndValue in (
                  from separatorToken in ArgTokens(SeparatorParser(separator).AsEnumerable().FilterNulls(), escapeChar, excludeTrailingWhitespaceInSeparator)
                  from value in ArgTokens(valueTokenParser.AsEnumerable(), escapeChar, excludeTrailingWhitespace: true, excludeLeadingWhitespaceInValue).Optional()
                  select ConcatTokens(separatorToken, value.GetOrDefault())
              ).Optional()
              select ConcatTokens(flag.GetOrDefault(), keyword, separatorAndValue.GetOrDefault())
            : optionalValue
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
