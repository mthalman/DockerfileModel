using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

/// <summary>An ADD instruction with source operands, destination, and optional transfer flags.</summary>
/// <remarks>Modeling flags does not assert that the selected Dockerfile frontend supports them.</remarks>
public class AddInstruction : FileTransferInstruction
{
    private const string Name = "ADD";
    private readonly char escapeChar;

    /// <summary>Creates ADD syntax, selecting JSON form when any source or destination contains a space.</summary>
    public AddInstruction(IEnumerable<string> sources, string destination,
        string? changeOwner = null, string? permissions = null,
        string? checksum = null, bool keepGitDir = false, bool link = false,
        bool unpack = false, IEnumerable<string>? excludes = null,
        char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(GetTokens(sources, destination, changeOwner, permissions, checksum, keepGitDir, link, unpack, excludes, escapeChar), escapeChar)
    {
        this.escapeChar = escapeChar;
        InitExcludes();
    }

    private AddInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        this.escapeChar = escapeChar;
        InitExcludes();
    }

    private void InitExcludes()
    {
        ExcludeFlagTokens = new Valleysoft.DockerfileModel.Tokens.TokenList<ExcludeFlag>(this);
        Excludes = InstructionCollectionEditing.Excludes(ExcludeFlagTokens, this);
    }

    public string? Checksum
    {
        get => ChecksumToken?.Value;
        set => SetOptionalLiteralTokenValue(ChecksumToken, value, token => ChecksumToken = token, canContainVariables: true, escapeChar);
    }

    public LiteralToken? ChecksumToken
    {
        get => ChecksumFlagToken?.ValueToken;
        set => SetOptionalKeyValueTokenValue(
            ChecksumFlagToken, value, val => new ChecksumFlag(val, escapeChar), token => ChecksumFlagToken = token);
    }

    private ChecksumFlag? ChecksumFlagToken
    {
        get => Tokens.OfType<ChecksumFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(ChecksumFlagToken, value);
    }

    public bool KeepGitDir
    {
        get => KeepGitDirFlagToken?.BoolValue ?? false;
        set
        {
            if (value)
            {
                if (KeepGitDirFlagToken is null)
                {
                    KeepGitDirFlagToken = new KeepGitDirFlag(escapeChar);
                }
                else if (!KeepGitDirFlagToken.BoolValue)
                {
                    // Replace explicit =false with a bare flag in-place to preserve position
                    SetToken(KeepGitDirFlag, new KeepGitDirFlag(escapeChar));
                }
            }
            else if (KeepGitDirFlagToken is not null)
            {
                KeepGitDirFlagToken = null;
            }
        }
    }

    public KeepGitDirFlag? KeepGitDirFlagToken
    {
        get => KeepGitDirFlag;
        set => SetOptionalFlagToken(KeepGitDirFlag, value);
    }

    private KeepGitDirFlag? KeepGitDirFlag
    {
        get => Tokens.OfType<KeepGitDirFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(KeepGitDirFlag, value);
    }

    public bool Link
    {
        get => LinkFlagToken?.BoolValue ?? false;
        set
        {
            if (value)
            {
                if (LinkFlagToken is null)
                {
                    LinkFlagToken = new LinkFlag(escapeChar);
                }
                else if (!LinkFlagToken.BoolValue)
                {
                    // Replace explicit =false with a bare flag in-place to preserve position
                    SetToken(LinkFlag, new LinkFlag(escapeChar));
                }
            }
            else if (LinkFlagToken is not null)
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

    public bool Unpack
    {
        get => UnpackFlagInternal?.BoolValue ?? false;
        set
        {
            if (value)
            {
                if (UnpackFlagInternal is null)
                {
                    UnpackFlagToken = new UnpackFlag(escapeChar);
                }
                else if (!UnpackFlagInternal.BoolValue)
                {
                    // Replace explicit =false with a bare flag in-place to preserve position
                    SetToken(UnpackFlagInternal, new UnpackFlag(escapeChar));
                }
            }
            else if (UnpackFlagInternal is not null)
            {
                UnpackFlagToken = null;
            }
        }
    }

    public UnpackFlag? UnpackFlagToken
    {
        get => UnpackFlagInternal;
        set => SetOptionalFlagToken(UnpackFlagInternal, value);
    }

    private UnpackFlag? UnpackFlagInternal
    {
        get => Tokens.OfType<UnpackFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(UnpackFlagInternal, value);
    }

    /// <summary>Gets the live editable pattern view backed by repeated --exclude flags.</summary>
    public EditableList<string> Excludes { get; private set; } = null!;

    /// <summary>Gets the live --exclude token view, preserving flag identity and formatting.</summary>
    public EditableList<ExcludeFlag> ExcludeFlagTokens { get; private set; } = null!;

    /// <summary>Parses a standalone ADD instruction, preserving its operand form, formatting, and escape context.</summary>
    public static AddInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    internal static TextParser<AddInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new AddInstruction(tokens, escapeChar);

    internal static AddInstruction ParseDiagnostic(string text, char escapeChar, InstructionParseContext context) =>
        new(GetTokens(text, GetInnerParser(escapeChar, context)), escapeChar);

    private static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar, InstructionParseContext? context = null) =>
        GetInnerParser(escapeChar, Name,
            ArgTokens(ChecksumFlag.GetParser(escapeChar).AsEnumerable(), escapeChar)
                .Try().Or(ArgTokens(KeepGitDirFlag.GetParser(escapeChar).AsEnumerable(), escapeChar))
                .Try().Or(ArgTokens(LinkFlag.GetParser(escapeChar).AsEnumerable(), escapeChar))
                .Try().Or(ArgTokens(UnpackFlag.GetParser(escapeChar).AsEnumerable(), escapeChar))
                .Try().Or(ArgTokens(ExcludeFlag.GetParser(escapeChar).AsEnumerable(), escapeChar)), context);

    private static IEnumerable<Token> GetTokens(IEnumerable<string> sources, string destination,
        string? changeOwner, string? permissions, string? checksum, bool keepGitDir, bool link,
        bool unpack, IEnumerable<string>? excludes, char escapeChar)
    {
        string checksumFlag = checksum is null ? "" : new ChecksumFlag(checksum, escapeChar).ToString() + " ";
        string keepGitDirFlag = keepGitDir ? new KeepGitDirFlag(escapeChar).ToString() + " " : "";
        string linkFlag = link ? new LinkFlag(escapeChar).ToString() + " " : "";
        string unpackFlag = unpack ? new UnpackFlag(escapeChar).ToString() + " " : "";
        string excludeFlags = excludes is null ? "" : string.Concat(
            excludes.Select(pattern => new ExcludeFlag(pattern, escapeChar).ToString() + " "));
        string trailingFlags = $"{keepGitDirFlag}{linkFlag}{unpackFlag}{excludeFlags}";
        string text = CreateInstructionString(sources, destination, changeOwner, permissions, escapeChar, Name, checksumFlag, trailingFlags);
        return GetTokens(text, GetInnerParser(escapeChar));
    }
}
