using System.Text;
using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class FromInstruction : Instruction
{
    private LiteralToken imageName;
    private readonly char escapeChar;

    public FromInstruction(string imageName, string? stageName = null, string? platform = null,
        char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(imageName, stageName, platform, escapeChar), escapeChar)
    {
    }

    private FromInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
    {
        PlatformFlag? platform = this.PlatformFlag;
        int startIndex = 0;
        if (platform != null)
        {
            startIndex = this.TokenList.IndexOf(platform) + 1;
        }

        this.imageName = this.TokenList
            .Skip(startIndex)
            .OfType<LiteralToken>()
            .First();
        this.escapeChar = escapeChar;
    }

    public string ImageName
    {
        get => this.imageName.Value;
        set
        {
            Requires.NotNullOrEmpty(value, nameof(value));
            this.imageName.Value = value;
        }
    }

    public LiteralToken ImageNameToken
    {
        get => this.imageName;
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(ImageNameToken, value);
            this.imageName = value;
        }
    }

    public string? Platform
    {
        get => this.PlatformFlag?.Value;
        set => SetOptionalLiteralTokenValue(PlatformToken, value, token => PlatformToken = token, canContainVariables: true, escapeChar);
    }

    public LiteralToken? PlatformToken
    {
        get => PlatformFlag?.ValueToken;
        set => SetOptionalKeyValueTokenValue(
            PlatformFlag, value, val => new PlatformFlag(val, escapeChar), token => PlatformFlag = token);
    }

    private PlatformFlag? PlatformFlag
    {
        get => this.Tokens.OfType<PlatformFlag>().FirstOrDefault();
        set => SetOptionalFlagToken(PlatformFlag, value);
    }

    public string? StageName
    {
        get => StageNameToken?.Value;
        set => SetOptionalTokenValue(StageNameToken, value, val => new StageName(val, escapeChar), token => StageNameToken = token);
    }

    public StageName? StageNameToken
    {
        get => this.Tokens.OfType<StageName>().FirstOrDefault();
        set
        {
            SetToken(StageNameToken, value,
                addToken: token =>
                {
                    this.TokenList.AddRange(new Token[]
                    {
                        new WhitespaceToken(" "),
                        new KeywordToken("AS", escapeChar),
                        new WhitespaceToken(" "),
                        token,
                    });
                },
                removeToken: token =>
                {
                    TokenList.RemoveRange(
                        TokenList.FirstPreviousOfType<Token, WhitespaceToken>(TokenList.FirstPreviousOfType<Token, KeywordToken>(token)),
                        token);
                });
        }
    }

    public static FromInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<FromInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new FromInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string imageName, string? stageName, string? platform, char escapeChar)
    {
        Requires.NotNullOrEmpty(imageName, nameof(imageName));

        StringBuilder builder = new("FROM ");
        if (platform is not null)
        {
            builder.Append($"{new PlatformFlag(platform, escapeChar)} ");
        }

        builder.Append(imageName);

        if (stageName is not null)
        {
            builder.Append($" AS {stageName}");
        }

        return GetTokens(builder.ToString(), GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("FROM", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        (from platform in GetPlatformParser(escapeChar).Optional()
        from imageName in GetImageNameParser(escapeChar)
        from stageName in GetStageNameParser(escapeChar).Optional()
        select ConcatTokens(
            platform.GetOrDefault(),
            imageName,
            stageName.GetOrDefault())).End();

    private static Parser<IEnumerable<Token>> GetStageNameParser(char escapeChar) =>
        from asKeyword in ArgTokens(KeywordToken.GetParser("AS", escapeChar).AsEnumerable(), escapeChar)
        from stageName in ArgTokens(DockerfileModel.StageName.GetParser(escapeChar).AsEnumerable(), escapeChar)
        select ConcatTokens(asKeyword, stageName);

    private static Parser<IEnumerable<Token>> GetPlatformParser(char escapeChar) =>
        ArgTokens(PlatformFlag.GetParser(escapeChar).AsEnumerable(), escapeChar);

    private static Parser<IEnumerable<Token>> GetImageNameParser(char escapeChar) =>
        ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar);
}
