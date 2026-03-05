using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class ChecksumFlag : KeywordLiteralFlag
{
    private const string Keyword = "checksum";

    public ChecksumFlag(string checksum, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(Keyword, checksum, escapeChar)
    {
    }

    internal ChecksumFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static ChecksumFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        ParseFlag(text, Keyword, tokens => new ChecksumFlag(tokens), escapeChar);

    public static Parser<ChecksumFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetFlagParser(Keyword, tokens => new ChecksumFlag(tokens), escapeChar);
}
