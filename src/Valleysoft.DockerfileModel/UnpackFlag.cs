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

    internal UnpackFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static UnpackFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new UnpackFlag(tokens), escapeChar);

    public static Parser<UnpackFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new UnpackFlag(tokens), escapeChar);
}
