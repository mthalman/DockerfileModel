using System.Collections.Generic;
using Sprache;
using Validation;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel.Tokens
{
    public class LiteralToken : AggregateToken, IQuotableValueToken
    {
        private readonly bool canContainVariables;
        private readonly char escapeChar;

        public LiteralToken(string value, bool canContainVariables = false, char escapeChar = Dockerfile.DefaultEscapeChar)
             : this(GetTokens(value, canContainVariables, escapeChar), canContainVariables, escapeChar)
        {
        }

        private LiteralToken((IEnumerable<Token> Tokens, char? QuoteChar) tokensInfo, bool canContainVariables, char escapeChar)
            : this(tokensInfo.Tokens, canContainVariables, escapeChar)
        {
            QuoteChar = tokensInfo.QuoteChar;
        }

        internal LiteralToken(IEnumerable<Token> tokens, bool canContainVariables, char escapeChar)
            : base(tokens)
        {
            this.canContainVariables = canContainVariables;
            this.escapeChar = escapeChar;
        }

        public string Value
        {
            get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
            set
            {
                Requires.NotNull(value, nameof(value));
                ReplaceWithTokens(GetInnerTokens(value));
            }
        }

        public char? QuoteChar { get; set; }

        protected virtual IEnumerable<Token> GetInnerTokens(string value) =>
            GetTokens(value, canContainVariables, escapeChar).Tokens;

        private static (IEnumerable<Token> Tokens, char? QuoteChar) GetTokens(string value, bool canContainVariables, char escapeChar)
        {
            Requires.NotNull(value, nameof(value));

            if (value == string.Empty)
            {
                return (new Token[] { new StringToken(value) }, null);
            }

            Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> parser;
            if (canContainVariables)
            {
                parser = LiteralWithVariablesTokens(escapeChar, whitespaceMode: WhitespaceMode.Allowed);
            }
            else
            {
                parser = WrappedInOptionalQuotesLiteralStringWithSpaces(escapeChar, excludeVariableRefChars: false);
            }

            return parser.Parse(value);
        }
    }
}
