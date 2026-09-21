using System.Text;
using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.BasicParsers;
using static Valleysoft.DockerfileModel.Parsing.HeredocParsers;
using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;
using static Valleysoft.DockerfileModel.Parsing.TokenSequences;

namespace Valleysoft.DockerfileModel;

/// <summary>A RUN instruction containing a shell command, JSON exec command, or heredoc content.</summary>
public partial class RunInstruction : CommandInstruction
{
    private readonly char escapeChar;

    /// <summary>Parses raw command text after RUN; ordinary text uses shell form, while JSON-array text selects exec form.</summary>
    public RunInstruction(string commandWithArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(commandWithArgs, Enumerable.Empty<Mount>(), null, null, escapeChar)
    {
    }

    /// <summary>Creates RUN from raw command text with mount and optional network/security flags.</summary>
    /// <remarks>The command text is parsed, not JSON-encoded; use the command-and-arguments overload for explicit exec form.</remarks>
    public RunInstruction(string commandWithArgs, IEnumerable<Mount> mounts,
        string? network = null, string? security = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(commandWithArgs, mounts, network, security, escapeChar), escapeChar)
    {
    }

    /// <summary>Creates JSON exec-form RUN with the command followed by its arguments.</summary>
    public RunInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(command, args, Enumerable.Empty<Mount>(), null, null, escapeChar)
    {
    }

    /// <summary>Creates JSON exec-form RUN with arguments and mount, network, and security flags.</summary>
    public RunInstruction(string command, IEnumerable<string> args, IEnumerable<Mount> mounts,
        string? network = null, string? security = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(command, args, mounts, network, security, escapeChar), escapeChar)
    {
    }

    private RunInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        this.escapeChar = escapeChar;
        Mounts = new ProjectedItemList<MountFlag, Mount>(
            new TokenList<MountFlag>(this),
            flag => flag.ValueToken
                ?? throw new InvalidOperationException("MountFlag.ValueToken cannot be null when accessing RunInstruction.Mounts."),
            mount =>
            {
                Guard.NotNull(mount, nameof(mount));
                if (mount.EditingEscapeChar != escapeChar)
                {
                    throw new InvalidOperationException("The mount uses an incompatible escape character.");
                }
                return new MountFlag(mount, escapeChar);
            },
            ReferenceComparer<Mount>.Instance);
    }

    /// <summary>
    /// Gets or sets the command. Returns null when the instruction uses heredoc syntax.
    /// </summary>
    public override Command? Command
    {
        get => this.Tokens.OfType<Command>().FirstOrDefault();
        set
        {
            if (HeredocMarkerTokens.Any())
            {
                throw new InvalidOperationException("Cannot set Command on a heredoc RUN instruction.");
            }

            Guard.NotNull(value!, nameof(value));
            Command? current = Command;
            if (current is null)
            {
                throw new InvalidOperationException("No Command token exists to replace.");
            }
            SetToken(current, value);
        }
    }

    /// <summary>Gets the live syntax-aware mount view backed by --mount flags.</summary>
    /// <remarks>Inserted mounts must use this instruction's retained escape context. Removing a mount removes its flag.</remarks>
    public EditableList<Mount> Mounts { get; }

    /// <summary>
    /// Gets the heredoc marker tokens in this instruction.
    /// </summary>
    public IEnumerable<HeredocMarkerToken> HeredocMarkerTokens => Tokens.OfType<HeredocMarkerToken>();

    /// <summary>
    /// Gets the heredoc body tokens in this instruction.
    /// </summary>
    public IEnumerable<HeredocBodyToken> HeredocBodyTokens => Tokens.OfType<HeredocBodyToken>();

    /// <summary>
    /// Gets the heredoc tokens in this instruction (marker tokens, for backward compatibility checks).
    /// </summary>
    public IEnumerable<HeredocMarkerToken> HeredocTokens => HeredocMarkerTokens;

    public string? Network
    {
        get => NetworkToken?.Value;
        set => SetOptionalLiteralTokenValue(NetworkToken, value, token => NetworkToken = token, canContainVariables: true, escapeChar);
    }

