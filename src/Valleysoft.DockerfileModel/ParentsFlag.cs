using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class ParentsFlag : BooleanFlag
{
    private const string Keyword = "parents";

    public ParentsFlag(char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, escapeChar)
    {
    }

    public ParentsFlag(bool value, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, value, escapeChar)
    {
    }

    internal ParentsFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static ParentsFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new ParentsFlag(tokens, esc), escapeChar);

    public static Parser<ParentsFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new ParentsFlag(tokens, esc), escapeChar);
}
