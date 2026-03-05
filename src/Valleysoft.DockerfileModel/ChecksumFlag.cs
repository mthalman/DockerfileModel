using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class ChecksumFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    public ChecksumFlag(string checksum, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("checksum", escapeChar), new LiteralToken(checksum, canContainVariables: true, escapeChar), isFlag: true)
    {
    }

    internal ChecksumFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public static ChecksumFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(
            text,
            KeywordToken.GetParser("checksum", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new ChecksumFlag(tokens),
            escapeChar: escapeChar);

    public static Parser<ChecksumFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("checksum", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new ChecksumFlag(tokens),
            escapeChar: escapeChar);
}
