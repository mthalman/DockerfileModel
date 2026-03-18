using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class ExcludeFlag : KeywordLiteralFlag
{
    private const string Keyword = "exclude";

    public ExcludeFlag(string pattern, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, pattern, escapeChar)
    {
    }

    internal ExcludeFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static ExcludeFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new ExcludeFlag(tokens, esc), escapeChar);

    public static Parser<ExcludeFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new ExcludeFlag(tokens, esc), escapeChar);
}
