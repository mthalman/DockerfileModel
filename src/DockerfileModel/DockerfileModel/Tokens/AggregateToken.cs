using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel.Tokens
{
    public abstract class AggregateToken : Token
    {
        protected AggregateToken(IEnumerable<Token> tokens)
        {
            Requires.NotNull(tokens, nameof(tokens));

            this.TokenList = tokens.ToList();
        }

        protected static IEnumerable<Token> GetTokens(string text, Parser<IEnumerable<Token?>> parser)
        {
            Requires.NotNull(text, nameof(text));
            Requires.NotNull(parser, nameof(parser));

            return FilterNulls(parser.Parse(text))
                .ToList();
        }

        protected internal List<Token> TokenList { get; }

        public IEnumerable<Token> Tokens => this.TokenList;

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            Requires.NotNull(options, nameof(options));

            return String.Concat(
                Tokens
                    .Where(token => !options.ExcludeLineContinuations || token is not LineContinuationToken)
                    .Where(token => !options.ExcludeComments || token is not CommentToken)
                    .Select(token => token.ToString(options)));
        }

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

        protected IList<string?> GetComments()
        {
            return new ProjectedItemList<CommentToken, string?>(
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

            if (currentValue is not null)
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
            else if (newValue is not null)
            {
                addToken(newValue);
            }
        }

        protected void SetOptionalFlagToken<TToken>(TToken? currentValue, TToken? newValue)
            where TToken : Token
        {
            SetToken(currentValue, newValue,
                addToken: token =>
                {
                    TokenList.InsertRange(1, new Token[]
                    {
                        new WhitespaceToken(" "),
                        token
                    });
                },
                removeToken: token =>
                {
                    TokenList.RemoveRange(
                        TokenList.FirstPreviousOfType<Token, WhitespaceToken>(token),
                        token);
                });
        }

        protected static void SetOptionalLiteralTokenValue(LiteralToken? currentToken, string? value,
            Action<LiteralToken?> setToken) =>
            SetOptionalTokenValue(currentToken, value, val => new LiteralToken(val), setToken);

        protected static void SetOptionalKeyValueTokenValue<TKeyValueToken>(TKeyValueToken? currentToken, LiteralToken? value,
            Func<string, TKeyValueToken> createToken, Action<TKeyValueToken?> setToken)
            where TKeyValueToken : KeyValueToken<KeywordToken, LiteralToken> =>
            SetOptionalTokenValue(currentToken, value, token => createToken(token.Value), (token, val) => token.ValueToken = val, setToken);

        protected static void SetOptionalKeyValueTokenValue<TKey, TValue, TKeyValueToken>(TKeyValueToken? currentToken, TValue? value,
            Func<TValue, TKeyValueToken> createToken, Action<TKeyValueToken?> setToken)
            where TKeyValueToken : KeyValueToken<TKey, TValue>
            where TKey : Token, IValueToken
            where TValue : Token =>
            SetOptionalTokenValue(currentToken, value, createToken, (token, val) => token.ValueToken = val, setToken);

        protected static void SetOptionalTokenValue<TToken>(TToken? currentToken, string? value, Func<string, TToken> createToken,
            Action<TToken?> setToken)
            where TToken : Token, IValueToken =>
            SetOptionalTokenValue(currentToken, value, createToken, (token, val) => token.Value = val, setToken);

        protected static void SetOptionalTokenValue<TToken, TValue>(TToken? currentToken, TValue? value, Func<TValue, TToken> createToken,
            Action<TToken, TValue> setTokenValue, Action<TToken?> setToken)
            where TToken : Token
        {
            if (currentToken is not null && !IsNullOrEmpty(value))
            {
                setTokenValue(currentToken, value!);
            }
            else
            {
                setToken(IsNullOrEmpty(value) ? null : createToken(value!));
            }
        }

        private static bool IsNullOrEmpty(object? obj) => obj is null || (obj is string str && str == string.Empty);

        internal void ReplaceWithToken(Token token)
        {
            TokenList.Clear();
            TokenList.Add(token);
        }

        private string? ResolveVariablesCore(char escapeChar, IDictionary<string, string?> variables, ResolutionOptions options)
        {
            StringBuilder builder = new StringBuilder();

            for (int i = TokenList.Count - 1; i >= 0; i--)
            {
                Token token = TokenList[i];
                string? value;
                if (token is AggregateToken aggregate)
                {
                    value = aggregate.ResolveVariables(escapeChar, variables, options);
                    if (options.UpdateInline)
                    {
                        if (value is null)
                        {
                            TokenList.RemoveAt(i);
                        }
                        else
                        {
                            TokenList[i] = new StringToken(value);
                        }
                    }
                }
                else
                {
                    value = options.FormatValue(escapeChar, token.ToString());
                }

                builder.Insert(0, value);
            }

            return builder.ToString();
        }

        protected IEnumerable<CommentToken> GetCommentTokens()
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
