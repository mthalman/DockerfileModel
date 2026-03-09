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
    /// Returns null when the instruction uses heredoc syntax (no LiteralToken children).
    /// </summary>
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
    /// Returns null when the instruction uses heredoc syntax (no LiteralToken children).
    /// </summary>
    public LiteralToken? DestinationToken
    {
        get => Tokens.OfType<LiteralToken>().LastOrDefault();
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
    public IEnumerable<HeredocToken> Heredocs =>
        this.Tokens.OfType<HeredocToken>();

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
    /// Multiple heredocs per instruction are not supported because HeredocParseImpl
    /// consumes the rest of the marker line as a StringToken, which swallows any
    /// subsequent &lt;&lt;DELIM markers on the same line.
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
