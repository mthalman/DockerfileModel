using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public partial class Heredoc
{
    /// <summary>Constructs a complete opening-marker and body pair for insertion into a heredoc collection.</summary>
    /// <param name="name">The nonempty, decoded delimiter name, without its quote characters.</param>
    /// <param name="rawContent">The exact body text, excluding the closing delimiter; nonempty content must end with a newline.</param>
    /// <param name="quote">The quoting form of the opening delimiter.</param>
    /// <param name="chomp">Whether to strip leading tabs when interpreting the body and matching the closing delimiter.</param>
    /// <param name="escapeChar">Backslash or backtick context metadata; it need not match the receiving instruction.</param>
    /// <remarks>
    /// Raw content is not normalized or newline-terminated automatically. No newline is appended
    /// after the closing delimiter; the owning collection supplies required insertion boundaries.
    /// Heredoc word quoting uses fixed-backslash rules independently of <paramref name="escapeChar"/>.
    /// Complete built-in pairs can be adopted across escape contexts; this parameter is retained
    /// for compatibility and context bookkeeping, not as an adoption precondition.
    /// <see cref="HeredocQuoteKind.Unquoted"/> reports <see cref="Expand"/> as true even when delimiter
    /// characters require backslash escapes. Quoted modes report false. This is Dockerfile parser
    /// metadata, not a guarantee of expansion by the selected shell.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="rawContent"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The escape character or quote mode is unsupported.</exception>
    /// <exception cref="ArgumentException">
    /// The delimiter is empty, spans physical lines, or cannot use the requested quoting form,
    /// or the nonempty body does not end with a newline.
    /// </exception>
    /// <exception cref="InvalidOperationException">A body line collides with the closing delimiter under the selected chomp rules.</exception>
    public Heredoc(string name, string rawContent, HeredocQuoteKind quote = HeredocQuoteKind.Unquoted,
        bool chomp = false, char escapeChar = Dockerfile.DefaultEscapeChar)
    {
        if (escapeChar is not '\\' and not '`') throw new ArgumentOutOfRangeException(nameof(escapeChar));
        ValidateContent(name, rawContent, chomp);
        Marker = CreateMarker(name, quote, chomp);
        Body = new HeredocBodyToken((rawContent.Length == 0 ? Enumerable.Empty<Token>() :
            new Token[] { new StringToken(rawContent) }).Append(new HeredocDelimiterToken(name)));
        Marker.EditingEscapeChar = Body.EditingEscapeChar = escapeChar;
    }

    /// <summary>Gets the body text before chomp processing, preserving its tabs and line endings.</summary>
    /// <remarks>The opening marker, closing delimiter, and closing-line terminator are not included.</remarks>
    public string RawContent => Body.Content;

    /// <summary>Checks that the current marker and body still form one faithfully mapped, complete pair.</summary>
    /// <remarks>Collection adapters call this before staging edits because the exposed underlying tokens can be modified separately.</remarks>
    /// <exception cref="InvalidOperationException">Pairing, token ownership, delimiter boundaries, or body content are inconsistent.</exception>
    /// <exception cref="ArgumentException">The delimiter or raw content no longer satisfies construction requirements.</exception>
    internal void ValidatePair()
    {
        EditValidation.ValidateTree(Marker);
        EditValidation.ValidateTree(Body);
        if (Marker.Tokens.OfType<HeredocDelimiterToken>().Count() != 1 ||
            Body.Tokens.OfType<HeredocDelimiterToken>().Count() != 1 ||
            Body.ClosingDelimiter != Name)
            throw new InvalidOperationException("Heredoc marker and closing delimiter do not form one complete pair.");
        var symbols = Marker.Tokens.OfType<SymbolToken>().Take(2).ToArray();
        if (symbols.Length != 2 || symbols.Any(symbol => symbol.Value != "<"))
            throw new InvalidOperationException("The heredoc operator cannot be edited safely.");
        var markerTokens = Marker.Tokens.ToList();
        int nameIndex = markerTokens.FindIndex(token => token is HeredocDelimiterToken);
        string spelling = ((HeredocDelimiterToken)markerTokens[nameIndex]).Value;
        if (nameIndex > 0 && markerTokens[nameIndex - 1] is SymbolToken wrapper && wrapper.Value is "'" or "\"")
            spelling = wrapper.Value + spelling + wrapper.Value;
        HeredocWord word = HeredocWord.Read(spelling, 0);
        if (!word.IsTerminated || word.End != spelling.Length || word.Value != Name || word.ContainsLessThan)
            throw new InvalidOperationException("The heredoc word cannot be mapped faithfully.");
        ValidateContent(Name, RawContent, Chomp);
        List<Token> body = Body.Tokens.ToList();
        int delimiter = body.FindIndex(token => token is HeredocDelimiterToken);
        if (body.Skip(delimiter + 1).Any(token => token is not NewLineToken || token.ToString() is not "\n" and not "\r\n") ||
            body.Count - delimiter > 2)
            throw new InvalidOperationException("The closing delimiter has an invalid boundary.");
        if (!Chomp && ClosingPrefix(body, delimiter) is not null)
            throw new InvalidOperationException("A non-chomping delimiter cannot have leading tabs.");
    }

    private static StringToken? ClosingPrefix(List<Token> tokens, int delimiterIndex) =>
        delimiterIndex > 0 && tokens[delimiterIndex - 1] is StringToken prefix &&
        prefix.Value.Length > 0 && prefix.Value.All(ch => ch == '\t') ? prefix : null;

    private static void ValidateContent(string name, string rawContent, bool chomp)
    {
        Guard.NotNullOrEmpty(name, nameof(name));
        Guard.NotNull(rawContent, nameof(rawContent));
        if (name.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            throw new ArgumentException("A heredoc delimiter must fit on one physical line.", nameof(name));
        if (rawContent.Length > 0 && !rawContent.EndsWith("\n", StringComparison.Ordinal))
            throw new ArgumentException("Raw heredoc content must end with a newline.", nameof(rawContent));
        foreach (string rawLine in rawContent.Split('\n').Take(rawContent.Split('\n').Length - 1))
        {
            string line = rawLine.EndsWith("\r", StringComparison.Ordinal) ? rawLine.Substring(0, rawLine.Length - 1) : rawLine;
            if ((chomp ? line.TrimStart('\t') : line) == name)
                throw new InvalidOperationException("The heredoc body contains its closing delimiter.");
        }
    }

    private static HeredocMarkerToken CreateMarker(string name, HeredocQuoteKind quote, bool chomp)
    {
        string spelling = quote switch
        {
            HeredocQuoteKind.Unquoted => string.Concat(name.Select(ch =>
                char.IsWhiteSpace(ch) || ch is '\\' or '\'' or '"' ? "\\" + ch : ch.ToString())),
            HeredocQuoteKind.SingleQuoted when !name.Contains('\'') => "'" + name + "'",
            HeredocQuoteKind.DoubleQuoted => "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$") + "\"",
            HeredocQuoteKind.SingleQuoted => throw new ArgumentException("A single-quoted delimiter cannot contain a single quote."),
            _ => throw new ArgumentOutOfRangeException(nameof(quote))
        };
        HeredocWord word = HeredocWord.Read(spelling, 0);
        if (!word.IsTerminated || word.End != spelling.Length || word.Value != name || word.ContainsLessThan)
            throw new ArgumentException("The delimiter cannot be represented in the requested quote mode.");
        List<Token> tokens = new() { new SymbolToken('<'), new SymbolToken('<') };
        if (chomp) tokens.Add(new SymbolToken('-'));
        // Diagnostic markers decode the fixed-backslash heredoc word, independently of Dockerfile escapes.
        tokens.Add(new HeredocDelimiterToken(spelling));
        return new(tokens, diagnostic: true);
    }
}
