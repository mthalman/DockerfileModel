using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.VariableParsers;

namespace Valleysoft.DockerfileModel;

public class ChangeOwnerFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    public ChangeOwnerFlag(string changeOwner, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("chown", escapeChar), new LiteralToken(changeOwner, canContainVariables: true, escapeChar), isFlag: true,
            escapeChar: escapeChar)
    {
    }

    internal ChangeOwnerFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static ChangeOwnerFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Parse(text,
            KeywordToken.GetParser("chown", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new ChangeOwnerFlag(tokens, escapeChar),
            escapeChar: escapeChar,
            isFlag: true);

    [Obsolete("Use Dockerfile.Parse or Dockerfile.TryParse instead. Parser factories will be removed in the next major version.")]
    public static Parser<ChangeOwnerFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("chown", escapeChar),
            LiteralWithVariables(escapeChar),
            tokens => new ChangeOwnerFlag(tokens, escapeChar),
            escapeChar: escapeChar,
            isFlag: true);
}
