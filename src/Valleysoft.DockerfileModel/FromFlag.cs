using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class FromFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    public FromFlag(string stageName, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("from", escapeChar), new LiteralToken(stageName, canContainVariables: false, escapeChar), isFlag: true)
    {
    }

    internal FromFlag(IEnumerable<Token> tokens)
        : base(tokens)
    {
    }

    public static FromFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(text,
            KeywordToken.GetParser("from", escapeChar),
            LiteralToken(escapeChar, Enumerable.Empty<char>()),
            tokens => new FromFlag(tokens),
            escapeChar: escapeChar);

    public static Parser<FromFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("from", escapeChar),
            LiteralToken(escapeChar, Enumerable.Empty<char>()),
            tokens => new FromFlag(tokens),
            escapeChar: escapeChar);
}
