using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

/// <summary>Shell-form command text, without interpreting shell words or expanding runtime variables.</summary>
public class ShellFormCommand : Command
{
    /// <summary>Creates command tokens from nonempty shell text using the supplied Dockerfile escape context.</summary>
    public ShellFormCommand(string command, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(command, escapeChar), escapeChar)
    {
    }

    internal ShellFormCommand(IEnumerable<Token> tokens) : this(tokens, Dockerfile.DefaultEscapeChar)
    {
    }

    internal ShellFormCommand(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    public static ShellFormCommand Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, ArgumentListAsLiteral(escapeChar)), escapeChar);

    internal static TextParser<ShellFormCommand> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ShellFormCommand(tokens, escapeChar);

    internal static TextParser<ShellFormCommand> GetDiagnosticParser(char escapeChar) =>
        from tokens in ArgumentListAsLiteral(escapeChar, requireContent: true)
        select new ShellFormCommand(tokens, escapeChar);

    public override CommandType CommandType => CommandType.ShellForm;

    /// <summary>Gets or sets command contents through the existing literal, excluding formatting trivia from the returned value.</summary>
    public string Value
    {
        get => ValueToken.Value;
        set
        {
            Guard.NotNullOrEmpty(value, nameof(value));
            ValueToken.Value = value;
        }
    }

    /// <summary>Gets or replaces the command literal, including its syntax and formatting.</summary>
    public LiteralToken ValueToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Guard.NotNull(value, nameof(value));
            SetToken(ValueToken, value);
        }
    }

    private static IEnumerable<Token> GetTokens(string command, char escapeChar)
    {
        Guard.NotNullOrEmpty(command, nameof(command));
        return GetTokens(command, GetInnerParser(escapeChar));
    }

    private static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        ArgumentListAsLiteral(escapeChar);
}
