using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel.Tokens
{
    public class KeyValueToken<TKey, TValue> : AggregateToken, IKeyValuePair
        where TKey : Token, IValueToken
        where TValue : Token
    {
        public const char DefaultSeparator = '=';

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

        public string Value
        {
            get => ValueToken.ToString(TokenStringOptions.CreateOptionsForValueString());
            set
            {
                Requires.NotNull(value, nameof(value));
                
                if (ValueToken is IValueToken valueToken)
                {
                    valueToken.Value = value;
                }
                else
                {
                    throw new NotSupportedException($"Setting the value is not supported for values of type '{typeof(TValue)}'.");
                }
            }
        }

        public TValue ValueToken
        {
            get => Tokens.OfType<TValue>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(ValueToken, value);
            }
        }

        public static KeyValueToken<TKey, TValue> Create(TKey key, TValue value, bool isFlag = false, char separator = DefaultSeparator) =>
            Create(key, value, tokens => new KeyValueToken<TKey, TValue>(tokens), isFlag, separator);

        public static KeyValueToken<TKey, TValue> Parse(string text, Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
            char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(text, keyTokenParser, valueTokenParser, tokens => new KeyValueToken<TKey, TValue>(tokens), separator, escapeChar);

        public static Parser<KeyValueToken<TKey, TValue>> GetParser(
            Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
            char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(keyTokenParser, valueTokenParser, tokens => new KeyValueToken<TKey, TValue>(tokens), separator, escapeChar);

        protected static T Create<T>(TKey key, TValue value, Func<IEnumerable<Token>, T> createToken, bool isFlag = false, char separator = DefaultSeparator)
            where T : KeyValueToken<TKey, TValue> =>
            createToken(
                ConcatTokens(
                    isFlag ? new Token[] { new SymbolToken('-'), new SymbolToken('-') } : Enumerable.Empty<Token>(),
                    new Token[]
                    {
                        key,
                        Char.IsWhiteSpace(separator) ? new WhitespaceToken(separator.ToString()) : new SymbolToken(separator),
                        value
                    }));

        protected static T Parse<T>(string text, Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
            Func<IEnumerable<Token>, T> createToken, char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar)
            where T : KeyValueToken<TKey, TValue> =>
            createToken(GetTokens(text, GetInnerParser(separator, keyTokenParser, valueTokenParser, escapeChar)));

        protected static Parser<T> GetParser<T>(
            Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser, Func<IEnumerable<Token>, T> createToken,
            char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar)
            where T : KeyValueToken<TKey, TValue> =>
            from tokens in GetInnerParser(separator, keyTokenParser, valueTokenParser, escapeChar)
            select createToken(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char separator, Parser<TKey> keyTokenParser,
            Parser<TValue> valueTokenParser, char escapeChar) =>
            from flag in ArgTokens(FlagParser(escapeChar), escapeChar).Optional()
            from keyword in ArgTokens(keyTokenParser.AsEnumerable(), escapeChar)
            from separatorToken in ArgTokens(SeparatorParser(separator).AsEnumerable().FilterNulls(), escapeChar)
            from value in ArgTokens(valueTokenParser.AsEnumerable(), escapeChar, excludeTrailingWhitespace: true)
            select ConcatTokens(flag.GetOrDefault(), keyword, separatorToken, value);

        private static Parser<IEnumerable<Token>> FlagParser(char escapeChar) =>
            ArgTokens(Symbol('-').AsEnumerable(), escapeChar).Repeat(2).Flatten();

        private static Parser<Token?> SeparatorParser(char separator) =>
            Char.IsWhiteSpace(separator) ?
                Sprache.Parse.Return<Token?>(null) :
                Symbol(separator).Cast<SymbolToken, Token>();
    }
}
