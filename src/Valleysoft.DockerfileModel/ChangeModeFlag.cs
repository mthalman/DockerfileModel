using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class ChangeModeFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    public ChangeModeFlag(string permissions, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("chmod", escapeChar), new LiteralToken(permissions, canContainVariables: true, escapeChar), isFlag: true)
    {
    }

    internal ChangeModeFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static ChangeModeFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(text,
            KeywordToken.GetParser("chmod", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new ChangeModeFlag(tokens),
            escapeChar: escapeChar);

    public static Parser<ChangeModeFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("chmod", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new ChangeModeFlag(tokens),
            escapeChar: escapeChar);
}
