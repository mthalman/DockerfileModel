using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class KeepGitDirFlag : BooleanFlag
{
    private const string Keyword = "keep-git-dir";

    public KeepGitDirFlag(char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, escapeChar)
    {
    }

    public KeepGitDirFlag(bool value, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, value, escapeChar)
    {
    }

    internal KeepGitDirFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static KeepGitDirFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new KeepGitDirFlag(tokens, esc), escapeChar);

    public static Parser<KeepGitDirFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new KeepGitDirFlag(tokens, esc), escapeChar);
}
