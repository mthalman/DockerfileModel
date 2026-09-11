namespace Valleysoft.DockerfileModel.Tokens;

/// <summary>
/// Aggregate token representing a heredoc marker inline in the command stream: <<[-][QUOTE]DELIM[QUOTE].
/// Child tokens are: SymbolToken('<'), SymbolToken('<'), optional SymbolToken('-'),
/// optional SymbolToken(quoteChar), HeredocDelimiterToken(name), optional SymbolToken(quoteChar).
/// </summary>
public class HeredocMarkerToken : AggregateToken
{
    internal HeredocMarkerToken(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    /// <summary>
    /// Gets the delimiter name (e.g. "EOF") from the marker.
    /// </summary>
    public string DelimiterName
    {
        get
        {
            HeredocDelimiterToken? delim = Tokens.OfType<HeredocDelimiterToken>().FirstOrDefault();
            return delim?.Value ?? string.Empty;
        }
    }

    /// <summary>
    /// True if the heredoc uses &lt;&lt;- (tab-stripping chomp mode).
    /// </summary>
    public bool Chomp
    {
        get
        {
            // The chomp symbol '-' appears after the two '<' symbols and before any quote or delimiter.
            var tokenList = Tokens.ToList();
            // Tokens[0] = '<', Tokens[1] = '<', Tokens[2] = '-' or quote or delimiter
            return tokenList.Count > 2 &&
                   tokenList[2] is SymbolToken sym &&
                   sym.Value == "-";
        }
    }

    /// <summary>
    /// True if the delimiter was quoted (single or double quotes), meaning no variable expansion.
    /// </summary>
    public bool IsQuoted => QuoteChar.HasValue;

    /// <summary>
    /// Gets the quote character used for the delimiter, or null if unquoted.
    /// </summary>
    public char? QuoteChar
    {
        get
        {
            var tokenList = Tokens.ToList();
            // Find the HeredocDelimiterToken index; the quote symbol is immediately before it
            for (int i = 0; i < tokenList.Count; i++)
            {
                if (tokenList[i] is HeredocDelimiterToken)
                {
                    if (i > 0 && tokenList[i - 1] is SymbolToken sym)
                    {
                        char c = sym.Value[0];
                        if (c == '\'' || c == '"')
                        {
                            return c;
                        }
                    }
                    break;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// True if the heredoc body should expand variables (i.e. delimiter is NOT quoted).
    /// </summary>
    public bool Expand => !IsQuoted;
}
