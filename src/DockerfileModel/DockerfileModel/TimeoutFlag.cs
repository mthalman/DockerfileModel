using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class TimeoutFlag : KeyValueToken<KeywordToken, LiteralToken>
    {
        internal TimeoutFlag(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static TimeoutFlag Create(string timeout) =>
            Create(new KeywordToken("timeout"), new LiteralToken(timeout), tokens => new TimeoutFlag(tokens), isFlag: true);

        public static TimeoutFlag Parse(string text,
            char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(text, Keyword("timeout", escapeChar), LiteralAggregate(escapeChar), tokens => new TimeoutFlag(tokens), escapeChar: escapeChar);

        public static Parser<TimeoutFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(Keyword("timeout", escapeChar), LiteralAggregate(escapeChar), tokens => new TimeoutFlag(tokens), escapeChar: escapeChar);
    }
}
