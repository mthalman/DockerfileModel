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
    /// Gets or sets the destination path. For heredoc instructions, the destination is
    /// properly tokenized as a separate LiteralToken after the marker.
    /// </summary>
    public string? Destination
    {
        get
        {
            LiteralToken? destToken = DestinationToken;
            return destToken?.Value;
        }
        set
        {
            Requires.NotNullOrEmpty(value!, nameof(value));
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
            Requires.NotNull(value!, nameof(value));
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
    /// Gets the paired heredoc marker+body objects in this instruction.
    /// Association is positional: first marker pairs with first body, etc.
    /// </summary>
    public IReadOnlyList<Heredoc> Heredocs
    {
        get
        {
            var markerList = HeredocMarkerTokens.ToList();
            var bodyList = HeredocBodyTokens.ToList();
            int count = Math.Min(markerList.Count, bodyList.Count);
            List<Heredoc> result = new(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(new Heredoc(markerList[i], bodyList[i]));
            }
            return result;
        }
    }

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

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar, Parser<IEnumerable<Token>>? optionalFlagParser) =>
        from flags in FlagOption(escapeChar, optionalFlagParser).Many().Flatten()
        from whitespace in Whitespace()
        from files in HeredocTokenParser(escapeChar)
            .Or(ArgTokens(JsonArray(escapeChar, canContainVariables: true, allowEmpty: true), escapeChar))
            .Or(from literals in ArgTokens(
                    LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes).AsEnumerable(),
                    escapeChar).Many()
                select literals.Flatten())
        select ConcatTokens(flags, whitespace, files);

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
