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

        public static KeyValueToken<TKey, TValue> Create(TKey key, TValue value, char separator = DefaultSeparator) =>
            new KeyValueToken<TKey, TValue>(
                ConcatTokens(
                    key,
                    Char.IsWhiteSpace(separator) ? new WhitespaceToken(separator.ToString()) : new SymbolToken(separator),
                    value));

        public static KeyValueToken<TKey, TValue> Parse(string text, Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
            char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new KeyValueToken<TKey, TValue>(GetTokens(text, GetInnerParser(separator, keyTokenParser, valueTokenParser, escapeChar)));

        public static Parser<KeyValueToken<TKey, TValue>> GetParser(
            Parser<TKey> keyTokenParser, Parser<TValue> valueTokenParser,
            char separator = DefaultSeparator, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(separator, keyTokenParser, valueTokenParser, escapeChar)
            select new KeyValueToken<TKey, TValue>(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char separator, Parser<TKey> keyTokenParser,
            Parser<TValue> valueTokenParser, char escapeChar) =>
            from keyword in ArgTokens(keyTokenParser.AsEnumerable(), escapeChar)
            from separatorToken in ArgTokens(SeparatorParser(separator, escapeChar).AsEnumerable().FilterNulls(), escapeChar)
            from value in ArgTokens(valueTokenParser.AsEnumerable(), escapeChar, excludeTrailingWhitespace: true)
            select ConcatTokens(keyword, separatorToken, value);

        private static Parser<Token?> SeparatorParser(char separator, char escapeChar) =>
            Char.IsWhiteSpace(separator) ?
                Sprache.Parse.Return<Token?>(null) :
                Symbol(separator).Cast<SymbolToken, Token>();
    }
}
