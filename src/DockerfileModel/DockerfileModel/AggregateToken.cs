using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public abstract class AggregateToken : Token
    {
        protected AggregateToken(string text, Parser<IEnumerable<Token?>> parser)
            : base(text)
        {
            this.TokenList = FilterNulls(parser.Parse(text))
                .ToList();
        }

        protected AggregateToken(string text, Parser<Token> parser)
            : base(text)
        {
            this.TokenList = new List<Token>
            {
                parser.Parse(text)
            };
        }

        protected AggregateToken(IEnumerable<Token> tokens)
            : base(TokensToString(tokens))
        {
            this.TokenList = tokens.ToList();
        }

        protected List<Token> TokenList { get; }

        public IEnumerable<Token> Tokens => this.TokenList;

        public override string ToString() =>
            TokensToString(Tokens);

        private static string TokensToString(IEnumerable<Token> tokens) =>
            String.Join("", tokens
                .Select(token => token.ToString())
                .ToArray());

        protected IEnumerable<CommentToken> GetComments()
        {
            foreach (Token token in Tokens)
            {
                if (token is CommentToken comment)
                {
                    yield return comment;
                }
                else if (token is AggregateToken aggToken)
                {
                    foreach (var subToken in aggToken.GetComments())
                    {
                        yield return subToken;
                    }
                }
            }
        }
    }
}
