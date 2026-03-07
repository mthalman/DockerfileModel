using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class AddInstruction : FileTransferInstruction
{
    private const string Name = "ADD";
    private readonly char escapeChar;

    public AddInstruction(IEnumerable<string> sources, string destination,
        string? changeOwner = null, string? permissions = null,
        string? checksum = null, bool keepGitDir = false, bool link = false,
        char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(GetTokens(sources, destination, changeOwner, permissions, checksum, keepGitDir, link, escapeChar), escapeChar)
    {
        this.escapeChar = escapeChar;
    }

    private AddInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        this.escapeChar = escapeChar;
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
        get => KeepGitDirFlagToken is not null;
        set
        {
            if (value && KeepGitDirFlagToken is null)
            {
                KeepGitDirFlagToken = new KeepGitDirFlag(escapeChar);
            }
            else if (!value && KeepGitDirFlagToken is not null)
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
        get => LinkFlagToken is not null;
        set
        {
            if (value && LinkFlagToken is null)
            {
                LinkFlagToken = new LinkFlag(escapeChar);
            }
            else if (!value && LinkFlagToken is not null)
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

    public static AddInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<AddInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new AddInstruction(tokens, escapeChar);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        GetInnerParser(escapeChar, Name,
            ArgTokens(ChecksumFlag.GetParser(escapeChar).AsEnumerable(), escapeChar)
                .Or(ArgTokens(KeepGitDirFlag.GetParser(escapeChar).AsEnumerable(), escapeChar))
                .Or(ArgTokens(LinkFlag.GetParser(escapeChar).AsEnumerable(), escapeChar)));

    private static IEnumerable<Token> GetTokens(IEnumerable<string> sources, string destination,
        string? changeOwner, string? permissions, string? checksum, bool keepGitDir, bool link, char escapeChar)
    {
        string checksumFlag = checksum is null ? "" : new ChecksumFlag(checksum, escapeChar).ToString() + " ";
        string keepGitDirFlag = keepGitDir ? new KeepGitDirFlag(escapeChar).ToString() + " " : "";
        string linkFlag = link ? new LinkFlag(escapeChar).ToString() + " " : "";
        string trailingFlags = $"{keepGitDirFlag}{linkFlag}";
        string text = CreateInstructionString(sources, destination, changeOwner, permissions, escapeChar, Name, checksumFlag, trailingFlags);
        return GetTokens(text, GetInnerParser(escapeChar));
    }
}
