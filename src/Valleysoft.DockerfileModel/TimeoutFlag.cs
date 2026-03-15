using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class TimeoutFlag : KeywordLiteralFlag
{
    private const string Keyword = "timeout";

    public TimeoutFlag(string timeout, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, timeout, escapeChar)
    {
    }

    internal TimeoutFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static TimeoutFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new TimeoutFlag(tokens, esc), escapeChar);

    public static Parser<TimeoutFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new TimeoutFlag(tokens, esc), escapeChar);
}
