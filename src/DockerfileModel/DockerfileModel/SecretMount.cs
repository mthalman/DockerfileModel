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
        private readonly char escapeChar;

        public SecretMount(string id, string? destinationPath = null, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(id, destinationPath, escapeChar), escapeChar)
        {
        }

        internal SecretMount(IEnumerable<Token> tokens, char escapeChar)
            : base(tokens)
        {
            this.escapeChar = escapeChar;
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
                        new KeyValueToken<KeywordToken, LiteralToken>(
                            new KeywordToken("dst", escapeChar),
                            new LiteralToken(value!, canContainVariables: true, escapeChar));
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

        private static IEnumerable<Token> GetTokens(string id, string? destinationPath,
            char escapeChar)
        {
            Requires.NotNullOrEmpty(id, nameof(id));
            string? destinationSegment = null;
            if (!String.IsNullOrEmpty(destinationPath))
            {
                destinationSegment = $",dst={destinationPath}";
            }

            return GetTokens($"type=secret,id={id}{destinationSegment}", GetInnerParser(escapeChar));
        }

        public static SecretMount Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new SecretMount(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

        public static Parser<SecretMount> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new SecretMount(tokens, escapeChar);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar)
        {
            Parser<LiteralToken> valueParser = LiteralWithVariables(
                escapeChar, new char[] { ',' });

            return
                from type in ArgTokens(
                    KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                        KeywordToken.GetParser("type", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable(), escapeChar)
                from comma in ArgTokens(Symbol(',').AsEnumerable(), escapeChar)
                from idDest in IdAndDestinationParser(valueParser, escapeChar).Or(IdParser(valueParser, escapeChar))
                select ConcatTokens(type, comma, idDest);
        }

        private static Parser<IEnumerable<Token>> IdAndDestinationParser(Parser<LiteralToken> valueParser, char escapeChar) =>
            from id in ArgTokens(
                KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                    KeywordToken.GetParser("id", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable(), escapeChar)
            from dest in ArgTokens(DestinationParser(escapeChar), escapeChar, excludeTrailingWhitespace: true)
            select ConcatTokens(id, dest);

        private static Parser<IEnumerable<Token>> IdParser(Parser<LiteralToken> valueParser, char escapeChar) =>
            KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                KeywordToken.GetParser("id", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable();

        private static Parser<IEnumerable<Token>> DestinationParser(char escapeChar) =>
            from comma in Symbol(',')
            from dst in KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                KeywordToken.GetParser("dst", escapeChar), LiteralWithVariables(escapeChar), escapeChar: escapeChar)
            select ConcatTokens(comma, dst);
    }
}
