using System.Collections.Generic;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel
{
    public class RetriesFlag : KeyValueToken<KeywordToken, LiteralToken>
    {
        public RetriesFlag(string retryCount, char escapeChar = Dockerfile.DefaultEscapeChar)
            : base(new KeywordToken("retries", escapeChar), new LiteralToken(retryCount, canContainVariables: true, escapeChar), isFlag: true)
        {
        }

        internal RetriesFlag(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static RetriesFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(
                text,
                KeywordToken.GetParser("retries", escapeChar),
                LiteralWithVariables(escapeChar),
                tokens => new RetriesFlag(tokens),
                escapeChar: escapeChar);

        public static Parser<RetriesFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(
                KeywordToken.GetParser("retries", escapeChar),
                LiteralWithVariables(escapeChar),
                tokens => new RetriesFlag(tokens),
                escapeChar: escapeChar);
    }
}
