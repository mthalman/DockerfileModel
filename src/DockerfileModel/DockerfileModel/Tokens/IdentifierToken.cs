using System.Collections.Generic;
using Sprache;
using Validation;

namespace DockerfileModel.Tokens
{
    public abstract class IdentifierToken : AggregateToken, IQuotableValueToken
    {
        protected IdentifierToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Value
        {
            get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                ReplaceWithTokens(GetInnerTokens(value));
            }
        }

        public char? QuoteChar { get; set; }

        protected abstract IEnumerable<Token> GetInnerTokens(string value);

        protected static (IEnumerable<Token> Tokens, char? QuoteChar) GetTokens(string value,
            Parser<(IEnumerable<Token> Token, char? QuoteChar)> parser)
        {
            Requires.NotNull(value, nameof(value));
            return parser.Parse(value);
        }
    }
}