    public LiteralToken? NetworkToken
    {
        get => NetworkFlag?.ValueToken;
        set => SetOptionalKeyValueTokenValue(
            NetworkFlag, value, val => new NetworkFlag(val, escapeChar), token => NetworkFlag = token);
    }

    private NetworkFlag? NetworkFlag
    {
        get => Tokens.OfType<NetworkFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(NetworkFlag, value);
    }

    public string? Security
    {
        get => SecurityToken?.Value;
        set => SetOptionalLiteralTokenValue(SecurityToken, value, token => SecurityToken = token, canContainVariables: true, escapeChar);
    }

    public LiteralToken? SecurityToken
    {
        get => SecurityFlag?.ValueToken;
        set => SetOptionalKeyValueTokenValue(
            SecurityFlag, value, val => new SecurityFlag(val, escapeChar), token => SecurityFlag = token);
    }

    private SecurityFlag? SecurityFlag
    {
        get => Tokens.OfType<SecurityFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(SecurityFlag, value);
    }

    /// <summary>Parses a standalone RUN instruction, preserving its command form, formatting, and escape context.</summary>
    public static RunInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<RunInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new RunInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string commandWithArgs, IEnumerable<Mount> mounts,
        string? network, string? security, char escapeChar)
    {
        Guard.NotNullOrEmpty(commandWithArgs, nameof(commandWithArgs));
        Guard.NotNull(mounts, nameof(mounts));

        return GetTokens($"RUN {GetFlagArgs(mounts, network, security, escapeChar)}{commandWithArgs}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(string command, IEnumerable<string> args, IEnumerable<Mount> mounts,
        string? network, string? security, char escapeChar)
    {
        Guard.NotNullOrEmpty(command, nameof(command));
        Guard.NotNull(args, nameof(args));
        Guard.NotNull(mounts, nameof(mounts));

        return GetTokens(
            $"RUN {GetFlagArgs(mounts, network, security, escapeChar)}{StringHelper.FormatAsJson(new string[] { command }.Concat(args))}", GetInnerParser(escapeChar));
    }

    internal static RunInstruction ParseDiagnostic(string text, char escapeChar, InstructionParseContext context) =>
        new(GetTokens(text, GetInnerParser(escapeChar, context)), escapeChar);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar, InstructionParseContext? context = null) =>
        Instruction("RUN", escapeChar,
            GetArgsParser(escapeChar, context));

    private static string GetFlagArgs(IEnumerable<Mount> mounts, string? network, string? security, char escapeChar)
    {
        StringBuilder builder = new();

        foreach (Mount mount in mounts)
        {
            builder.Append($"{new MountFlag(mount, escapeChar)} ");
        }

        if (network is not null)
        {
            builder.Append($"{new NetworkFlag(network, escapeChar)} ");
        }

        if (security is not null)
        {
            builder.Append($"{new SecurityFlag(security, escapeChar)} ");
        }

        return builder.ToString();
    }

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar, InstructionParseContext? context) =>
        from options in Options(escapeChar)
        from whitespace in Whitespace()
        from command in context is { HasHeredocs: true }
            ? context.HeredocParser(escapeChar)
            : context is null
                ? HeredocTokenParser(escapeChar).Or(ArgTokens(GetCommandParser(escapeChar, false).AsEnumerable(), escapeChar))
                : ArgTokens(GetCommandParser(escapeChar, true).AsEnumerable(), escapeChar)
        select ConcatTokens(options, whitespace, command);

    private static Parser<IEnumerable<Token>> Options(char escapeChar) =>
        ArgTokens(
            MountFlag.GetParser(escapeChar).Cast<MountFlag, Token>()
                .Or(NetworkFlag.GetParser(escapeChar))
                .Or(SecurityFlag.GetParser(escapeChar)).AsEnumerable(),
            escapeChar)
            .Many().Flatten();

    private new static Parser<Command> GetCommandParser(char escapeChar, bool diagnostic) =>
        ExecFormCommand.GetParser(escapeChar)
            .Cast<ExecFormCommand, Command>()
            .Or(diagnostic ? ShellFormCommand.GetDiagnosticParser(escapeChar) : ShellFormCommand.GetParser(escapeChar));
}
