using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class SecurityFlag : KeywordLiteralFlag
{
    private const string Keyword = "security";

    public SecurityFlag(string security, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, security, escapeChar)
    {
    }

    internal SecurityFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static SecurityFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new SecurityFlag(tokens, esc), escapeChar);

    public static Parser<SecurityFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new SecurityFlag(tokens, esc), escapeChar);
}
