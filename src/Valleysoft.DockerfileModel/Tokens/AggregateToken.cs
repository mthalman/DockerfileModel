using System.Text;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel.Tokens;
/// <summary>A syntax element composed of ordered child tokens.</summary>
/// <remarks>
/// Children are shared mutable objects. Low-level token manipulation does not provide all validation
/// and separator/trivia maintenance supplied by syntax-aware <see cref="EditableList{T}"/> views.
/// </remarks>
public abstract class AggregateToken : Token
{
    protected AggregateToken(IEnumerable<Token> tokens)
    {
        Guard.NotNull(tokens, nameof(tokens));

        this.TokenList = tokens.ToList();
    }

    protected static IEnumerable<Token> GetTokens(string text, Parser<IEnumerable<Token?>> parser)
    {
        Guard.NotNull(text, nameof(text));
        Guard.NotNull(parser, nameof(parser));

        return FilterNulls(parser.Parse(text))
            .ToList();
    }

    protected internal List<Token> TokenList { get; }

    /// <summary>Gets the live ordered child sequence, not a snapshot or a deep copy.</summary>
    /// <remarks>Structural edits during enumeration can invalidate the enumerator.</remarks>
    public IEnumerable<Token> Tokens => this.TokenList;

    internal char EditingEscapeChar { get; set; } = Dockerfile.DefaultEscapeChar;

    protected override string GetUnderlyingValue(TokenStringOptions options)
    {
        Guard.NotNull(options, nameof(options));

        return String.Concat(
            Tokens
                .Where(token => !options.ExcludeLineContinuations || token is not LineContinuationToken)
                .Where(token => !options.ExcludeComments || token is not CommentToken)
                .Where(token => !options.ExcludeNewLines || token is not NewLineToken)
                .Select(token => token.ToString(options)));
    }

    /// <summary>Resolves variable-reference tokens using the supplied environment, without establishing document ARG scope.</summary>
    /// <param name="escapeChar">The escape character to use when formatting resolved text.</param>
    /// <param name="variables">Variable values, or null for an empty environment.</param>
    /// <param name="options">Substitution options, or null for non-mutating defaults.</param>
    /// <returns>Resolved token text, including surrounding quotes when present; a variable token may resolve to null.</returns>
    /// <remarks>Command instructions can suppress expansion, and raw command text may contain no variable-reference tokens. Use document resolution when ARG scope matters.</remarks>
    public virtual string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
    {
        variables ??= new Dictionary<string, string?>();
        options ??= new ResolutionOptions();

        if (this is IQuotableToken quotableToken && quotableToken.QuoteChar.HasValue)
        {
            return $"{quotableToken.QuoteChar}{ResolveVariablesCore(escapeChar, variables, options)}{quotableToken.QuoteChar}";
        }
        else
        {
            return ResolveVariablesCore(escapeChar, variables, options);
        }
    }

    protected EditableList<string?> GetComments() => InstructionCommentEditing.Values(this);

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
        Action<LiteralToken?> setToken, bool canContainVariables, char escapeChar) =>
        SetOptionalTokenValue(currentToken, value, val => new LiteralToken(val, canContainVariables, escapeChar), setToken);

    protected static void SetOptionalKeyValueTokenValue<TKeyValueToken, TValue>(TKeyValueToken? currentToken, TValue? value,
        Func<string, TKeyValueToken> createToken, Action<TKeyValueToken?> setToken)
        where TKeyValueToken : KeyValueToken<KeywordToken, TValue>
        where TValue : Token, IValueToken =>
        SetOptionalTokenValue(currentToken, value, token => createToken(token.Value), (token, val) => token.ValueToken = val, setToken);

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

    internal void ReplaceWithTokens(IEnumerable<Token> tokens)
    {
        TokenList.Clear();
        TokenList.AddRange(tokens);
    }

    private string? ResolveVariablesCore(char escapeChar, IDictionary<string, string?> variables, ResolutionOptions options)
    {
        StringBuilder builder = new();

        for (int i = TokenList.Count - 1; i >= 0; i--)
        {
            Token token = TokenList[i];
            string? value;
            if (token is AggregateToken aggregateToken)
            {
                value = aggregateToken.ResolveVariables(escapeChar, variables, options);
                if (token is VariableRefToken && options.UpdateInline)
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
