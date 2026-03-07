using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Base class for standalone boolean flags (e.g., --link, --keep-git-dir) that have no value.
/// Implements IKeyValuePair so that the token maps to the "keyValue" kind,
/// consistent with the Lean specification where boolean flags are keyValue tokens.
/// Subclasses only need to specify their keyword string and provide static Parse/GetParser wrappers.
/// </summary>
public abstract class BooleanFlag : AggregateToken, IKeyValuePair
{
    protected BooleanFlag(string keyword, char escapeChar)
        : base(GetTokens($"--{keyword}", GetInnerParser(keyword, escapeChar)))
    {
    }

    protected BooleanFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    /// <summary>
    /// Gets the keyword name of the boolean flag (e.g., "link", "keep-git-dir").
    /// </summary>
    public string Key
    {
        get => Tokens.OfType<KeywordToken>().First().Value;
        set => throw new NotSupportedException("The key of a boolean flag is read-only.");
    }

    /// <summary>
    /// Boolean flags have no value; always returns null.
    /// Setting a non-null value is not supported.
    /// </summary>
    string? IKeyValuePair.Value
    {
        get => null;
        set
        {
            if (value is not null)
            {
                throw new NotSupportedException("Boolean flags do not support a value.");
            }
        }
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
        from dash1 in Symbol('-').AsEnumerable()
        from dash2 in Symbol('-').AsEnumerable()
        from kw in KeywordToken.GetParser(keyword, escapeChar).AsEnumerable()
        select ConcatTokens(dash1, dash2, kw);
}
