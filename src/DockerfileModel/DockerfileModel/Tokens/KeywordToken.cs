using System;
using System.Collections.Generic;
using System.Text;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel.Tokens
{
    public class KeywordToken : AggregateToken, IValueToken
    {
        public KeywordToken(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
            : base(GetTokens(value, GetInnerParser(StripLineContinuations(value, escapeChar), escapeChar)))
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

        public static Parser<KeywordToken> GetParser(string keyword, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(keyword, escapeChar)
            select new KeywordToken(tokens);

        private static string StripLineContinuations(string value, char escapeChar)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char ch in value)
            {
                if (ch == escapeChar || Char.IsWhiteSpace(ch))
                {
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(string value, char escapeChar) =>
            StringToken(value, escapeChar);
    }
}
