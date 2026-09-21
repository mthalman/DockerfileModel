using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class ShellInstruction : CommandInstruction
{
    public ShellInstruction(string command, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(command, Enumerable.Empty<string>(), escapeChar)
    {
    }

    public ShellInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(command, args, escapeChar), escapeChar)
    {
    }

    private ShellInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    public static ShellInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

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
