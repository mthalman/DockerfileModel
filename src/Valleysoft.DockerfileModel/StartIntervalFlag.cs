using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class StartIntervalFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    public StartIntervalFlag(string startInterval, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("start-interval", escapeChar), new LiteralToken(startInterval, canContainVariables: true, escapeChar), isFlag: true)
    {
    }

    internal StartIntervalFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static StartIntervalFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(
            text,
            KeywordToken.GetParser("start-interval", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new StartIntervalFlag(tokens),
            escapeChar: escapeChar);

    public static Parser<StartIntervalFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("start-interval", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new StartIntervalFlag(tokens),
            escapeChar: escapeChar);
}
