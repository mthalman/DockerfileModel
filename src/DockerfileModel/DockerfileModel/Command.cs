using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public abstract class Command : AggregateToken
    {
        protected Command(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        protected Command(string text, Parser<IEnumerable<Token?>> parser)
            : base(text, parser)
        {
        }

        public abstract CommandType CommandType { get; }

        /// <summary>
        /// Parses a literal token.
        /// </summary>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
        protected static Parser<LiteralToken> LiteralToken(char escapeChar, IEnumerable<char> excludedChars)
        {
            Requires.NotNull(excludedChars, nameof(excludedChars));
            return
                from literal in LiteralString(escapeChar, excludedChars, excludeVariableRefChars: false).Many().Flatten()
                where literal.Any()
                select new LiteralToken(TokenHelper.CollapseStringTokens(literal));
        }

        protected static IEnumerable<Token> CollapseCommandTokens(IEnumerable<Token> tokens, char? quoteChar = null)
        {
            Requires.NotNullEmptyOrNullElements(tokens, nameof(tokens));
            return new Token[]
           {
                new LiteralToken(
                    TokenHelper.CollapseTokens(ExtractLiteralTokenContents(tokens),
                        token => token is StringToken || token.GetType() == typeof(WhitespaceToken),
                        val => new StringToken(val)))
                {
                    QuoteChar = quoteChar
                }
           };
        }

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
