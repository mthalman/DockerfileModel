using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class PlatformFlag : KeyValueToken<KeywordToken, LiteralToken>
    {
        public PlatformFlag(string platform, char escapeChar = Dockerfile.DefaultEscapeChar)
            : base(new KeywordToken("platform"), new LiteralToken(platform, canContainVariables: true, escapeChar), isFlag: true)
        {
        }

        internal PlatformFlag(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public static PlatformFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(
                text,
                KeywordToken.GetParser("platform", escapeChar),
                LiteralWithVariables(escapeChar),
                tokens => new PlatformFlag(tokens),
                escapeChar: escapeChar);

        public static Parser<PlatformFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(
                KeywordToken.GetParser("platform", escapeChar),
                LiteralWithVariables(escapeChar),
                tokens => new PlatformFlag(tokens),
                escapeChar: escapeChar);
    }
}
