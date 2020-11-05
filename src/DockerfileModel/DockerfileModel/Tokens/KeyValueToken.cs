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
        private KeyValueToken(string text, char escapeChar, string key)
            : base(text, GetInnerParser(key, escapeChar))
        {
        }

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

        public string Value
        {
            get => ValueToken.ToString(TokenStringOptions.CreateOptionsForValueString());
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

        public static KeyValueToken<LiteralToken> Create(string key, string value) =>
            Parse($"{key}={value}", Dockerfile.DefaultEscapeChar, key);

        public static KeyValueToken<LiteralToken> Parse(string text, char escapeChar, string key) =>
            new KeyValueToken<LiteralToken>(text, escapeChar, key);

        public static Parser<KeyValueToken<TValue>> GetParser(string key, char escapeChar, Parser<TValue>? valueTokenParser = null) =>
            from tokens in GetInnerParser(key, escapeChar, valueTokenParser)
            select new KeyValueToken<TValue>(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(string key, char escapeChar, Parser<Token>? valueTokenParser = null)
        {
            if (valueTokenParser is null)
            {
                valueTokenParser = LiteralAggregate(escapeChar);
            }

            return from keyword in Keyword(key, escapeChar).AsEnumerable()
                   from equalOperator in CharWithOptionalLineContinuation(escapeChar, Sprache.Parse.Char('='), ch => new SymbolToken(ch))
                   from lineCont in LineContinuation(escapeChar).AsEnumerable().Optional()
                   from value in valueTokenParser.AsEnumerable()
                   select ConcatTokens(keyword, equalOperator, lineCont.GetOrDefault(), value);
        }
            
    }
}
