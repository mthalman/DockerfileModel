using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Represents a mount specification for RUN --mount flags.
/// Handles all BuildKit mount types (bind, cache, tmpfs, secret, ssh) by parsing
/// the mount value as comma-separated entries,
/// where each entry is either a key=value pair or a bare keyword (e.g., required, readonly).
/// Key/value entries may have an empty value.
/// The type entry may appear anywhere; an omitted type defaults to bind without adding text.
/// </summary>
public class Mount : AggregateToken
{
    private readonly char escapeChar;

    internal Mount(IEnumerable<Token> tokens, char escapeChar = Dockerfile.DefaultEscapeChar) : base(tokens)
    {
        this.escapeChar = escapeChar;
    }

    /// <summary>
    /// Gets the explicit type or "bind" if omitted. Setting this property inserts an explicit
    /// type entry when absent; reading it does not change the mount text.
    /// </summary>
    public string Type
    {
        get => TypeToken?.Value ?? "bind";
        set
        {
            Guard.NotNullOrEmpty(value, nameof(value));
            var typeToken = TypeToken;
            if (typeToken is null)
            {
                TypeToken = new KeyValueToken<KeywordToken, LiteralToken>(
                    new KeywordToken("type", escapeChar),
                    new LiteralToken(value, canContainVariables: true, escapeChar),
                    isFlag: false, escapeChar: escapeChar);
            }
            else
            {
                var valueToken = typeToken.ValueToken
                    ?? throw new InvalidOperationException("Mount.TypeToken.ValueToken cannot be null when setting Mount.Type.");
                valueToken.Value = value;
            }
        }
    }

    /// <summary>
    /// Gets the explicit type entry, or null if omitted. Setting a non-null token replaces
    /// the existing entry or inserts it before the first entry. The setter rejects null.
    /// </summary>
#if NET5_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.DisallowNull]
#endif
    public KeyValueToken<KeywordToken, LiteralToken>? TypeToken
    {
        get => Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>()
            .FirstOrDefault(token => string.Equals(token.Key, "type", StringComparison.OrdinalIgnoreCase));
        set
        {
            Guard.NotNull(value!, nameof(value));
            SetToken(TypeToken, value, addToken: token =>
            {
                int entryIndex = TokenList.FindIndex(entry =>
                    entry is KeyValueToken<KeywordToken, LiteralToken> or KeywordToken);
                TokenList.InsertRange(entryIndex, new Token[] { token, new SymbolToken(',') });
            });
        }
    }

    /// <summary>
    /// Parses a complete mount specification, preserving whitespace around entries.
    /// Unlike a RUN flag parser, this method rejects unconsumed trailing text.
    /// </summary>
    public static Mount Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar, isFlagValue: false).End()), escapeChar);

    public static Parser<Mount> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        GetParser(escapeChar, isFlagValue: false);

    internal static Parser<Mount> GetParser(char escapeChar, bool isFlagValue) =>
        from tokens in GetInnerParser(escapeChar, isFlagValue)
        select new Mount(tokens, escapeChar);

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar, bool isFlagValue)
    {
        Parser<bool> wordBoundary = Sprache.Parse.WhiteSpace.Select(_ => true)
            .Or(Sprache.Parse.Return(true).End());
        Parser<IEnumerable<Token>> continuationTrivia =
            from continuations in LineContinuations(escapeChar)
            from comments in continuations.Any()
                ? CommentText().Many().Flatten()
                : Sprache.Parse.Return(Enumerable.Empty<Token>())
            select ConcatTokens(continuations, comments);
        Parser<LiteralToken> emptyValueParser =
            from boundary in (
                from trivia in continuationTrivia
                from end in Sprache.Parse.Char(',').Select(_ => true).Or(wordBoundary)
                select end).Preview()
            where boundary.IsDefined
            select new LiteralToken("", canContainVariables: true, escapeChar);

        Parser<LiteralToken> valueParser = LiteralWithVariables(
            escapeChar, new char[] { ',' });

        // Check empty values before the whitespace-tolerant parser, so "source= echo"
        // leaves the command untouched while nonempty values retain their continuation tokens.
        Parser<KeyValueToken<KeywordToken, LiteralToken>> keyValueParser =
            KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                KeywordToken.GetParser(escapeChar), emptyValueParser, escapeChar: escapeChar,
                excludeLeadingWhitespaceInValue: true, excludeTrailingWhitespaceInSeparator: true)
            .Or(KeyValueToken<KeywordToken, LiteralToken>.GetParser(
                KeywordToken.GetParser(escapeChar), valueParser, escapeChar: escapeChar));

        Parser<Token> bareKeyParser =
            from keyword in KeywordToken.GetParser(escapeChar)
            from noSeparator in (
                from continuations in LineContinuations(escapeChar)
                from separator in Symbol('=')
                select separator).Not()
            select (Token)keyword;

        // Backtrack for bare keys, but not for a key whose '=' value failed to parse.
        Parser<Token> entryParser =
            keyValueParser.Cast<KeyValueToken<KeywordToken, LiteralToken>, Token>()
            .Or(bareKeyParser);

        // Line continuations can appear between comma-separated pairs.
        // Whitespace() after each LineContinuations() handles indentation that may
        // appear on the next line after a line continuation (e.g., "type=bind,\\\n  readonly").
        // CommentText() handles comment lines that can appear after line continuations
        // (e.g., "type=bind,\\\n# comment\nreadonly").
        //
        // Entries leave trailing whitespace for the surrounding context. RUN flags leave it
        // at instruction level; standalone mounts retain it after all entries are parsed.
        return
            from first in ArgTokens(
                entryParser.AsEnumerable(), escapeChar,
                excludeTrailingWhitespace: true)
            from rest in (
                from lineCont1 in LineContinuations(escapeChar)
                from comments1 in CommentText().Many()
                from ws1 in Whitespace()
                where !isFlagValue || !ws1.Any() || lineCont1.Any()
                from comma in Symbol(',')
                from wsAfterComma in Whitespace()
                from lineCont2 in LineContinuations(escapeChar)
                where !isFlagValue || !wsAfterComma.Any() || lineCont2.Any()
                from comments2 in CommentText().Many()
                from ws2 in Whitespace()
                from entry in entryParser
                select ConcatTokens(lineCont1, comments1.SelectMany(c => c), ws1, new Token[] { comma }, wsAfterComma, lineCont2, comments2.SelectMany(c => c), ws2, new Token[] { entry })).Many()
            from boundary in (
                from trivia in continuationTrivia
                from end in wordBoundary
                select end).Preview()
            where boundary.IsDefined
            from trailingWhitespace in isFlagValue
                ? Sprache.Parse.Return(Enumerable.Empty<Token>())
                : ArgTrailingWhitespace(escapeChar)
            select ConcatTokens(first, rest.SelectMany(t => t), trailingWhitespace);
    }
}
