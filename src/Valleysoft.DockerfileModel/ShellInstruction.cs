using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;

namespace Valleysoft.DockerfileModel;

/// <summary>A SHELL instruction, whose command uses JSON exec form only.</summary>
public class ShellInstruction : CommandInstruction
{
    /// <summary>Creates a one-element JSON array containing the command, without splitting shell words.</summary>
    public ShellInstruction(string command, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(command, Enumerable.Empty<string>(), escapeChar)
    {
    }

    /// <summary>Creates a JSON array containing the command followed by the supplied arguments.</summary>
    public ShellInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(command, args, escapeChar), escapeChar)
    {
    }

    private ShellInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    /// <summary>Parses a standalone SHELL instruction in JSON form, preserving formatting and escape context.</summary>
    public static ShellInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    [Obsolete("Use Dockerfile.Parse or Dockerfile.TryParse instead. Parser factories will be removed in the next major version.")]
    public static Parser<ShellInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ShellInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string command, IEnumerable<string> args, char escapeChar)
    {
        Guard.NotNull(command, nameof(command));
        Guard.NotNull(args, nameof(args));
        return GetTokens($"SHELL {StringHelper.FormatAsJson(new string[] { command }.Concat(args))}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Instruction("SHELL", escapeChar,
            GetArgsParser(escapeChar));

    private new static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(ExecFormCommand.GetParser(escapeChar).AsEnumerable(), escapeChar);
}
