using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class ChangeModeFlag : KeywordLiteralFlag
{
    private const string Keyword = "chmod";

    public ChangeModeFlag(string permissions, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, permissions, escapeChar)
    {
    }

    internal ChangeModeFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static ChangeModeFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new ChangeModeFlag(tokens, esc), escapeChar);

    public static Parser<ChangeModeFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new ChangeModeFlag(tokens, esc), escapeChar);
}
