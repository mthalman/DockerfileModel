using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Base class for boolean flags (e.g., --link, --keep-git-dir) that optionally accept
/// an explicit =true or =false value.
/// Extends KeyValueToken so that the token maps to the "keyValue" kind,
/// consistent with the Lean specification where boolean flags are keyValue tokens.
/// When bare (no =value), the separator and value are absent; IKeyValuePair.Value returns null.
/// When explicit (=true/=false), the separator ('=') and value (LiteralToken) are present.
/// Subclasses only need to specify their keyword string and provide static Parse/GetParser wrappers.
/// </summary>
public abstract class BooleanFlag : KeyValueToken<KeywordToken, LiteralToken>
{
    protected BooleanFlag(string keyword, char escapeChar)
        : base(GetTokens($"--{keyword}", GetInnerParser(keyword, escapeChar)))
    {
    }

    protected BooleanFlag(string keyword, bool value, char escapeChar)
        : base(GetTokens(
            $"--{keyword}={BoolToString(value)}",
            GetInnerParser(keyword, escapeChar)))
    {
    }

    protected BooleanFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    /// <summary>
    /// Gets the logical boolean value of this flag.
    /// Returns true for bare flags (--link) and explicit =true variants.
    /// Returns false for explicit =false variants.
    /// </summary>
    public bool BoolValue
    {
        get
        {
            LiteralToken? valueToken = ValueToken;
            if (valueToken is null)
            {
                // Bare flag (no =value) is implicitly true
                return true;
            }

            return string.Equals(valueToken.Value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Gets or sets the explicit value of this boolean flag.
    /// Returns null for bare flags (no =value), or the string "true"/"false" when explicit.
    /// Setting a value is not supported; use the constructor to specify an explicit value.
    /// </summary>
    public override string Value
    {
        get
        {
            LiteralToken? valueToken = ValueToken;
            return valueToken is null ? null! : valueToken.Value;
        }
        set => throw new NotSupportedException("Boolean flags do not support setting a value directly.");
    }

    protected static TFlag ParseFlag<TFlag>(string text, string keyword,
        Func<IEnumerable<Token>, TFlag> factory, char escapeChar = Dockerfile.DefaultEscapeChar)
        where TFlag : BooleanFlag =>
        factory(GetTokens(text, GetInnerParser(keyword, escapeChar)));

    protected static Parser<TFlag> GetFlagParser<TFlag>(string keyword,
        Func<IEnumerable<Token>, TFlag> factory, char escapeChar = Dockerfile.DefaultEscapeChar)
        where TFlag : BooleanFlag =>
        from tokens in GetInnerParser(keyword, escapeChar)
        select factory(tokens);

    private static Parser<IEnumerable<Token>> GetInnerParser(string keyword, char escapeChar) =>
        // Path 1: --name=true or --name=false (case-insensitive)
        // Sprache's .Or() backtracks even when input has been consumed, so if '='
        // is present but the value is invalid (e.g. =yes, =1, empty =), .Or()
        // backtracks to try Path 2.
        (from dash1 in Symbol('-').AsEnumerable()
         from dash2 in Symbol('-').AsEnumerable()
         from kw in KeywordToken.GetParser(keyword, escapeChar).AsEnumerable()
         from eq in Symbol('=').AsEnumerable()
         from val in BooleanValueLiteral(escapeChar).AsEnumerable()
         select ConcatTokens(dash1, dash2, kw, eq, val)
        ).Or(
        // Path 2: bare --name (must not be followed by '=' to prevent matching
        // --name from --name=<invalid> after Path 1 backtrack)
            from dash1 in Symbol('-').AsEnumerable()
            from dash2 in Symbol('-').AsEnumerable()
            from kw in KeywordToken.GetParser(keyword, escapeChar).AsEnumerable()
            from notEq in Sprache.Parse.Not(Sprache.Parse.Char('='))
            select ConcatTokens(dash1, dash2, kw)
        );

    /// <summary>
    /// Parses a case-insensitive "true" or "false" and returns it as a LiteralToken.
    /// </summary>
    private static Parser<LiteralToken> BooleanValueLiteral(char escapeChar) =>
        (from tokens in StringToken("true", escapeChar)
         select new LiteralToken(
             TokenHelper.CollapseStringTokens(tokens),
             canContainVariables: false,
             escapeChar)
        ).Or(
        from tokens in StringToken("false", escapeChar)
        select new LiteralToken(
            TokenHelper.CollapseStringTokens(tokens),
            canContainVariables: false,
            escapeChar)
        );

    private static string BoolToString(bool value) => value ? "true" : "false";
}
