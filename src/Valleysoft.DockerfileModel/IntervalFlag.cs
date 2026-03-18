using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class IntervalFlag : KeywordLiteralFlag
{
    private const string Keyword = "interval";

    public IntervalFlag(string interval, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, interval, escapeChar)
    {
    }

    internal IntervalFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static IntervalFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new IntervalFlag(tokens, esc), escapeChar);

    public static Parser<IntervalFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new IntervalFlag(tokens, esc), escapeChar);
}
