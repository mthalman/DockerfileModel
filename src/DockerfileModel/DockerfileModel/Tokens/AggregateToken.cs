using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        protected internal List<Token> TokenList { get; }

        public IEnumerable<Token> Tokens => this.TokenList;

        public override string ToString() =>
            String.Concat(Tokens.Select(token => token.ToString()));

        public virtual string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            if (variables is null)
            {
                variables = new Dictionary<string, string?>();
            }

            if (options is null)
            {
                options = new ResolutionOptions();
            }

            if (options.UpdateInline)
            {
                new ArgResolverVisitor(escapeChar, variables, options).Visit(this);
                return ToString();
            }

            StringBuilder builder = new StringBuilder();
            foreach (Token token in Tokens)
            {
                string? value;
                if (token is AggregateToken aggregate)
                {
                    value = aggregate.ResolveVariables(escapeChar, variables, options);
                }
                else
                {
                    value = options.FormatValue(escapeChar, token.ToString());
                }

                builder.Append(value);
            }

            return builder.ToString();
        }

        protected IList<string> GetComments()
        {
            return new StringWrapperList<CommentToken>(
                GetCommentTokens(),
                token => token.Text,
                (token, value) => token.Text = value);
        }

        internal void ReplaceWithToken(Token token)
        {
            TokenList.RemoveAll(token => true);
            TokenList.Add(token);
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

        private class ArgResolverVisitor : TokenVisitor
        {
            private readonly char escapeChar;
            private readonly IDictionary<string, string?> variables;
            private readonly ResolutionOptions options;

            public ArgResolverVisitor(char escapeChar, IDictionary<string, string?> variables, ResolutionOptions options)
            {
                this.escapeChar = escapeChar;
                this.variables = variables;
                this.options = options;
            }

            protected override void VisitAggregateToken(AggregateToken token)
            {
                base.VisitAggregateToken(token);

                if (token is QuotableAggregateToken quotableAggregate)
                {
                    string[] resolvedChildValues = quotableAggregate.Tokens
                        .Select(token =>
                        {
                            if (token is VariableRefToken variableRef)
                            {
                                return variableRef.ResolveVariables(escapeChar, variables, options) ?? string.Empty;
                            }
                            else
                            {
                                return token.ToString();
                            }
                        })
                        .ToArray();

                    Token replacementToken = CreatePrimitiveToken(quotableAggregate.PrimitiveType, string.Concat(resolvedChildValues));
                    quotableAggregate.ReplaceWithToken(replacementToken);
                }
            }
        }
    }

    public abstract class QuotableAggregateToken : AggregateToken
    {
        protected QuotableAggregateToken(IEnumerable<Token> tokens, Type primitiveType) : base(tokens)
        {
            PrimitiveType = primitiveType;
        }

        public char? QuoteChar { get; set; }

        public Type PrimitiveType { get; }

        public override string ToString() => ToString(includeQuotes: true);

        public string ToString(bool includeQuotes)
        {
            if (includeQuotes && QuoteChar.HasValue)
            {
                return $"{QuoteChar}{base.ToString()}{QuoteChar}";
            }

            return base.ToString();
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            if (QuoteChar.HasValue)
            {
                return $"{QuoteChar}{base.ResolveVariables(escapeChar, variables, options)}{QuoteChar}";
            }
            else
            {
                return base.ResolveVariables(escapeChar, variables, options);
            }
        }
    }
}
