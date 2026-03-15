using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class NetworkFlag : KeywordLiteralFlag
{
    private const string Keyword = "network";

    public NetworkFlag(string network, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, network, escapeChar)
    {
    }

    internal NetworkFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static NetworkFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, (tokens, esc) => new NetworkFlag(tokens, esc), escapeChar);

    public static Parser<NetworkFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, (tokens, esc) => new NetworkFlag(tokens, esc), escapeChar);
}
