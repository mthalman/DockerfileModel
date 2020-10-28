using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public abstract class RunCommand : AggregateToken
    {
        protected RunCommand(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        protected RunCommand(string text, Parser<IEnumerable<Token?>> parser)
            : base(text, parser)
        {
        }

        public abstract RunCommandType CommandType { get; }

        /// <summary>
        /// Parses a literal token.
        /// </summary>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
        protected static Parser<LiteralToken> LiteralToken(char escapeChar, IEnumerable<char> excludedChars) =>
            from literal in LiteralString(escapeChar, excludedChars)
                .Many()
                .Flatten()
            select new LiteralToken(TokenHelper.CollapseStringTokens(literal));

        protected static IEnumerable<Token> CollapseRunCommandTokens(IEnumerable<Token> tokens, char? quoteChar = null) =>
           new Token[]
           {
                new LiteralToken(
                    TokenHelper.CollapseTokens(ExtractLiteralTokenContents(tokens),
                        token => token is StringToken || token.GetType() == typeof(WhitespaceToken),
                        val => new StringToken(val)))
                {
                    QuoteChar = quoteChar
                }
           };

        private static IEnumerable<Token> ExtractLiteralTokenContents(IEnumerable<Token> tokens)
        {
            foreach (Token token in tokens)
            {
                if (token is LiteralToken literal)
                {
                    foreach (Token literalItem in literal.Tokens)
                    {
                        yield return literalItem;
                    }
                }
                else
                {
                    yield return token;
                }
            }
        }
    }
}
