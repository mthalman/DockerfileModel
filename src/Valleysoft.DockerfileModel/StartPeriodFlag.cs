using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class StartPeriodFlag : KeywordLiteralFlag
{
    private const string Keyword = "start-period";

    public StartPeriodFlag(string startPeriod, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, startPeriod, escapeChar)
    {
    }

    internal StartPeriodFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static StartPeriodFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new StartPeriodFlag(tokens, esc), escapeChar);

    public static Parser<StartPeriodFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new StartPeriodFlag(tokens, esc), escapeChar);
}
