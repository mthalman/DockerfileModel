using System.Collections.Generic;
using Validation;

namespace DockerfileModel.Tokens
{
    public class IdentifierToken : AggregateToken, IQuotableValueToken
    {
        public IdentifierToken(string value)
             : base(new Token[] { new StringToken(value) })
        {
        }

        internal IdentifierToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Value
        {
            get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                ReplaceWithToken(new StringToken(value));
            }
        }

        public char? QuoteChar { get; set; }
    }
}
