using System.Collections.Generic;

namespace DockerfileModel.Tokens
{
    public class KeywordToken : AggregateToken, IValueToken
    {
        public KeywordToken(string value)
            : base(new Token[] { new StringToken(value) })
        {
        }

        public KeywordToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Value
        {
            get => this.ToString(excludeLineContinuations: true);
            set => ReplaceWithToken(new StringToken(value));
        }
    }
}
