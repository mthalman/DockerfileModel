using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class MountFlag : KeyValueToken<KeywordToken, Mount>
    {
        internal MountFlag(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static MountFlag Create(Mount mount) =>
            Create(new KeywordToken("mount"), mount, tokens => new MountFlag(tokens), isFlag: true);

        public static MountFlag Parse(string text,
            char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(text, Keyword("mount", escapeChar), MountParser(escapeChar), tokens => new MountFlag(tokens), escapeChar: escapeChar);

        public static Parser<MountFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(Keyword("mount", escapeChar), MountParser(escapeChar), tokens => new MountFlag(tokens), escapeChar: escapeChar);

        private static Parser<Mount> MountParser(char escapeChar) =>
            SecretMount.GetParser(escapeChar);
    }
}
