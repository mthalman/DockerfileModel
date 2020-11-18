using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class PlatformFlag : KeyValueToken<KeywordToken, LiteralToken>
    {
        internal PlatformFlag(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public static PlatformFlag Create(string platform) =>
            Create(new KeywordToken("platform"), new LiteralToken(platform), tokens => new PlatformFlag(tokens), isFlag: true);

        public static PlatformFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(text, Keyword("platform", escapeChar), LiteralAggregate(escapeChar), tokens => new PlatformFlag(tokens), escapeChar: escapeChar);

        public static Parser<PlatformFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(Keyword("platform", escapeChar), LiteralAggregate(escapeChar), tokens => new PlatformFlag(tokens), escapeChar: escapeChar);
    }
}
