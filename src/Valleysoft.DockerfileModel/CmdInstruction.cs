using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

/// <summary>A CMD instruction in shell or JSON exec form.</summary>
public class CmdInstruction : CommandInstruction
{
    /// <summary>Parses raw command text after CMD; ordinary text uses shell form, while JSON-array text selects exec form.</summary>
    public CmdInstruction(string commandWithArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(commandWithArgs, escapeChar), escapeChar)
    {
    }

    /// <summary>Creates JSON exec-form CMD from default argument elements without shell splitting.</summary>
    public CmdInstruction(IEnumerable<string> defaultArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(defaultArgs, escapeChar), escapeChar)
    {
    }

    /// <summary>Creates JSON exec-form CMD with the command as its first element followed by the supplied arguments.</summary>
    public CmdInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(command, args, escapeChar), escapeChar)
    {
    }

    private CmdInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    /// <summary>Parses a standalone CMD instruction, preserving its form, formatting, and escape context.</summary>
    public static CmdInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    internal static TextParser<CmdInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new CmdInstruction(tokens, escapeChar);

    internal static CmdInstruction ParseDiagnostic(string text, char escapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar, diagnostic: true)), escapeChar);

    internal static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar, bool diagnostic = false) =>
        Instruction("CMD", escapeChar, GetArgsParser(escapeChar, diagnostic));

    private static IEnumerable<Token> GetTokens(string commandWithArgs, char escapeChar)
    {
        Guard.NotNullOrEmpty(commandWithArgs, nameof(commandWithArgs));
        return GetTokens($"CMD {commandWithArgs}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(IEnumerable<string> defaultArgs, char escapeChar)
    {
        Guard.NotNull(defaultArgs, nameof(defaultArgs));

        return GetTokens($"CMD {StringHelper.FormatAsJson(defaultArgs)}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(string command, IEnumerable<string> args, char escapeChar)
    {
        Guard.NotNullOrEmpty(command, nameof(command));
        Guard.NotNull(args, nameof(args));
        return GetTokens($"CMD {StringHelper.FormatAsJson(new string[] { command }.Concat(args))}", GetInnerParser(escapeChar));
    }
}
