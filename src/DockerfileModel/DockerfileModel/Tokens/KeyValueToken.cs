using System.Collections.Generic;
using System.Linq;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel.Tokens
{
    public class KeyValueToken<TValue> : AggregateToken
        where TValue : Token
    {
        internal KeyValueToken(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public string Key
        {
            get => KeyToken.Value;
        }

        public KeywordToken KeyToken
        {
            get => Tokens.OfType<KeywordToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(KeyToken, value);
            }
        }

        public string Value => ValueToken.ToString(TokenStringOptions.CreateOptionsForValueString());

        public TValue ValueToken
        {
            get => Tokens.OfType<TValue>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(ValueToken, value);
            }
        }

        public static KeyValueToken<TValue> Create(string key, TValue value, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new KeyValueToken<TValue>(
                ConcatTokens(
                    new KeywordToken(key),
                    new SymbolToken('='),
                    value));

        public static KeyValueToken<LiteralToken> Parse(string text, string key, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new KeyValueToken<LiteralToken>(GetTokens(text, GetInnerParser(key, escapeChar)));

        public static Parser<KeyValueToken<TValue>> GetParser(string key, char escapeChar = Dockerfile.DefaultEscapeChar, Parser<TValue>? valueTokenParser = null) =>
            from tokens in GetInnerParser(key, escapeChar, valueTokenParser)
            select new KeyValueToken<TValue>(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(string key, char escapeChar, Parser<Token>? valueTokenParser = null)
        {
            Requires.NotNullOrEmpty(key, nameof(key));

            if (valueTokenParser is null)
            {
                valueTokenParser = LiteralAggregate(escapeChar);
            }

            return from keyword in ArgTokens(Keyword(key, escapeChar).AsEnumerable(), escapeChar)
                   from equalOperator in ArgTokens(Symbol('=').AsEnumerable(), escapeChar)
                   from value in ArgTokens(valueTokenParser.AsEnumerable(), escapeChar, excludeTrailingWhitespace: true)
                   select ConcatTokens(keyword, equalOperator, value);
        }
    }
}
