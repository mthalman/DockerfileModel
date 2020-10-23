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

        public override string ToString() => GetTokensString(excludeLineContinuations: false);

        internal string GetTokensString(bool excludeLineContinuations) =>
            String.Concat(
                Tokens
                    .Where(token => !excludeLineContinuations || token is not LineContinuationToken)
                    .Select(token => token.ToString()));

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

            if (this is IQuotableToken quotableToken && quotableToken.QuoteChar.HasValue)
            {
                return $"{quotableToken.QuoteChar}{ResolveVariablesCore(escapeChar, variables, options)}{quotableToken.QuoteChar}";
            }
            else
            {
                return ResolveVariablesCore(escapeChar, variables, options);
            }
        }

        protected IList<string> GetComments()
        {
            return new StringWrapperList<CommentToken>(
                GetCommentTokens(),
                token => token.Text,
                (token, value) => token.Text = value);
        }

        protected void SetToken<TToken>(TToken? currentValue, TToken? newValue,
            Action<TToken>? addToken = null, Action<TToken>? removeToken = null)
            where TToken : Token
        {
            if (addToken is null)
            {
                addToken = token => this.TokenList.Add(token);
            }

            if (removeToken is null)
            {
                removeToken = token => this.TokenList.Remove(token);
            }

            if (currentValue != null)
            {
                if (newValue is null)
                {
                    removeToken(currentValue);
                }
                else
                {
                    this.TokenList[this.TokenList.IndexOf(currentValue)] = newValue;
                }
            }
            else if (newValue != null)
            {
                addToken(newValue);
            }
        }

        internal void ReplaceWithToken(Token token)
        {
            TokenList.RemoveAll(token => true);
            TokenList.Add(token);
        }

        private string? ResolveVariablesCore(char escapeChar, IDictionary<string, string?> variables, ResolutionOptions options)
        {
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < TokenList.Count; i++)
            {
                Token token = TokenList[i];
                string? value;
                if (token is AggregateToken aggregate)
                {
                    value = aggregate.ResolveVariables(escapeChar, variables, options);
                    if (options.UpdateInline)
                    {
                        TokenList[i] = new StringToken(value ?? String.Empty);
                    }
                }
                else
                {
                    value = options.FormatValue(escapeChar, token.ToString());
                }

                builder.Append(value);
            }

            return builder.ToString();
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
