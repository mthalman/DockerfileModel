using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class IntervalFlag : KeyValueToken<KeywordToken, LiteralToken>
    {
        internal IntervalFlag(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static IntervalFlag Create(string interval) =>
            Create(new KeywordToken("interval"), new LiteralToken(interval), tokens => new IntervalFlag(tokens), isFlag: true);

        public static IntervalFlag Parse(string text,
            char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(text, Keyword("interval", escapeChar), LiteralAggregate(escapeChar), tokens => new IntervalFlag(tokens), escapeChar: escapeChar);

        public static Parser<IntervalFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(Keyword("interval", escapeChar), LiteralAggregate(escapeChar), tokens => new IntervalFlag(tokens), escapeChar: escapeChar);
    }
}
