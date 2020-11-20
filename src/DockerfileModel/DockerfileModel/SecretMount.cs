using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class SecretMount : Mount
    {
        internal SecretMount(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public string Id
        {
            get => IdToken.Value;
            set
            {
                Requires.NotNull(value, nameof(value));
                IdToken.ValueToken.Value = value;
            }
        }

        public KeyValueToken<KeywordToken, LiteralToken> IdToken
        {
            get => Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>().Skip(1).First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(IdToken, value);
            }
        }

        public string? DestinationPath
        {
            get => DestinationPathToken?.Value;
            set
            {
                KeyValueToken<KeywordToken, LiteralToken>? destinationPath = DestinationPathToken;
                if (destinationPath is not null && value is not null)
                {
                    destinationPath.ValueToken.Value = value;
                }
                else
                {
                    DestinationPathToken = String.IsNullOrEmpty(value) ?
                        null :
                        KeyValueToken<KeywordToken, LiteralToken>.Create(new KeywordToken("dst"), new LiteralToken(value!));
                }
            }
        }

        public KeyValueToken<KeywordToken, LiteralToken>? DestinationPathToken
        {
            get => Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>().Skip(2).FirstOrDefault();
            set
            {
                SetToken(DestinationPathToken, value,
                    addToken: token =>
                    {
                        TokenList.Add(new SymbolToken(','));
                        TokenList.Add(token);
                    },
                    removeToken: token =>
                    {
                        TokenList.RemoveRange(
                            TokenList.FirstPreviousOfType<Token, SymbolToken>(token),
                            token);
                    });
            }
        }

        public static SecretMount Create(string id, string? destinationPath = null,
            char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullOrEmpty(id, nameof(id));
            string? destinationSegment = null;
            if (!String.IsNullOrEmpty(destinationPath))
            {
                destinationSegment = $",dst={destinationPath}";
            }

            return Parse($"type=secret,id={id}{destinationSegment}", escapeChar);
        }

        public static SecretMount Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new SecretMount(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<SecretMount> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new SecretMount(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar)
        {
            Parser<LiteralToken> valueParser = LiteralAggregate(
                escapeChar, new char[] { ',' });

            return
                from type in ArgTokens(
                    KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                        Keyword("type", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable(), escapeChar)
                from comma in ArgTokens(Symbol(',').AsEnumerable(), escapeChar)
                from idDest in IdAndDestinationParser(valueParser, escapeChar).Or(IdParser(valueParser, escapeChar))
                select ConcatTokens(type, comma, idDest);
        }

        private static Parser<IEnumerable<Token>> IdAndDestinationParser(Parser<LiteralToken> valueParser, char escapeChar) =>
            from id in ArgTokens(
                KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                    Keyword("id", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable(), escapeChar)
            from dest in ArgTokens(DestinationParser(escapeChar), escapeChar, excludeTrailingWhitespace: true)
            select ConcatTokens(id, dest);

        private static Parser<IEnumerable<Token>> IdParser(Parser<LiteralToken> valueParser, char escapeChar) =>
            KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                Keyword("id", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable();

        private static Parser<IEnumerable<Token>> DestinationParser(char escapeChar) =>
            from comma in Symbol(',')
            from dst in KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                Keyword("dst", escapeChar), LiteralAggregate(escapeChar), escapeChar: escapeChar)
            select ConcatTokens(comma, dst);
    }
}
