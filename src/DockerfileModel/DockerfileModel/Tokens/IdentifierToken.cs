using System.Collections.Generic;

namespace DockerfileModel.Tokens
{
    public class IdentifierToken : AggregateToken, IValueToken, IQuotableToken
    {
        public IdentifierToken(string value)
             : base(new Token[] { new StringToken(value) })
        {
        }

        public IdentifierToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public virtual string Value
        {
            get => this.ToString(excludeQuotes: true);
            set => ReplaceWithToken(new StringToken(value));
        }

        public char? QuoteChar { get; set; }

        public override string ToString() => this.ToString(excludeQuotes: false);
    }
}
