using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class IntervalFlag : KeyValueToken<KeywordToken, LiteralToken>
    {
        public IntervalFlag(string interval, char escapeChar = Dockerfile.DefaultEscapeChar)
            : base(new KeywordToken("interval"), new LiteralToken(interval, canContainVariables: true, escapeChar), isFlag: true)
        {
        }

        internal IntervalFlag(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static IntervalFlag Parse(string text,
            char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(text, Keyword("interval", escapeChar), LiteralWithVariables(escapeChar), tokens => new IntervalFlag(tokens), escapeChar: escapeChar);

        public static Parser<IntervalFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(Keyword("interval", escapeChar), LiteralWithVariables(escapeChar), tokens => new IntervalFlag(tokens), escapeChar: escapeChar);
    }
}
