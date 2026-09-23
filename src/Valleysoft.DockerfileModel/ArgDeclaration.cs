using System.Text;
using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

/// <summary>An ARG name and optional default, preserving the distinction between no assignment and an empty assignment.</summary>
public class ArgDeclaration : AggregateToken, IKeyValuePair
{
    private const char AssignmentOperator = '=';
    private readonly char escapeChar;

    public ArgDeclaration(string name, string? value= null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(name, value, escapeChar), escapeChar)
    {
    }

    internal ArgDeclaration(IEnumerable<Token> tokens, char escapeChar)
        : base(tokens)
    {
        this.escapeChar = escapeChar;
        EditingEscapeChar = escapeChar;
    }

    public string Name
    {
        get => NameToken.Value;
        set
        {
            Guard.NotNullOrEmpty(value, nameof(value));
            NameToken.Value = value;
        }
    }

    string IKeyValuePair.Key
    {
        get => Name;
        set => Name = value;
    }

    public Variable NameToken
    {
        get => Tokens.OfType<Variable>().First();
        set
        {
            Guard.NotNull(value, nameof(value));
            SetToken(NameToken, value);
        }
    }

    /// <summary>Gets the default value, null for a bare name, or an empty string for an empty assignment; sets the default contents.</summary>
    /// <remarks>
    /// A scalar update retains an existing value token. When a value token exists, assigning null removes it
    /// and its assignment operator. A parsed empty assignment can have no value token; assigning null in
    /// that state leaves the existing assignment operator unchanged.
    /// </remarks>
    public string? Value
    {
        get
        {
            string? argValue = ValueToken?.Value;
            if (argValue is null)
            {
                return HasAssignmentOperator ? string.Empty : null;
            }

            return argValue;
        }
        set
        {
            LiteralToken? argValue = ValueToken;
            if (argValue != null && value is not null)
            {
                argValue.Value = value;
            }
            else
            {
                ValueToken = value is null ? null : new LiteralToken(value, canContainVariables: true, escapeChar);
            }
        }
    }

    /// <summary>Gets or replaces the default's syntax token, which can be absent even when an empty assignment exists.</summary>
    public LiteralToken? ValueToken
    {
        get => this.Tokens.OfType<LiteralToken>().FirstOrDefault();
        set
        {
            this.SetToken(ValueToken, value,
                addToken: token =>
                {
                    if (HasAssignmentOperator)
                    {
                        this.TokenList.Add(token);
                    }
                    else
                    {
                        this.TokenList.AddRange(new Token[]
                        {
                            new SymbolToken(AssignmentOperator),
                            token
                        });
                    }
                },
                removeToken: token =>
                {
                    TokenList.RemoveRange(
                        TokenList.FirstPreviousOfType<Token, SymbolToken>(token),
                        token);
                });
        }
    }

    /// <summary>Gets whether the serialized declaration contains an assignment operator, independently of value-token presence.</summary>
    public bool HasAssignmentOperator =>
        Tokens.OfType<SymbolToken>().Where(token => token.Value == AssignmentOperator.ToString()).Any();

    public static ArgDeclaration Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar).AtEnd()), escapeChar);

    internal static TextParser<ArgDeclaration> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ArgDeclaration(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string name, string? value, char escapeChar)
    {
        Guard.NotNullOrEmpty(name, nameof(name));

        StringBuilder builder = new(name);
        if (value != null)
        {
            builder.Append($"{AssignmentOperator}{value}");
        }

        return GetTokens(builder.ToString(), GetInnerParser(escapeChar));
    }

    private static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        from argName in ArgTokens(
            Variable.GetParser(escapeChar).AsEnumerable(),
            escapeChar,
            excludeTrailingWhitespace: true)
        from argAssignment in ArgTokens(
            GetArgAssignmentParser(escapeChar),
            escapeChar,
            excludeTrailingWhitespace: true).Try().OptionalOrDefault(Enumerable.Empty<Token>())
        select ConcatTokens(
            argName,
            argAssignment);

    private static TextParser<IEnumerable<Token>> GetArgAssignmentParser(char escapeChar) =>
        from lineContinuation in LineContinuations(escapeChar)
        from assignment in Symbol(AssignmentOperator).AsEnumerable()
        from lineContinuation2 in LineContinuationWithTrailingWhitespace(escapeChar)
        from value in GetAssignedValueParser(escapeChar, lineContinuation2.Any())
        select ConcatTokens(
            lineContinuation,
            assignment,
            lineContinuation2,
            value);

    /// <summary>
    /// Parses zero or more line continuations. When at least one line continuation is present,
    /// also consumes any trailing whitespace (continuation-line indentation).
    /// </summary>
    private static TextParser<IEnumerable<Token>> LineContinuationWithTrailingWhitespace(char escapeChar) =>
        (from firstContinuation in LineContinuationToken.GetParser(escapeChar)
         from moreContinuations in LineContinuations(escapeChar)
         from trailingWhitespace in ContinuationIndentation()
         select ConcatTokens(
             new Token[] { firstContinuation },
             moreContinuations,
             trailingWhitespace is null ? Enumerable.Empty<Token>() : new Token[] { trailingWhitespace }))
        .Try().Or(Superpower.Parse.Return(Enumerable.Empty<Token>()));

    private static TextParser<IEnumerable<Token>> GetAssignedValueParser(char escapeChar, bool requireValue)
    {
        TextParser<IEnumerable<Token>> valueParser = LiteralWithVariables(
                escapeChar,
                whitespaceMode: WhitespaceMode.AllowedInQuotes)
            .AsEnumerable()
            .Select(tokens => tokens.Cast<Token>());
        if (requireValue)
        {
            return valueParser;
        }

        return
            from optionalValue in valueParser.Try().OptionalOrDefault(Enumerable.Empty<Token>())
            select optionalValue;
    }

    private static TextParser<WhitespaceToken?> ContinuationIndentation() =>
        from whitespace in Character.Matching(
            ch => char.IsWhiteSpace(ch) && ch is not ('\r' or '\n'),
            "horizontal whitespace").Try().Many().Text()
        select whitespace.Length > 0 ? new WhitespaceToken(whitespace) : null;
}
