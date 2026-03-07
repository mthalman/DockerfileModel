using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Represents a generic mount specification for RUN --mount flags.
/// Handles all BuildKit mount types (bind, cache, tmpfs, secret, ssh) by parsing
/// the mount value as a type=X prefix followed by zero or more comma-separated key=value pairs.
/// </summary>
public class GenericMount : Mount
{
    internal GenericMount(IEnumerable<Token> tokens)
        : base(tokens)
    {
    }

    public static GenericMount Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<GenericMount> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new GenericMount(tokens);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar)
    {
        Parser<LiteralToken> valueParser = LiteralWithVariables(
            escapeChar, new char[] { ',' });

        Parser<KeyValueToken<KeywordToken, LiteralToken>> keyValueParser =
            KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                KeywordToken.GetParser(escapeChar), valueParser, escapeChar: escapeChar);

        // Parse: type=X followed by zero or more ,key=value pairs
        return
            from type in ArgTokens(
                KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                    KeywordToken.GetParser("type", escapeChar), valueParser, escapeChar: escapeChar).AsEnumerable(), escapeChar)
            from rest in (
                from comma in Symbol(',')
                from kv in keyValueParser
                select ConcatTokens(comma, kv)).Many()
            select ConcatTokens(type, rest.SelectMany(t => t));
    }
}
