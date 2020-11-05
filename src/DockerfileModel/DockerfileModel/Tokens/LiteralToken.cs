using System.Collections.Generic;
using Validation;

namespace DockerfileModel.Tokens
{
    public class LiteralToken : AggregateToken, IQuotableValueToken
    {
        public LiteralToken(string value)
             : base(new Token[] { new StringToken(value) })
        {
        }

        internal LiteralToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Value
        {
            get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
            set
            {
                Requires.NotNull(value, nameof(value));
                ReplaceWithToken(new StringToken(value));
            }
        }

        public char? QuoteChar { get; set; }
    }
}
