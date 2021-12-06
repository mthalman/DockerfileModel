using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class TimeoutFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    public TimeoutFlag(string timeout, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("timeout", escapeChar), new LiteralToken(timeout, canContainVariables: true, escapeChar), isFlag: true)
    {
    }

    internal TimeoutFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static TimeoutFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(
            text,
            KeywordToken.GetParser("timeout", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new TimeoutFlag(tokens),
            escapeChar: escapeChar);

    public static Parser<TimeoutFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("timeout", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new TimeoutFlag(tokens),
            escapeChar: escapeChar);
}
