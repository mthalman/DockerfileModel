using System.Collections.Generic;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel
{
    public class Variable : IdentifierToken
    {
        private readonly char escapeChar;

        public Variable(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
        {
        }

        private Variable((IEnumerable<Token> Tokens, char? QuoteChar) tokensInfo, char escapeChar)
            : this(tokensInfo.Tokens, escapeChar)
        {
            QuoteChar = tokensInfo.QuoteChar;
        }

        internal Variable(IEnumerable<Token> tokens, char escapeChar)
            : base(tokens)
        {
            this.escapeChar = escapeChar;
        }

        public static Parser<Variable> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new Variable(tokens, escapeChar);

        protected override IEnumerable<Token> GetInnerTokens(string value) =>
            GetTokens(value, GetInnerParser(escapeChar)).Tokens;

        private static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> GetInnerParser(char escapeChar) =>
            IdentifierTokens(VariableRefCharParser, VariableRefCharParser, escapeChar);
    }
}
