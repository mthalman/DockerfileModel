using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Base class for standalone boolean flags (e.g., --link, --keep-git-dir) that have no value.
/// Extends KeyValueToken so that the token maps to the "keyValue" kind,
/// consistent with the Lean specification where boolean flags are keyValue tokens.
/// The separator and value are both absent; IKeyValuePair.Value returns null.
/// Subclasses only need to specify their keyword string and provide static Parse/GetParser wrappers.
/// </summary>
public abstract class BooleanFlag : KeyValueToken<KeywordToken, LiteralToken>, IKeyValuePair
{
    protected BooleanFlag(string keyword, char escapeChar)
        : base(GetTokens($"--{keyword}", GetInnerParser(keyword, escapeChar)))
    {
    }

    protected BooleanFlag(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    /// <summary>
    /// Boolean flags have no value; always returns null.
    /// Setting a value is not supported.
    /// </summary>
    /// <remarks>
    /// Overrides the base class <see cref="KeyValueToken{TKey, TValue}.Value"/> property
    /// to prevent callers from accidentally reading <see cref="string.Empty"/> or
    /// inserting a <see cref="LiteralToken"/> via the setter, which would create a
    /// structurally invalid boolean flag.
    /// </remarks>
    public override string Value
    {
        get => null!;
        set => throw new NotSupportedException("Boolean flags do not support a value.");
    }

    string? IKeyValuePair.Value
    {
        get => null;
        set => throw new NotSupportedException("Boolean flags do not support a value.");
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
