using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class FromFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    public FromFlag(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("from", escapeChar), new LiteralToken(value, canContainVariables: false, escapeChar), isFlag: true,
            escapeChar: escapeChar)
    {
    }

    internal FromFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static FromFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(text,
            KeywordToken.GetParser("from", escapeChar),
            LiteralToken(escapeChar, Enumerable.Empty<char>()),
            tokens => new FromFlag(tokens),
            escapeChar: escapeChar,
            isFlag: true);

    public static Parser<FromFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("from", escapeChar),
            LiteralToken(escapeChar, Enumerable.Empty<char>()),
            tokens => new FromFlag(tokens),
            escapeChar: escapeChar,
            isFlag: true);
}
