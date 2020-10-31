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
        private SecretMount(string text, char escapeChar)
            : base(text, GetInnerParser(escapeChar))
        {
        }

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

        public KeyValueToken<LiteralToken> IdToken
        {
            get => Tokens.OfType<KeyValueToken<LiteralToken>>().Skip(1).First();
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
                KeyValueToken<LiteralToken>? destinationPath = DestinationPathToken;
                if (destinationPath is not null && value is not null)
                {
                    destinationPath.ValueToken.Value = value;
                }
                else
                {
                    DestinationPathToken = String.IsNullOrEmpty(value) ? null : KeyValueToken<LiteralToken>.Create("dst", value!);
                }
            }
        }

        public KeyValueToken<LiteralToken>? DestinationPathToken
        {
            get => Tokens.OfType<KeyValueToken<LiteralToken>>().Skip(2).FirstOrDefault();
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
                        TokenList.RemoveRange(TokenList.Count - 2, 2);
                    });
            }
        }

        public static SecretMount Create(string id, string? destinationPath = null)
        {
            string? destinationSegment = null;
            if (!String.IsNullOrEmpty(destinationPath))
            {
                destinationSegment = $",dst={destinationPath}";
            }

            return Parse($"type=secret,id={id}{destinationSegment}", Instruction.DefaultEscapeChar);
        }

        public static SecretMount Parse(string text, char escapeChar) =>
            new SecretMount(text, escapeChar);

        public static Parser<SecretMount> GetParser(char escapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new SecretMount(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar)
        {
            Parser<LiteralToken> valueParser = LiteralAggregate(
                escapeChar, tokens => new LiteralToken(tokens), new char[] { ',' });

            return from type in KeyValueToken<LiteralToken>.GetParser("type", escapeChar, valueParser).AsEnumerable()
                   from comma in CharWithOptionalLineContinuation(escapeChar, Sprache.Parse.Char(','), ch => new SymbolToken(ch))
                   from lineCont in LineContinuation(escapeChar).AsEnumerable().Optional()
                   from id in KeyValueToken<LiteralToken>.GetParser("id", escapeChar, valueParser).AsEnumerable()
                   from destination in Destination(escapeChar).Optional()
                   select ConcatTokens(type, comma, lineCont.GetOrDefault(), id, destination.GetOrDefault());
        }

        private static Parser<IEnumerable<Token>> Destination(char escapeChar) =>
            from comma in Symbol(',')
            from dst in KeyValueToken<LiteralToken>.GetParser("dst", escapeChar)
            select ConcatTokens(comma, dst);
    }
}
