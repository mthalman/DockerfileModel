using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class FromFlag : KeyValueToken<KeywordToken, StageName>
    {
        public FromFlag(string stageName, char escapeChar = Dockerfile.DefaultEscapeChar)
            : base(new KeywordToken("from"), new StageName(stageName, escapeChar), isFlag: true)
        {
        }

        internal FromFlag(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public static FromFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(text, Keyword("from", escapeChar), StageName.GetParser(escapeChar), tokens => new FromFlag(tokens), escapeChar: escapeChar);

        public static Parser<FromFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(Keyword("from", escapeChar), StageName.GetParser(escapeChar), tokens => new FromFlag(tokens), escapeChar: escapeChar);
    }
}
