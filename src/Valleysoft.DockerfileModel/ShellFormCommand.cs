using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.CommandParsers;

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

    public static Parser<ShellFormCommand> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ShellFormCommand(tokens, escapeChar);

    internal static Parser<ShellFormCommand> GetExecFormFallbackParser(char escapeChar, bool diagnostic) =>
        input =>
        {
            if (StartsWithNestedJsonArray(source: input.Source, start: input.Position, escapeChar))
            {
                return Result.Failure<ShellFormCommand>(
                    input,
                    "Nested JSON arrays are not valid shell-form fallback commands.",
                    new[] { "shell-form command" });
            }

            return (diagnostic ? GetDiagnosticParser(escapeChar) : GetParser(escapeChar))(input);
        };

    internal static Parser<ShellFormCommand> GetDiagnosticParser(char escapeChar) =>
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

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        ArgumentListAsLiteral(escapeChar);

    private static bool StartsWithNestedJsonArray(string source, int start, char escapeChar)
    {
        if (start >= source.Length || source[start] != '[')
        {
            return false;
        }

        int i = start + 1;
        while (i < source.Length)
        {
            char c = source[i];
            if (c is '\r' or '\n')
            {
                return false;
            }

            if (char.IsWhiteSpace(c) || c == ',')
            {
                i++;
                continue;
            }

            if (c == '[')
            {
                return true;
            }

            if (c == ']')
            {
                return false;
            }

            if (c != '"')
            {
                return false;
            }

            i++;
            bool escaped = false;
            while (i < source.Length)
            {
                c = source[i];
                if (c is '\r' or '\n')
                {
                    return false;
                }

                if (escaped)
                {
                    escaped = false;
                }
                else if (c == escapeChar)
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    i++;
                    break;
                }

                i++;
            }
        }

        return false;
    }
}
