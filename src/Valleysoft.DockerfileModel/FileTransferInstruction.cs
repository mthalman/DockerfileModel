using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

/// <summary>Shared operand and flag model for ADD and COPY instructions.</summary>
/// <remarks>
/// Source collections are live syntax-aware views; the destination is a separate required operand.
/// Constructors choose JSON form when any supplied source or destination contains a space, otherwise
/// space-separated form. They retain the supplied escape context for later edits.
/// </remarks>
public abstract partial class FileTransferInstruction : Instruction
{
    protected FileTransferInstruction(IEnumerable<string> sources, string destination,
        string? changeOwner, string? permissions, char escapeChar, string instructionName)
        : this(GetTokens(sources, destination, changeOwner, permissions, escapeChar, instructionName), escapeChar)
    {
    }

    protected FileTransferInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        SourceTokens = new TokenList<LiteralToken>(this,
            literals => literals.Take(literals.Count() - 1));
        Sources = InstructionCollectionEditing.Strings(SourceTokens, this);
        EscapeChar = escapeChar;
    }

    protected char EscapeChar { get; }

    /// <summary>Gets the live editable source value view, excluding the destination and heredoc definitions.</summary>
    /// <remarks>
    /// New strings supply semantic values. In JSON form, values containing double quotes,
    /// backslashes, or U+0000–U+001F are rejected because this view does not implement JSON escaping.
    /// Use <see cref="SourceTokens"/> for valid encoded syntax within the existing operand grammar.
    /// Removing the last required source is rejected; heredoc definitions are edited separately.
    /// </remarks>
    public EditableList<string> Sources { get; }

    /// <summary>Gets the live editable source token view, excluding the destination and heredoc definitions.</summary>
    /// <remarks>
    /// In JSON form, inserted and non-self replacement tokens must form one JSON string when
    /// double-quoted and normalized for Dockerfile continuations and physical comments.
    /// Valid encoded syntax is preserved; rejection leaves incoming tokens unchanged.
    /// Existing operand grammar checks still apply.
    /// </remarks>
    public EditableList<LiteralToken> SourceTokens { get; }

    /// <summary>
    /// Gets or sets the destination path. For heredoc instructions, the destination is
    /// properly tokenized as a separate LiteralToken after the marker.
    /// </summary>
    /// <remarks>The getter can return null for a missing operand, but the setter rejects null and empty values and cannot insert a missing destination.</remarks>
    public string? Destination
    {
        get
        {
            LiteralToken? destToken = DestinationToken;
            return destToken?.Value;
        }
        set
        {
            Guard.NotNullOrEmpty(value!, nameof(value));
            LiteralToken? destToken = DestinationToken;
            if (destToken is null)
            {
                throw new InvalidOperationException("No destination token exists to update.");
            }
            destToken.Value = value;
        }
    }

    /// <summary>
    /// Gets or sets the destination token. For heredoc instructions, the destination
    /// is a LiteralToken that appears after the marker in the command stream.
    /// Returns null only if no LiteralToken exists.
    /// </summary>
    public LiteralToken? DestinationToken
    {
        get => Tokens.OfType<LiteralToken>().LastOrDefault();
        set
        {
            Guard.NotNull(value!, nameof(value));
            LiteralToken? current = DestinationToken;
            if (current is null)
            {
                throw new InvalidOperationException("No destination token exists to replace.");
            }
            SetToken(current, value);
        }
    }

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
        GetInnerParser(escapeChar, instructionName, optionalFlagParser, null);

    private protected static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar, string instructionName,
        Parser<IEnumerable<Token>>? optionalFlagParser, InstructionParseContext? context) =>
        Instruction(instructionName, escapeChar, GetArgsParser(escapeChar, optionalFlagParser, context));

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
        Guard.NotNullEmptyOrNullElements(sources, nameof(sources));
        Guard.NotNullOrEmpty(destination, nameof(destination));

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

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar,
        Parser<IEnumerable<Token>>? optionalFlagParser, InstructionParseContext? context) =>
        from flags in FlagOption(escapeChar, optionalFlagParser).Many().Flatten()
        from whitespace in Whitespace()
        from files in context is { HasHeredocs: true }
            ? context.HeredocParser(escapeChar, canContainVariables: true)
            : context is null
                ? HeredocTokenParser(escapeChar).Or(FileArgs(escapeChar))
                : FileArgs(escapeChar)
        select ConcatTokens(flags, whitespace, files);

    private static Parser<IEnumerable<Token>> FileArgs(char escapeChar) =>
        ArgTokens(JsonArray(escapeChar, canContainVariables: true, allowEmpty: true), escapeChar)
            .Or(from literals in ArgTokens(
                    LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes).AsEnumerable(),
                    escapeChar).Many()
                select literals.Flatten());

    private static Parser<IEnumerable<Token>> FlagOption(char escapeChar, Parser<IEnumerable<Token>>? optionalFlagParser)
    {
        Parser<IEnumerable<Token>> parser =
            ArgTokens(ChangeOwnerFlag.GetParser(escapeChar)
                .Cast<ChangeOwnerFlag, Token>()
                .AsEnumerable(), escapeChar)
            .Or(ArgTokens(ChangeModeFlag.GetParser(escapeChar).AsEnumerable(), escapeChar));

        if (optionalFlagParser is not null)
        {
            parser = parser.Or(optionalFlagParser);
        }

        return parser;
    }
}
