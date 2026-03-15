using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class UnpackFlag : BooleanFlag
{
    private const string Keyword = "unpack";

    public UnpackFlag(char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, escapeChar)
    {
    }

    public UnpackFlag(bool value, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, value, escapeChar)
    {
    }

    internal UnpackFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static UnpackFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new UnpackFlag(tokens, esc), escapeChar);

    public static Parser<UnpackFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new UnpackFlag(tokens, esc), escapeChar);
}
