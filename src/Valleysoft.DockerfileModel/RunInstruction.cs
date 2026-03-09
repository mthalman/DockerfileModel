using System.Text;
using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class RunInstruction : CommandInstruction
{
    private readonly char escapeChar;

    public RunInstruction(string commandWithArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(commandWithArgs, Enumerable.Empty<Mount>(), null, null, escapeChar)
    {
    }

    public RunInstruction(string commandWithArgs, IEnumerable<Mount> mounts,
        string? network = null, string? security = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(commandWithArgs, mounts, network, security, escapeChar), escapeChar)
    {
    }

    public RunInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(command, args, Enumerable.Empty<Mount>(), null, null, escapeChar)
    {
    }

    public RunInstruction(string command, IEnumerable<string> args, IEnumerable<Mount> mounts,
        string? network = null, string? security = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(command, args, mounts, network, security, escapeChar), escapeChar)
    {
    }

    private RunInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
    {
        this.escapeChar = escapeChar;
        Mounts = new ProjectedItemList<MountFlag, Mount>(
            new TokenList<MountFlag>(TokenList),
            flag => flag.ValueToken
                ?? throw new InvalidOperationException("MountFlag.ValueToken cannot be null when accessing RunInstruction.Mounts."),
            (flag, mount) => flag.ValueToken = mount);
    }

    /// <summary>
    /// Gets or sets the command of this RUN instruction.
    /// Returns null when the instruction uses heredoc syntax.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when attempting to set the command on a heredoc-based RUN instruction.
    /// Heredoc instructions do not have a <see cref="Command"/> token to replace.
    /// </exception>
    public new Command? Command
    {
        get => this.Tokens.OfType<Command>().FirstOrDefault();
        set
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            Command? existing = Command;
            if (existing is null)
            {
                throw new InvalidOperationException(
                    "Cannot set Command on a heredoc-based RUN instruction. " +
                    "Heredoc instructions do not support command assignment.");
            }
            SetToken(existing, value);
        }
    }

    /// <summary>
    /// Gets the heredoc tokens contained in this RUN instruction.
    /// Empty if the instruction uses exec-form or shell-form commands.
    /// </summary>
    public IEnumerable<HeredocToken> Heredocs =>
        this.Tokens.OfType<HeredocToken>();

    public IList<Mount> Mounts { get; }

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

    public static RunInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<RunInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new RunInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string commandWithArgs, IEnumerable<Mount> mounts,
        string? network, string? security, char escapeChar)
    {
        Requires.NotNullOrEmpty(commandWithArgs, nameof(commandWithArgs));
        Requires.NotNull(mounts, nameof(mounts));

        return GetTokens($"RUN {GetFlagArgs(mounts, network, security, escapeChar)}{commandWithArgs}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(string command, IEnumerable<string> args, IEnumerable<Mount> mounts,
        string? network, string? security, char escapeChar)
    {
        Requires.NotNullOrEmpty(command, nameof(command));
        Requires.NotNull(args, nameof(args));
        Requires.NotNull(mounts, nameof(mounts));

        return GetTokens(
            $"RUN {GetFlagArgs(mounts, network, security, escapeChar)}{StringHelper.FormatAsJson(new string[] { command }.Concat(args))}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("RUN", escapeChar,
            GetArgsParser(escapeChar));

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

    private new static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        from options in Options(escapeChar)
        from whitespace in Whitespace()
        from command in ArgTokens(HeredocArgs(), escapeChar)
            .Or(ArgTokens(GetCommandParser(escapeChar).AsEnumerable(), escapeChar))
        select ConcatTokens(options, whitespace, command);

    /// <summary>
    /// Parses a single heredoc construct as the RUN instruction arguments.
    /// Multiple heredocs per instruction (e.g., RUN &lt;&lt;FILE1 &lt;&lt;FILE2) are not supported
    /// because HeredocParseImpl consumes the rest of the marker line as a StringToken,
    /// which swallows any subsequent &lt;&lt;DELIM markers on the same line.
    /// </summary>
    private static Parser<IEnumerable<Token>> HeredocArgs() =>
        from heredocs in Heredoc().Once()
        select heredocs.Cast<Token>();

    private static Parser<IEnumerable<Token>> Options(char escapeChar) =>
        ArgTokens(
            MountFlag.GetParser(escapeChar).Cast<MountFlag, Token>()
                .Or(NetworkFlag.GetParser(escapeChar))
                .Or(SecurityFlag.GetParser(escapeChar)).AsEnumerable(),
            escapeChar)
            .Many().Flatten();

    private new static Parser<Command> GetCommandParser(char escapeChar) =>
        ExecFormCommand.GetParser(escapeChar)
            .Cast<ExecFormCommand, Command>()
            .Or(ShellFormCommand.GetParser(escapeChar));
}
