using System.Text.Json;

using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.BasicParsers;
using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;
using static Valleysoft.DockerfileModel.Parsing.TokenSequences;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Base class for instructions that contain a command (CMD, ENTRYPOINT, SHELL, RUN).
/// Provides the shared Command property and suppresses variable resolution since
/// commands are shell/runtime-specific.
/// </summary>
/// <remarks>
/// Constructors accepting argument sequences surround each element with JSON quotes but do not implement
/// general JSON escaping. Embedded quotes, backslashes, and control characters require valid encoded syntax.
/// </remarks>
public abstract class CommandInstruction : Instruction
{
    protected CommandInstruction(IEnumerable<Token> tokens) : this(tokens, Dockerfile.DefaultEscapeChar)
    {
    }

    protected CommandInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    /// <summary>Gets or replaces the command token, allowing an explicit change between shell and exec forms.</summary>
    /// <remarks>This base setter requires a non-null replacement and an existing command; heredoc RUN has additional restrictions.</remarks>
    public virtual Command? Command
    {
        get => this.Tokens.OfType<Command>().FirstOrDefault();
        set
        {
            Guard.NotNull(value!, nameof(value));
            Command? current = Command;
            if (current is null)
            {
                throw new InvalidOperationException("No Command token exists to replace.");
            }
            SetToken(current, value);
        }
    }

    /// <summary>Returns the unchanged instruction text without expanding runtime command variables.</summary>
    /// <remarks>The supplied environment and resolution options, including escape removal and inline updates, are ignored.</remarks>
    public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
    {
        // Do not resolve variables for commands. They are shell/runtime-specific.
        return ToString();
    }

    protected static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        GetArgsParser(escapeChar, false);

    private protected static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar, bool diagnostic) =>
        from whitespace in Whitespace()
        from command in ArgTokens(GetCommandParser(escapeChar, diagnostic).AsEnumerable(), escapeChar)
        select ConcatTokens(
            whitespace, command);

    protected static Parser<Command> GetCommandParser(char escapeChar) =>
        GetCommandParser(escapeChar, false);

    internal static Parser<Command> GetCommandParser(char escapeChar, bool diagnostic)
    {
        Parser<Command> execFormParser = ExecFormCommand.GetParser(escapeChar).Cast<ExecFormCommand, Command>();
        Parser<Command> shellFormParser = (diagnostic ? ShellFormCommand.GetDiagnosticParser(escapeChar) : ShellFormCommand.GetParser(escapeChar))
            .Cast<ShellFormCommand, Command>();

        return input =>
        {
            IResult<Command> execResult = execFormParser(input);
            if (execResult.WasSuccessful)
            {
                return execResult;
            }

            string remainingText = input.Source.Substring(input.Position);
            if (ShouldFallbackToShell(remainingText))
            {
                return shellFormParser(input);
            }

            return Result.Failure<Command>(input, "Expected a valid JSON exec-form command or shell-form command.", new[] { "command" });
        };
    }

    private static bool ShouldFallbackToShell(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;

            return root.ValueKind != JsonValueKind.Array ||
                root.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String);
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
