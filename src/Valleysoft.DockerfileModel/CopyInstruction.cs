using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class CopyInstruction : FileTransferInstruction
{
    private const string Name = "COPY";
    private readonly char escapeChar;

    public CopyInstruction(IEnumerable<string> sources, string destination,
        string? fromStageName = null, string? changeOwner = null, string? permissions = null,
        bool link = false, bool parents = false, IEnumerable<string>? excludes = null,
        char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(GetTokens(sources, destination, fromStageName, changeOwner, permissions, link, parents, excludes, escapeChar), escapeChar)
    {
        this.escapeChar = escapeChar;
        ExcludeFlagTokens = new TokenList<ExcludeFlag>(TokenList);
        Excludes = new ProjectedItemList<ExcludeFlag, string>(
            ExcludeFlagTokens,
            flag => flag.Value,
            (flag, value) => flag.Value = value);
    }

    private CopyInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        this.escapeChar = escapeChar;
        ExcludeFlagTokens = new TokenList<ExcludeFlag>(TokenList);
        Excludes = new ProjectedItemList<ExcludeFlag, string>(
            ExcludeFlagTokens,
            flag => flag.Value,
            (flag, value) => flag.Value = value);
    }

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
        get => ParentsFlagInternal is not null;
        set
        {
            if (value && ParentsFlagInternal is null)
            {
                ParentsFlagToken = new ParentsFlag(escapeChar);
            }
            else if (!value && ParentsFlagInternal is not null)
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

    public IList<string> Excludes { get; }

    public IList<ExcludeFlag> ExcludeFlagTokens { get; }

    public static CopyInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<CopyInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new CopyInstruction(tokens, escapeChar);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        GetInnerParser(escapeChar, Name,
            ArgTokens(FromFlag.GetParser(escapeChar).AsEnumerable(), escapeChar)
                .Or(ArgTokens(LinkFlag.GetParser(escapeChar).AsEnumerable(), escapeChar))
                .Or(ArgTokens(ParentsFlag.GetParser(escapeChar).AsEnumerable(), escapeChar))
                .Or(ArgTokens(ExcludeFlag.GetParser(escapeChar).AsEnumerable(), escapeChar)));

    private static IEnumerable<Token> GetTokens(IEnumerable<string> sources, string destination,
        string? fromStageName, string? changeOwner, string? permissions, bool link, bool parents,
        IEnumerable<string>? excludes, char escapeChar)
    {
        string fromFlag = fromStageName is null ? "" : new FromFlag(fromStageName, escapeChar).ToString() + " ";
        string linkFlag = link ? new LinkFlag(escapeChar).ToString() + " " : "";
        string parentsFlag = parents ? new ParentsFlag(escapeChar).ToString() + " " : "";
        string excludeFlags = "";
        if (excludes is not null)
        {
            foreach (string pattern in excludes)
            {
                excludeFlags += new ExcludeFlag(pattern, escapeChar).ToString() + " ";
            }
        }
        string trailingFlags = $"{linkFlag}{parentsFlag}{excludeFlags}";
        string text = CreateInstructionString(sources, destination, changeOwner, permissions, escapeChar, Name, fromFlag, trailingFlags);
        return GetTokens(text, GetInnerParser(escapeChar));
    }
}
