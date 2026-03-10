using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public abstract class FileTransferInstruction : Instruction
{
    protected FileTransferInstruction(IEnumerable<string> sources, string destination,
        string? changeOwner, string? permissions, char escapeChar, string instructionName)
        : this(GetTokens(sources, destination, changeOwner, permissions, escapeChar, instructionName), escapeChar)
    {
    }

    protected FileTransferInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
    {
        SourceTokens = new TokenList<LiteralToken>(TokenList,
            literals => literals.Take(literals.Count() - 1));
        Sources = new ProjectedItemList<LiteralToken, string>(
            SourceTokens,
            token => token.Value,
            (token, value) => token.Value = value);
        EscapeChar = escapeChar;
    }

    protected char EscapeChar { get; }

    public IList<string> Sources { get; }

    public IList<LiteralToken> SourceTokens { get; }

    /// <summary>
    /// Gets or sets the destination path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nullability is intentionally asymmetric between the getter and setter.
    /// </para>
    /// <para>
    /// The getter returns <see langword="null"/> for heredoc-based instructions where the
    /// destination path is embedded inside the heredoc token rather than expressed as a
    /// separate <see cref="LiteralToken"/>.
    /// </para>
    /// <para>
    /// The setter requires a non-null, non-empty value and throws
    /// <see cref="InvalidOperationException"/> when called on a heredoc-based instruction
    /// (i.e., when the getter would return <see langword="null"/>), because there is no
    /// standalone destination token to update. To author a heredoc-based instruction, use
    /// heredoc syntax directly in the Dockerfile text.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown by the setter when <paramref name="value"/> is null or empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the setter when called on a heredoc-based instruction that has no
    /// standalone destination token.
    /// </exception>
    public string? Destination
    {
        get => DestinationToken?.Value;
        set
        {
            Requires.NotNullOrEmpty(value!, nameof(value));
            LiteralToken? token = DestinationToken;
            if (token is null)
            {
                throw new InvalidOperationException(
                    "Cannot set Destination on a heredoc-based instruction.");
            }
            token.Value = value!;
        }
    }

    /// <summary>
    /// Gets or sets the destination token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nullability is intentionally asymmetric between the getter and setter.
    /// </para>
    /// <para>
    /// The getter returns <see langword="null"/> for heredoc-based instructions where the
    /// destination path is embedded inside the heredoc token rather than expressed as a
    /// separate <see cref="LiteralToken"/>.
    /// </para>
    /// <para>
    /// The setter requires a non-null value and throws <see cref="InvalidOperationException"/>
    /// when called on a heredoc-based instruction (i.e., when no existing destination token
    /// is present to replace), because the setter can only update an existing token in-place.
    /// To author a heredoc-based instruction, use heredoc syntax directly in the Dockerfile text.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the setter when <paramref name="value"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the setter when called on a heredoc-based instruction that has no
    /// standalone destination token to replace.
    /// </exception>
    public LiteralToken? DestinationToken
    {
        get => Tokens.OfType<HeredocToken>().Any()
            ? Tokens.OfType<LiteralToken>().LastOrDefault()
            : Tokens.OfType<LiteralToken>().Last();
        set
        {
            Requires.NotNull(value!, nameof(value));
            LiteralToken? existing = DestinationToken;
            if (existing is null)
            {
                throw new InvalidOperationException(
                    "Cannot set DestinationToken on a heredoc-based instruction.");
            }
            SetToken(existing, value!);
        }
    }

    public string? ChangeOwner
    {
        get => ChangeOwnerFlagToken?.Value;
        set => SetOptionalLiteralTokenValue(ChangeOwnerToken, value, token => ChangeOwnerToken = token, canContainVariables: true, EscapeChar);
    }

    public LiteralToken? ChangeOwnerToken
    {
        get => ChangeOwnerFlagToken?.ValueToken;
        set => SetOptionalKeyValueTokenValue(
            ChangeOwnerFlagToken, value, val => new ChangeOwnerFlag(val, EscapeChar), token => ChangeOwnerFlagToken = token);
    }

    private ChangeOwnerFlag? ChangeOwnerFlagToken
    {
        get => Tokens.OfType<ChangeOwnerFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(ChangeOwnerFlagToken, value);
    }

    public string? Permissions
    {
        get => this.ChangeModeFlagToken?.Value;
        set => SetOptionalLiteralTokenValue(PermissionsToken, value, token => PermissionsToken = token, canContainVariables: true, EscapeChar);
    }

    public LiteralToken? PermissionsToken
    {
        get => ChangeModeFlagToken?.ValueToken;
        set => SetOptionalKeyValueTokenValue(
            ChangeModeFlagToken, value, val => new ChangeModeFlag(val, EscapeChar), token => ChangeModeFlagToken = token);
    }

    private ChangeModeFlag? ChangeModeFlagToken
    {
        get => Tokens.OfType<ChangeModeFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(ChangeModeFlagToken, value);
    }

    protected static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar, string instructionName,
        Parser<IEnumerable<Token>>? optionalFlagParser = null) =>
        Instruction(instructionName, escapeChar, GetArgsParser(escapeChar, optionalFlagParser));

    private static IEnumerable<Token> GetTokens(IEnumerable<string> sources, string destination,
        string? changeOwner, string? permissions, char escapeChar, string instructionName)
    {
        string text = CreateInstructionString(sources, destination, changeOwner, permissions, escapeChar, instructionName, null);
        return GetTokens(text, GetInnerParser(escapeChar, instructionName));
    }

    protected static string CreateInstructionString(IEnumerable<string> sources, string destination,
        string? changeOwner, string? permissions, char escapeChar, string instructionName, string? optionalFlag,
        string? trailingOptionalFlag = null)
    {
        Requires.NotNullEmptyOrNullElements(sources, nameof(sources));
        Requires.NotNullOrEmpty(destination, nameof(destination));

        IEnumerable<string> locations = sources.Append(destination);

        string changeOwnerFlagStr = changeOwner is null ?
            string.Empty :
            $"{new ChangeOwnerFlag(changeOwner, escapeChar)} ";

        string changeModeFlagStr = permissions is null ?
            string.Empty :
            $"{new ChangeModeFlag(permissions, escapeChar)} ";

        string flags = $"{optionalFlag}{changeOwnerFlagStr}{changeModeFlagStr}{trailingOptionalFlag}";

        bool useJsonForm = locations.Any(loc => loc.Contains(' '));
        if (useJsonForm)
        {
            return $"{instructionName} {flags}{StringHelper.FormatAsJson(locations)}";
        }
        else
        {
            return $"{instructionName} {flags}{String.Join(" ", locations.ToArray())}";
        }
    }

    /// <summary>
    /// Gets the heredoc tokens contained in this file transfer instruction.
    /// Empty if the instruction uses regular file arguments.
    /// </summary>
    public IEnumerable<HeredocToken> HeredocTokens =>
        this.Tokens.OfType<HeredocToken>();

    /// <summary>
    /// Gets the body content of each heredoc in this file transfer instruction as a string.
    /// Empty if the instruction uses regular file arguments.
    /// </summary>
    public IEnumerable<string> Heredocs =>
        HeredocTokens.Select(h => h.Body);

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar, Parser<IEnumerable<Token>>? optionalFlagParser) =>
        from flags in ArgTokens(
            from flag in FlagOption(escapeChar, optionalFlagParser).Optional()
            select flag.GetOrDefault(), escapeChar).Many().Flatten()
        from whitespace in Whitespace()
        from files in HeredocFileArgs()
            .Or(ArgTokens(JsonArray(escapeChar, canContainVariables: true), escapeChar))
            .Or(
                from literals in ArgTokens(
                    LiteralWithVariables(escapeChar).AsEnumerable(),
                    escapeChar).Many()
                select literals.Flatten())
        select ConcatTokens(flags, whitespace, files);

    /// <summary>
    /// Parses a single heredoc construct as file transfer instruction arguments.
    /// Syntax: COPY/ADD &lt;&lt;DELIM [destination]\n body \n DELIM
    ///
    /// Known limitations:
    /// <list type="bullet">
    ///   <item>Multiple heredocs per instruction are not supported because
    ///   <see cref="ParseHelper.HeredocParseImpl"/> consumes the rest of the marker line
    ///   as a StringToken, which swallows any subsequent &lt;&lt;DELIM markers.</item>
    ///   <item>The destination path that follows the marker on the first line
    ///   (e.g. the "/dest" in "COPY &lt;&lt;EOF /dest") is absorbed into the heredoc token's
    ///   rest-of-line StringToken and is therefore not returned as a separate
    ///   <see cref="LiteralToken"/>. Consequently <see cref="FileTransferInstruction.Destination"/>
    ///   and <see cref="FileTransferInstruction.Sources"/> are null/empty for heredoc-based
    ///   instructions. Fixing this requires stopping the heredoc token at the delimiter and
    ///   re-tokenizing the remainder; that change is deferred to avoid broad parser risk.</item>
    /// </list>
    /// </summary>
    private static Parser<IEnumerable<Token>> HeredocFileArgs() =>
        from heredocs in Heredoc().Once()
        select heredocs.Cast<Token>();

    private static Parser<IEnumerable<Token>?> FlagOption(char escapeChar, Parser<IEnumerable<Token>>? optionalFlagParser) =>
        ChangeOwnerFlag.GetParser(escapeChar)
            .Cast<ChangeOwnerFlag, Token>()
            .AsEnumerable()
            .Or(ChangeModeFlag.GetParser(escapeChar).AsEnumerable())
            .Or(optionalFlagParser ?? Parse.Return<IEnumerable<Token>?>(null));
}
