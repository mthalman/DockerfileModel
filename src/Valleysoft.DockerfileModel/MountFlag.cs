using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class MountFlag : KeyValueToken<KeywordToken, Mount>
{
    public MountFlag(Mount mount, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("mount", escapeChar), mount, isFlag: true, escapeChar: escapeChar)
    {
    }

    internal MountFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static MountFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(
            text,
            KeywordToken.GetParser("mount", escapeChar),
            MountParser(escapeChar),
            tokens => new MountFlag(tokens, escapeChar),
            escapeChar: escapeChar);

    public static Parser<MountFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("mount", escapeChar),
            MountParser(escapeChar),
            tokens => new MountFlag(tokens, escapeChar),
            escapeChar: escapeChar);

    private static Parser<Mount> MountParser(char escapeChar) =>
        Mount.GetParser(escapeChar);
}
