using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class NetworkFlag : KeywordLiteralFlag
{
    private const string Keyword = "network";

    public NetworkFlag(string network, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, network, escapeChar)
    {
    }

    internal NetworkFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static NetworkFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new NetworkFlag(tokens), escapeChar);

    public static Parser<NetworkFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new NetworkFlag(tokens), escapeChar);
}
