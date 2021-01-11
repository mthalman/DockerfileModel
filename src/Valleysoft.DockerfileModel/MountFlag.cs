using System.Collections.Generic;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;

namespace Valleysoft.DockerfileModel
{
    public class MountFlag : KeyValueToken<KeywordToken, Mount>
    {
        public MountFlag(Mount mount, char escapeChar = Dockerfile.DefaultEscapeChar)
            : base(new KeywordToken("mount", escapeChar), mount, isFlag: true)
        {
        }

        internal MountFlag(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static MountFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(
                text,
                KeywordToken.GetParser("mount", escapeChar),
                MountParser(escapeChar),
                tokens => new MountFlag(tokens),
                escapeChar: escapeChar);

        public static Parser<MountFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(
                KeywordToken.GetParser("mount", escapeChar),
                MountParser(escapeChar),
                tokens => new MountFlag(tokens),
                escapeChar: escapeChar);

        private static Parser<Mount> MountParser(char escapeChar) =>
            SecretMount.GetParser(escapeChar);
    }
}
