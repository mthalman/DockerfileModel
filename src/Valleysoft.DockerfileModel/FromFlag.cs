using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class FromFlag : KeyValueToken<KeywordToken, StageName>
{
    public FromFlag(string stageName, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("from", escapeChar), new StageName(stageName, escapeChar), isFlag: true)
    {
    }

    internal FromFlag(IEnumerable<Token> tokens)
        : base(tokens)
    {
    }

    public static FromFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(text,
            KeywordToken.GetParser("from", escapeChar),
            StageName.GetParser(escapeChar),
            tokens => new FromFlag(tokens),
            escapeChar: escapeChar);

    public static Parser<FromFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("from", escapeChar),
            StageName.GetParser(escapeChar),
            tokens => new FromFlag(tokens),
            escapeChar: escapeChar);
}
