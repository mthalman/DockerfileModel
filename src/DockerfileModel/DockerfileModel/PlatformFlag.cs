using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class PlatformFlag : AggregateToken
    {
        private PlatformFlag(string text, char escapeChar)
            : base(text, GetInnerParser(escapeChar))
        {
        }

        internal PlatformFlag(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public string Platform
        {
            get => PlatformToken.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                PlatformToken.ValueToken.Value = value;
            }
        }

        public KeyValueToken<LiteralToken> PlatformToken
        {
            get => Tokens.OfType<KeyValueToken<LiteralToken>>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(PlatformToken, value);
            }
        }

        public static PlatformFlag Create(string platform, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullOrEmpty(platform, nameof(platform));
            return Parse($"--platform={platform}", escapeChar); ;
        }

        public static PlatformFlag Parse(string text, char escapeChar) =>
            new PlatformFlag(text, escapeChar);

        public static Parser<PlatformFlag> GetParser(char escapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new PlatformFlag(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Flag(escapeChar, KeyValueToken<LiteralToken>.GetParser("platform", escapeChar));
    }
}
