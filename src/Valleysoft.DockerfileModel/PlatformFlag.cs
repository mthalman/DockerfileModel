using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class PlatformFlag : KeywordLiteralFlag
{
    private const string Keyword = "platform";

    public PlatformFlag(string platform, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, platform, escapeChar)
    {
    }

    internal PlatformFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static PlatformFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new PlatformFlag(tokens), escapeChar);

    public static Parser<PlatformFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new PlatformFlag(tokens), escapeChar);
}
