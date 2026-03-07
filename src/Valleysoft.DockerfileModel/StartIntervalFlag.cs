using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class StartIntervalFlag : KeywordLiteralFlag
{
    private const string Keyword = "start-interval";

    public StartIntervalFlag(string startInterval, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, startInterval, escapeChar)
    {
    }

    internal StartIntervalFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static StartIntervalFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new StartIntervalFlag(tokens), escapeChar);

    public static Parser<StartIntervalFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new StartIntervalFlag(tokens), escapeChar);
}
