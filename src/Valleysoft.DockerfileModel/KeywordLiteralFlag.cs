using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Base class for simple --key=value flags where the value is a literal that can contain variables.
/// Subclasses only need to specify their keyword string and provide static Parse/GetParser wrappers.
/// </summary>
public abstract class KeywordLiteralFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    protected KeywordLiteralFlag(string keyword, string value, char escapeChar)
        : base(new KeywordToken(keyword, escapeChar), new LiteralToken(value, canContainVariables: true, escapeChar), isFlag: true,
            escapeChar: escapeChar)
    {
    }

    protected KeywordLiteralFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    protected static TFlag ParseFlag<TFlag>(string text, string keyword,
        Func<IEnumerable<Token>, char, TFlag> factory, char escapeChar = Dockerfile.DefaultEscapeChar)
        where TFlag : KeywordLiteralFlag =>
        Parse(text, KeywordToken.GetParser(keyword, escapeChar), LiteralWithVariables(escapeChar),
            tokens => factory(tokens, escapeChar), escapeChar: escapeChar);

    protected static Parser<TFlag> GetFlagParser<TFlag>(string keyword,
        Func<IEnumerable<Token>, char, TFlag> factory, char escapeChar = Dockerfile.DefaultEscapeChar)
        where TFlag : KeywordLiteralFlag =>
        GetParser(KeywordToken.GetParser(keyword, escapeChar), LiteralWithVariables(escapeChar),
            tokens => factory(tokens, escapeChar), escapeChar: escapeChar);
}
