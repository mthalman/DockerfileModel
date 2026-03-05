using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class SecurityFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    public SecurityFlag(string security, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("security", escapeChar), new LiteralToken(security, canContainVariables: true, escapeChar), isFlag: true)
    {
    }

    internal SecurityFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static SecurityFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(
            text,
            KeywordToken.GetParser("security", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new SecurityFlag(tokens),
            escapeChar: escapeChar);

    public static Parser<SecurityFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("security", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new SecurityFlag(tokens),
            escapeChar: escapeChar);
}
