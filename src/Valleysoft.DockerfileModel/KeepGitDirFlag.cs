using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class KeepGitDirFlag : AggregateToken
{
    public KeepGitDirFlag(char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(GetTokens($"--keep-git-dir", GetInnerParser(escapeChar)))
    {
    }

    internal KeepGitDirFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static KeepGitDirFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<KeepGitDirFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new KeepGitDirFlag(tokens);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        from dash1 in Symbol('-').AsEnumerable()
        from dash2 in Symbol('-').AsEnumerable()
        from keyword in KeywordToken.GetParser("keep-git-dir", escapeChar).AsEnumerable()
        select ConcatTokens(dash1, dash2, keyword);
}
