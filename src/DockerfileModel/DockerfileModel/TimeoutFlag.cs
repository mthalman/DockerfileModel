using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class TimeoutFlag : KeyValueToken<KeywordToken, LiteralToken>
    {
        public TimeoutFlag(string timeout, char escapeChar = Dockerfile.DefaultEscapeChar)
            : base(new KeywordToken("timeout"), new LiteralToken(timeout, canContainVariables: true, escapeChar), isFlag: true)
        {
        }

        internal TimeoutFlag(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static TimeoutFlag Parse(string text,
            char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(
                text,
                KeywordToken.GetParser("timeout", escapeChar),
                LiteralWithVariables(escapeChar),
                tokens => new TimeoutFlag(tokens),
                escapeChar: escapeChar);

        public static Parser<TimeoutFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(
                KeywordToken.GetParser("timeout", escapeChar),
                LiteralWithVariables(escapeChar),
                tokens => new TimeoutFlag(tokens),
                escapeChar: escapeChar);
    }
}
