using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Base class for instructions that contain a command (CMD, ENTRYPOINT, SHELL, RUN).
/// Provides the shared Command property and suppresses variable resolution since
/// commands are shell/runtime-specific.
/// </summary>
public abstract class CommandInstruction : Instruction
{
    protected CommandInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public virtual Command? Command
    {
        get => this.Tokens.OfType<Command>().FirstOrDefault();
        set
        {
            Requires.NotNull(value!, nameof(value));
            Command? current = Command;
            if (current is null)
            {
                throw new InvalidOperationException("No Command token exists to replace.");
            }
            SetToken(current, value);
        }
    }

    public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
    {
        // Do not resolve variables for commands. They are shell/runtime-specific.
        return ToString();
    }

    protected static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        from whitespace in Whitespace()
        from command in ArgTokens(GetCommandParser(escapeChar).AsEnumerable(), escapeChar)
        select ConcatTokens(
            whitespace, command);

    protected static Parser<Command> GetCommandParser(char escapeChar) =>
        ExecFormCommand.GetParser(escapeChar)
            .Cast<ExecFormCommand, Command>()
            .XOr(ShellFormCommand.GetParser(escapeChar));
}
