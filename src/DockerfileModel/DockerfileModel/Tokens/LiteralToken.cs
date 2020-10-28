using System.Collections.Generic;

namespace DockerfileModel.Tokens
{
    public class LiteralToken : AggregateToken, IQuotableValueToken
    {
        public LiteralToken(string value)
             : base(new Token[] { new StringToken(value) })
        {
        }

        public LiteralToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Value
        {
            get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
            set => ReplaceWithToken(new StringToken(value));
        }

        public char? QuoteChar { get; set; }
    }
}
