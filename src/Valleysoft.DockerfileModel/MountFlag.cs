using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public class MountFlag : KeyValueToken<KeywordToken, Mount>
{
    public MountFlag(Mount mount, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(new KeywordToken("mount", escapeChar), mount, isFlag: true, escapeChar: escapeChar)
    {
    }

    internal MountFlag(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar)
        : base(tokens, escapeChar)
    {
    }

    public static MountFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(escapeChar).Parse(text);

    public static Parser<MountFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(
            KeywordToken.GetParser("mount", escapeChar),
            MountParser(escapeChar),
            tokens => new MountFlag(tokens, escapeChar),
            escapeChar: escapeChar,
            isFlag: true)
        // Whitespace after '=' ends the flag word; it must not let an empty mount
        // consume the first word of the shell command as a bare mount entry.
        .Where(flag => !flag.Tokens
            .After(flag.Tokens.OfType<SymbolToken>().Last())
            .OfType<WhitespaceToken>().Any());

    private static Parser<Mount> MountParser(char escapeChar) =>
        Mount.GetParser(escapeChar, isFlagValue: true);
}
