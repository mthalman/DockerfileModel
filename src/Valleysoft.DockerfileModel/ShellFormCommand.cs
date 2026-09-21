using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.CommandParsers;

namespace Valleysoft.DockerfileModel;

public class ShellFormCommand : Command
{
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

    public static Parser<ShellFormCommand> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ShellFormCommand(tokens, escapeChar);

    internal static Parser<ShellFormCommand> GetDiagnosticParser(char escapeChar) =>
        from tokens in ArgumentListAsLiteral(escapeChar, requireContent: true)
        select new ShellFormCommand(tokens, escapeChar);

    public override CommandType CommandType => CommandType.ShellForm;

    public string Value
    {
        get => ValueToken.Value;
        set
        {
            Guard.NotNullOrEmpty(value, nameof(value));
            ValueToken.Value = value;
        }
    }

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

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        ArgumentListAsLiteral(escapeChar);
}
