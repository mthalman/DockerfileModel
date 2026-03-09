using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class ExcludeFlag : KeywordLiteralFlag
{
    private const string Keyword = "exclude";

    public ExcludeFlag(string pattern, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, pattern, escapeChar)
    {
    }

    internal ExcludeFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static ExcludeFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new ExcludeFlag(tokens), escapeChar);

    public static Parser<ExcludeFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new ExcludeFlag(tokens), escapeChar);
}
