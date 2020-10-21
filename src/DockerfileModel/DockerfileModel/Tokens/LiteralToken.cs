using System.Collections.Generic;

namespace DockerfileModel.Tokens
{
    public class LiteralToken : AggregateToken, IValueToken, IQuotableToken
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
            get => this.ToString(excludeQuotes: true);
            set => ReplaceWithToken(new StringToken(value));
        }

        public char? QuoteChar { get; set; }

        public override string ToString() => this.ToString(excludeQuotes: false);
    }
}
