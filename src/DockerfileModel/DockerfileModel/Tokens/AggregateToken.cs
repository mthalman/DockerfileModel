using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel.Tokens
{
    public abstract class AggregateToken : Token
    {
        protected AggregateToken(string text, Parser<IEnumerable<Token?>> parser)
        {
            this.TokenList = FilterNulls(parser.Parse(text))
                .ToList();
        }

        protected AggregateToken(string text, Parser<Token> parser)
        {
            this.TokenList = new List<Token>
            {
                parser.Parse(text)
            };
        }

        protected AggregateToken(IEnumerable<Token> tokens)
        {
            this.TokenList = tokens.ToList();
        }

        protected List<Token> TokenList { get; }

        public IEnumerable<Token> Tokens => this.TokenList;

        public override string ToString() =>
            String.Concat(Tokens.Select(token => token.ToString()));

        protected IList<string> GetComments()
        {
            return new StringWrapperList<CommentToken>(
                GetCommentTokens(),
                token => token.Text,
                (token, value) => token.Text = value);
        }

        private IEnumerable<CommentToken> GetCommentTokens()
        {
            foreach (Token token in Tokens)
            {
                if (token is CommentToken comment)
                {
                    yield return comment;
                }
                else if (token is AggregateToken aggToken)
                {
                    foreach (var subToken in aggToken.GetCommentTokens())
                    {
                        yield return subToken;
                    }
                }
            }
        }
    }
}
