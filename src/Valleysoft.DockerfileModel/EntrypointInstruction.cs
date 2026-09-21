using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class EntrypointInstruction : CommandInstruction
{
    public EntrypointInstruction(string commandWithArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(commandWithArgs, escapeChar), escapeChar)
    {
    }

    public EntrypointInstruction(IEnumerable<string> execArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(execArgs, escapeChar), escapeChar)
    {
    }

    public EntrypointInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(command, args, escapeChar), escapeChar)
    {
    }

    private EntrypointInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    public static EntrypointInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<EntrypointInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new EntrypointInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string commandWithArgs, char escapeChar)
    {
        Guard.NotNullOrEmpty(commandWithArgs, nameof(commandWithArgs));
        return GetTokens($"ENTRYPOINT {commandWithArgs}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(IEnumerable<string> execArgs, char escapeChar)
    {
        Guard.NotNull(execArgs, nameof(execArgs));

        return GetTokens($"ENTRYPOINT {StringHelper.FormatAsJson(execArgs)}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(string command, IEnumerable<string> args, char escapeChar)
    {
        Guard.NotNullOrEmpty(command, nameof(command));
        Guard.NotNull(args, nameof(args));
        return GetTokens($"ENTRYPOINT {StringHelper.FormatAsJson(new string[] { command }.Concat(args))}", GetInnerParser(escapeChar));
    }

    internal static EntrypointInstruction ParseDiagnostic(string text, char escapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar, diagnostic: true)), escapeChar);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar, bool diagnostic = false) =>
        Instruction("ENTRYPOINT", escapeChar,
            GetArgsParser(escapeChar, diagnostic));
}
