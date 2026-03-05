using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class NetworkFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    public NetworkFlag(string network, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("network", escapeChar), new LiteralToken(network, canContainVariables: true, escapeChar), isFlag: true)
    {
    }

    internal NetworkFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static NetworkFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(
            text,
            KeywordToken.GetParser("network", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new NetworkFlag(tokens),
            escapeChar: escapeChar);

    public static Parser<NetworkFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("network", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new NetworkFlag(tokens),
            escapeChar: escapeChar);
}
