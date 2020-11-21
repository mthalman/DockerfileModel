using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class RetriesFlag : KeyValueToken<KeywordToken, LiteralToken>
    {
        internal RetriesFlag(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static RetriesFlag Create(string retryCount) =>
            Create(new KeywordToken("retries"), new LiteralToken(retryCount), tokens => new RetriesFlag(tokens), isFlag: true);

        public static RetriesFlag Parse(string text,
            char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(text, Keyword("retries", escapeChar), LiteralAggregate(escapeChar), tokens => new RetriesFlag(tokens), escapeChar: escapeChar);

        public static Parser<RetriesFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(Keyword("retries", escapeChar), LiteralAggregate(escapeChar), tokens => new RetriesFlag(tokens), escapeChar: escapeChar);
    }
}
