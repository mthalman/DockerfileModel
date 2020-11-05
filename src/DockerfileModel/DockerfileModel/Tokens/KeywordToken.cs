using System;
using System.Collections.Generic;

namespace DockerfileModel.Tokens
{
    public class KeywordToken : AggregateToken, IValueToken
    {
        public KeywordToken(string value)
            : base(new Token[] { new StringToken(value) })
        {
        }

        internal KeywordToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Value => this.ToString(TokenStringOptions.CreateOptionsForValueString());

        string IValueToken.Value
        {
            get => Value;
            set => throw new NotSupportedException("The value of a keyword is read-only.");
        }
    }
}
