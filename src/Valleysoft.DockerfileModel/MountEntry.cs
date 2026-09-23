using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Represents one token-backed key/value field or bare keyword in a mount specification.</summary>
/// <remarks>
/// Entries obtained from <see cref="Mount.Entries"/> retain their identity while their underlying
/// entry token remains present. To change an entry's key, value, or form through the editing API,
/// construct a replacement and assign it through that collection.
/// </remarks>
public sealed class MountEntry
{
    /// <summary>Constructs a mount field that can be inserted or used as a replacement in <see cref="Mount.Entries"/>.</summary>
    /// <param name="key">The nonempty field name or bare keyword.</param>
    /// <param name="value">
    /// The field value; <see langword="null"/> creates a bare keyword, while an empty string
    /// creates an explicit empty assignment.
    /// </param>
    /// <param name="escapeChar">The escape character used to construct the entry's tokens.</param>
    /// <remarks>
    /// The supplied values must round-trip as exactly one entry under the existing mount grammar.
    /// Construction does not attach the entry to a mount.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The key is empty or the supplied values cannot be represented faithfully as one entry.</exception>
    /// <exception cref="ParseException">The entry's representation is not accepted by the mount parser.</exception>
    public MountEntry(string key, string? value = null, char escapeChar = Dockerfile.DefaultEscapeChar)
    {
        Guard.NotNullOrEmpty(key, nameof(key));
        Token = value is null ? new KeywordToken(key, escapeChar) :
            new KeyValueToken<KeywordToken, LiteralToken>(
                new KeywordToken(key, escapeChar),
                new LiteralToken(value, canContainVariables: true, escapeChar),
                isFlag: false, escapeChar: escapeChar);
        if (value is not null && value.Any(char.IsWhiteSpace) &&
            value.IndexOfAny(new[] { '\r', '\n', '"' }) < 0)
            KeyValueToken!.ValueToken!.QuoteChar = '"';
        Mount parsed = Mount.Parse(Token.ToString(), escapeChar);
        var entries = parsed.Tokens.Where(IsEntry).ToArray();
        if (entries.Length != 1 || new MountEntry(entries[0]).Key != key ||
            new MountEntry(entries[0]).Value != value)
        {
            throw new ArgumentException("The value cannot be represented as one mount entry.");
        }
    }

    /// <summary>Creates a view over an existing entry token without copying its contents.</summary>
    /// <param name="token">A keyword token or a supported key/value entry token.</param>
    internal MountEntry(Token token) => Token = token;

    /// <summary>Gets the entry's root token, used to preserve ownership and wrapper identity during collection edits.</summary>
    internal Token Token { get; }

    /// <summary>Identifies semantic entry roots without treating comma separators or trivia as entries.</summary>
    /// <param name="token">The token to inspect.</param>
    /// <returns>Whether the token can back a mount entry.</returns>
    internal static bool IsEntry(Token token) =>
        token is KeywordToken or KeyValueToken<KeywordToken, LiteralToken>;

    /// <summary>Gets the field name or bare keyword from the underlying token.</summary>
    public string Key => KeyToken.Value;

    /// <summary>Gets the field value, or <see langword="null"/> for a bare keyword.</summary>
    /// <remarks>An explicit empty assignment returns an empty string, not <see langword="null"/>.</remarks>
    public string? Value => KeyValueToken?.Value;

    /// <summary>Gets whether this entry has no assignment separator or value.</summary>
    public bool IsBareKeyword => KeyValueToken is null;

    /// <summary>Gets the underlying key token rather than a detached copy.</summary>
    public KeywordToken KeyToken => KeyValueToken?.KeyToken ?? (KeywordToken)Token;

    /// <summary>Gets the underlying assignment token, or <see langword="null"/> for a bare keyword.</summary>
    public KeyValueToken<KeywordToken, LiteralToken>? KeyValueToken =>
        Token as KeyValueToken<KeywordToken, LiteralToken>;
}
