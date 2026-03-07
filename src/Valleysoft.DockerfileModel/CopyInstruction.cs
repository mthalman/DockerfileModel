using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class CopyInstruction : FileTransferInstruction
{
    private const string Name = "COPY";

    public CopyInstruction(IEnumerable<string> sources, string destination,
        string? fromStageName = null, string? changeOwner = null, string? permissions = null,
        bool link = false, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(GetTokens(sources, destination, fromStageName, changeOwner, permissions, link, escapeChar), escapeChar)
    {
    }

    private CopyInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
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
        get => LinkFlag is not null;
        set
        {
            if (value && LinkFlag is null)
            {
                LinkFlagToken = new LinkFlag(EscapeChar);
            }
            else if (!value && LinkFlag is not null)
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

    public static CopyInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<CopyInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new CopyInstruction(tokens, escapeChar);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        GetInnerParser(escapeChar, Name,
            ArgTokens(FromFlag.GetParser(escapeChar).AsEnumerable(), escapeChar)
                .Or(ArgTokens(LinkFlag.GetParser(escapeChar).AsEnumerable(), escapeChar)));

    private static IEnumerable<Token> GetTokens(IEnumerable<string> sources, string destination,
        string? fromStageName, string? changeOwner, string? permissions, bool link, char escapeChar)
    {
        string fromFlag = fromStageName is null ? "" : new FromFlag(fromStageName, escapeChar).ToString() + " ";
        string linkFlag = link ? new LinkFlag(escapeChar).ToString() + " " : "";
        string text = CreateInstructionString(sources, destination, changeOwner, permissions, escapeChar, Name, fromFlag, linkFlag);
        return GetTokens(text, GetInnerParser(escapeChar));
    }
}
