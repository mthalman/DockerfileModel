using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

/// <summary>A COPY instruction with source operands, destination, and optional transfer flags.</summary>
/// <remarks>Modeling flags does not assert that the selected Dockerfile frontend supports them.</remarks>
public class CopyInstruction : FileTransferInstruction
{
    private const string Name = "COPY";

    /// <summary>Creates COPY syntax, selecting JSON form when any source or destination contains a space.</summary>
    public CopyInstruction(IEnumerable<string> sources, string destination,
        string? fromStageName = null, string? changeOwner = null, string? permissions = null,
        bool link = false, bool parents = false, IEnumerable<string>? excludes = null,
        char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(GetTokens(sources, destination, fromStageName, changeOwner, permissions, link, parents, excludes, escapeChar), escapeChar)
    {
        InitializeLists();
    }

    /// <summary>Creates COPY syntax with the specified flags and escape context, without parents or exclusion flags.</summary>
    public CopyInstruction(IEnumerable<string> sources, string destination,
        string? fromStageName, string? changeOwner, string? permissions,
        bool link, char escapeChar)
        : this(sources, destination, fromStageName, changeOwner, permissions, link, false, null, escapeChar)
    {
    }

    private CopyInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        InitializeLists();
    }

    /// <summary>Gets or sets the --from operand; null or empty removes the flag.</summary>
    /// <remarks>Despite its name, this can denote a stage index, stage name, image, or named context; use document analysis for classification.</remarks>
    public string? FromStageName
    {
        get => FromStageNameToken?.Value;
        set => SetOptionalLiteralTokenValue(
            FromStageNameToken, value, token => FromStageNameToken = token, canContainVariables: false, EscapeChar);
    }

    public LiteralToken? FromStageNameToken
    {
        get => FromFlag?.ValueToken;
        set => SetOptionalKeyValueTokenValue(
            FromFlag, value, val => new FromFlag(val, EscapeChar), token => FromFlag = token);
    }

    private FromFlag? FromFlag
    {
        get => Tokens.OfType<FromFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(FromFlag, value);
    }

    public bool Link
    {
        get => LinkFlag?.BoolValue ?? false;
        set
        {
            if (value)
            {
                if (LinkFlag is null)
                {
                    LinkFlagToken = new LinkFlag(EscapeChar);
                }
                else if (!LinkFlag.BoolValue)
                {
                    // Replace explicit =false with a bare flag in-place to preserve position
                    SetToken(LinkFlag, new LinkFlag(EscapeChar));
                }
            }
            else if (LinkFlag is not null)
            {
                LinkFlagToken = null;
            }
        }
    }

    public LinkFlag? LinkFlagToken
    {
        get => LinkFlag;
        set => SetOptionalFlagToken(LinkFlag, value);
    }

    private LinkFlag? LinkFlag
    {
        get => Tokens.OfType<LinkFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(LinkFlag, value);
    }

    public bool Parents
    {
        get => ParentsFlagInternal?.BoolValue ?? false;
        set
        {
            if (value)
            {
                if (ParentsFlagInternal is null)
                {
                    ParentsFlagToken = new ParentsFlag(EscapeChar);
                }
                else if (!ParentsFlagInternal.BoolValue)
                {
                    // Replace explicit =false with a bare flag in-place to preserve position
                    SetToken(ParentsFlagInternal, new ParentsFlag(EscapeChar));
                }
            }
            else if (ParentsFlagInternal is not null)
            {
                ParentsFlagToken = null;
            }
        }
    }

    public ParentsFlag? ParentsFlagToken
    {
        get => ParentsFlagInternal;
        set => SetOptionalFlagToken(ParentsFlagInternal, value);
    }

    private ParentsFlag? ParentsFlagInternal
    {
        get => Tokens.OfType<ParentsFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(ParentsFlagInternal, value);
    }

    /// <summary>Gets the live editable pattern view backed by repeated --exclude flags.</summary>
    public EditableList<string> Excludes { get; private set; } = null!;

    /// <summary>Gets the live --exclude token view, preserving flag identity and formatting.</summary>
    public EditableList<ExcludeFlag> ExcludeFlagTokens { get; private set; } = null!;

    private void InitializeLists()
    {
        ExcludeFlagTokens = new TokenList<ExcludeFlag>(this);
        Excludes = InstructionCollectionEditing.Excludes(ExcludeFlagTokens, this);
    }

    /// <summary>Parses a standalone COPY instruction, preserving its operand form, formatting, and escape context.</summary>
    public static CopyInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<CopyInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new CopyInstruction(tokens, escapeChar);

    internal static CopyInstruction ParseDiagnostic(string text, char escapeChar, InstructionParseContext context) =>
        new(GetTokens(text, GetInnerParser(escapeChar, context)), escapeChar);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar, InstructionParseContext? context = null) =>
        GetInnerParser(escapeChar, Name,
            ArgTokens(FromFlag.GetParser(escapeChar).AsEnumerable(), escapeChar)
                .Or(ArgTokens(LinkFlag.GetParser(escapeChar).AsEnumerable(), escapeChar))
                .Or(ArgTokens(ParentsFlag.GetParser(escapeChar).AsEnumerable(), escapeChar))
                .Or(ArgTokens(ExcludeFlag.GetParser(escapeChar).AsEnumerable(), escapeChar)), context);

    private static IEnumerable<Token> GetTokens(IEnumerable<string> sources, string destination,
        string? fromStageName, string? changeOwner, string? permissions, bool link, bool parents,
        IEnumerable<string>? excludes, char escapeChar)
    {
        string fromFlag = fromStageName is null ? "" : new FromFlag(fromStageName, escapeChar).ToString() + " ";
        string linkFlag = link ? new LinkFlag(escapeChar).ToString() + " " : "";
        string parentsFlag = parents ? new ParentsFlag(escapeChar).ToString() + " " : "";
        string excludeFlags = excludes is not null
            ? string.Join("", excludes.Select(p => $"{new ExcludeFlag(p, escapeChar)} "))
            : "";
        string trailingFlags = $"{linkFlag}{parentsFlag}{excludeFlags}";
        string text = CreateInstructionString(sources, destination, changeOwner, permissions, escapeChar, Name, fromFlag, trailingFlags);
        return GetTokens(text, GetInnerParser(escapeChar));
    }
}
