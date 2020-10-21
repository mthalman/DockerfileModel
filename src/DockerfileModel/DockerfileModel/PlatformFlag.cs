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
        private LiteralToken platform;

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        private PlatformFlag(string text, char escapeChar)
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            : base(text, GetParser(escapeChar))
        {
            Initialize();
        }

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        internal PlatformFlag(IEnumerable<Token> tokens)
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            : base(tokens)
        {
            Initialize();
        }

        public string Platform
        {
            get => PlatformToken.Value;
            set => PlatformToken.Value = value;
        }

        public LiteralToken PlatformToken
        {
            get => this.platform;
            set
            {
                Requires.NotNull(value, nameof(value));
                this.platform = value;
                this.TokenList[3] = this.platform;
            }
        }

        public static PlatformFlag Create(string platform) =>
            Parse($"--platform={platform}", Instruction.DefaultEscapeChar);

        public static PlatformFlag Parse(string text, char escapeChar) =>
            new PlatformFlag(text, escapeChar);

        public static Parser<IEnumerable<Token>> GetParser(char escapeChar) =>
            from flagSeparator in Symbol("--")
            from platformKeyword in Sprache.Parse.IgnoreCase("platform").Text()
            from platformSeparator in Symbol("=")
            from platform in LiteralAggregate(escapeChar, isWhitespaceAllowed: false, tokens => new LiteralToken(tokens))
            select ConcatTokens(
                flagSeparator,
                new KeywordToken(platformKeyword),
                platformSeparator,
                platform);

        private void Initialize()
        {
            this.platform = this.TokenList.OfType<LiteralToken>().First();
        }
    }
}
