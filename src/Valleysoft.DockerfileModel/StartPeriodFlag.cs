using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class StartPeriodFlag : KeywordLiteralFlag
{
    private const string Keyword = "start-period";

    public StartPeriodFlag(string startPeriod, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, startPeriod, escapeChar)
    {
    }

    internal StartPeriodFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static StartPeriodFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new StartPeriodFlag(tokens), escapeChar);

    public static Parser<StartPeriodFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new StartPeriodFlag(tokens), escapeChar);
}
