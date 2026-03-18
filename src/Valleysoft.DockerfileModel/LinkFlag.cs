using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class LinkFlag : BooleanFlag
{
    private const string Keyword = "link";

    public LinkFlag(char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, escapeChar)
    {
    }

    public LinkFlag(bool value, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, value, escapeChar)
    {
    }

    internal LinkFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static LinkFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new LinkFlag(tokens, esc), escapeChar);

    public static Parser<LinkFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new LinkFlag(tokens, esc), escapeChar);
}
