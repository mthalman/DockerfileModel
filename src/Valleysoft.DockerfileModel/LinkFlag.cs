using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class LinkFlag : AggregateToken
{
    public LinkFlag(char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(GetTokens($"--link", GetInnerParser(escapeChar)))
    {
    }

    internal LinkFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static LinkFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<LinkFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new LinkFlag(tokens);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        from dash1 in Symbol('-').AsEnumerable()
        from dash2 in Symbol('-').AsEnumerable()
        from keyword in KeywordToken.GetParser("link", escapeChar).AsEnumerable()
        select ConcatTokens(dash1, dash2, keyword);
}
