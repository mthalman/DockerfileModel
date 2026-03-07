using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class IntervalFlag : KeywordLiteralFlag
{
    private const string Keyword = "interval";

    public IntervalFlag(string interval, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, interval, escapeChar)
    {
    }

    internal IntervalFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static IntervalFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new IntervalFlag(tokens), escapeChar);

    public static Parser<IntervalFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new IntervalFlag(tokens), escapeChar);
}
