using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class LinkFlag : BooleanFlag
{
    private const string Keyword = "link";

    public LinkFlag(char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, escapeChar)
    {
    }

    internal LinkFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static LinkFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new LinkFlag(tokens), escapeChar);

    public static Parser<LinkFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new LinkFlag(tokens), escapeChar);
}
