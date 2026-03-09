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

    internal KeepGitDirFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static KeepGitDirFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new KeepGitDirFlag(tokens), escapeChar);

    public static Parser<KeepGitDirFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new KeepGitDirFlag(tokens), escapeChar);
}
