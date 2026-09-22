using System.Text;
using System.Text.Json;

using Valleysoft.DockerfileModel.Tokens;


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
            if (ShouldFallbackToShell(remainingText, escapeChar))
            {
                return shellFormParser(input);
            }

            return execResult;
        };
    }

    private static bool ShouldFallbackToShell(string text, char escapeChar)
    {
        if (!CouldStartExecForm(text))
        {
            return true;
        }

        try
        {
            string jsonText = NormalizeLineContinuations(text, escapeChar);
            byte[] utf8Json = Encoding.UTF8.GetBytes(jsonText);
            var reader = new Utf8JsonReader(utf8Json);
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (!IsAllowedTrailingTrivia(jsonText, utf8Json, reader.BytesConsumed))
            {
                return true;
            }

            JsonElement root = document.RootElement;

            return root.ValueKind != JsonValueKind.Array ||
                root.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool CouldStartExecForm(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            char ch = text[index];
            if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
            {
                continue;
            }

            return ch == '[';
        }

        return false;
    }

    private static string NormalizeLineContinuations(string text, char escapeChar)
    {
        int continuationIndex = text.IndexOf(escapeChar);
        if (continuationIndex < 0)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != escapeChar)
            {
                builder.Append(text[index]);
                continue;
            }

            int lookahead = index + 1;
            while (lookahead < text.Length && (text[lookahead] == ' ' || text[lookahead] == '\t'))
            {
                lookahead++;
            }

            if (lookahead < text.Length && text[lookahead] == '\r')
            {
                int lineFeed = lookahead + 1;
                if (lineFeed < text.Length && text[lineFeed] == '\n')
                {
                    builder.Append('\n');
                    index = lineFeed;
                    continue;
                }
            }
            else if (lookahead < text.Length && text[lookahead] == '\n')
            {
                builder.Append('\n');
                index = lookahead;
                continue;
            }

            builder.Append(text[index]);
        }

        return builder.ToString();
    }

    private static bool IsAllowedTrailingTrivia(string text, byte[] utf8Text, long bytesConsumed)
    {
        int index = Encoding.UTF8.GetCharCount(utf8Text, 0, checked((int)bytesConsumed));
        bool atLineStart = false;

        while (index < text.Length)
        {
            char ch = text[index];
            if (ch == ' ' || ch == '\t')
            {
                index++;
                continue;
            }

            if (ch == '\r')
            {
                index++;
                if (index < text.Length && text[index] == '\n')
                {
                    index++;
                }
                atLineStart = true;
                continue;
            }

            if (ch == '\n')
            {
                index++;
                atLineStart = true;
                continue;
            }

            if (ch == '#' && atLineStart)
            {
                while (index < text.Length && text[index] != '\r' && text[index] != '\n')
                {
                    index++;
                }
                continue;
            }

            return false;
        }

        return true;
    }
}
