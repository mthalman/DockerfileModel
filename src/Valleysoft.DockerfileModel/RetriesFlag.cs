using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class RetriesFlag : KeywordLiteralFlag
{
    private const string Keyword = "retries";

    public RetriesFlag(string retryCount, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, retryCount, escapeChar)
    {
    }

    internal RetriesFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static RetriesFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new RetriesFlag(tokens), escapeChar);

    public static Parser<RetriesFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new RetriesFlag(tokens), escapeChar);
}
