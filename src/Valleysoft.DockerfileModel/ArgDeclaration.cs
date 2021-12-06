using System.Text;
using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

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
    }

    public string Name
    {
        get => NameToken.Value;
        set
        {
            Requires.NotNullOrEmpty(value, nameof(value));
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
            Requires.NotNull(value, nameof(value));
            SetToken(NameToken, value);
        }
    }

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

    public bool HasAssignmentOperator =>
        Tokens.OfType<SymbolToken>().Where(token => token.Value == AssignmentOperator.ToString()).Any();

    public static ArgDeclaration Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<ArgDeclaration> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ArgDeclaration(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string name, string? value, char escapeChar)
    {
        Requires.NotNullOrEmpty(name, nameof(name));

        StringBuilder builder = new(name);
        if (value != null)
        {
            builder.Append($"{AssignmentOperator}{value}");
        }

        return GetTokens(builder.ToString(), GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        from argName in ArgTokens(
            Variable.GetParser(escapeChar).AsEnumerable(),
            escapeChar,
            excludeTrailingWhitespace: true)
        from argAssignment in ArgTokens(
            GetArgAssignmentParser(escapeChar),
            escapeChar,
            excludeTrailingWhitespace: true).Optional()
        select ConcatTokens(
            argName,
            argAssignment.GetOrDefault());

    private static Parser<IEnumerable<Token>> GetArgAssignmentParser(char escapeChar) =>
        from lineContinuation in LineContinuations(escapeChar)
        from assignment in Symbol(AssignmentOperator).AsEnumerable()
        from lineContinuation2 in LineContinuations(escapeChar)
        from value in LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes).AsEnumerable().Optional()
        select ConcatTokens(
            lineContinuation,
            assignment,
            lineContinuation2,
            value.GetOrDefault());
}
