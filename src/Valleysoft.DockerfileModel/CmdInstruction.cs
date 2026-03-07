using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class CmdInstruction : CommandInstruction
{
    public CmdInstruction(string commandWithArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(commandWithArgs, escapeChar))
    {
    }

    public CmdInstruction(IEnumerable<string> defaultArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(defaultArgs, escapeChar))
    {
    }

    public CmdInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(command, args, escapeChar))
    {
    }

    private CmdInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static CmdInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<CmdInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new CmdInstruction(tokens);

    internal static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("CMD", escapeChar, GetArgsParser(escapeChar));

    private static IEnumerable<Token> GetTokens(string commandWithArgs, char escapeChar)
    {
        Requires.NotNullOrEmpty(commandWithArgs, nameof(commandWithArgs));
        return GetTokens($"CMD {commandWithArgs}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(IEnumerable<string> defaultArgs, char escapeChar)
    {
        Requires.NotNull(defaultArgs, nameof(defaultArgs));
        return GetTokens($"CMD {StringHelper.FormatAsJson(defaultArgs)}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(string command, IEnumerable<string> args, char escapeChar)
    {
        Requires.NotNullOrEmpty(command, nameof(command));
        Requires.NotNull(args, nameof(args));
        return GetTokens($"CMD {StringHelper.FormatAsJson(new string[] { command }.Concat(args))}", GetInnerParser(escapeChar));
    }
}
