using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Represents a heredoc block in a Dockerfile instruction.
/// A heredoc is opened by &lt;&lt;DELIM (or &lt;&lt;-DELIM, &lt;&lt;"DELIM", &lt;&lt;'DELIM') and closed by DELIM on its own line.
/// Child tokens preserve exact character fidelity for round-trip. The tokenization structure is:
/// <list type="bullet">
///   <item><description>
///     The opening marker (e.g. <c>&lt;&lt;EOF</c>) is a <see cref="Tokens.StringToken"/>, optionally
///     followed by a second <see cref="Tokens.StringToken"/> for any text that appears after the marker
///     on the same line, and then a <see cref="Tokens.NewLineToken"/>.
///   </description></item>
///   <item><description>
///     Each body line is stored as a single <see cref="Tokens.StringToken"/> whose value
///     already includes the trailing newline character(s); no separate <see cref="Tokens.NewLineToken"/>
///     is emitted for body lines.
///   </description></item>
///   <item><description>
///     The closing delimiter line is a <see cref="Tokens.StringToken"/>, optionally followed
///     by a <see cref="Tokens.NewLineToken"/> when a newline is present after the delimiter.
///   </description></item>
/// </list>
/// </summary>
public class HeredocToken : AggregateToken
{
    internal HeredocToken(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    /// <summary>
    /// Gets the body content of the heredoc — the text between the opening delimiter line
    /// and the closing delimiter line. This is the concatenation of all body-line
    /// <see cref="Tokens.StringToken"/>s (each of which includes its trailing newline).
    /// Returns an empty string when the heredoc has no body.
    /// </summary>
    public string Body
    {
        get
        {
            // Find the first NewLineToken index — everything before it (inclusive) is the marker line.
            int markerNewLineIndex = -1;
            for (int i = 0; i < TokenList.Count; i++)
            {
                if (TokenList[i] is NewLineToken)
                {
                    markerNewLineIndex = i;
                    break;
                }
            }

            // No newline means no body (marker-only heredoc).
            if (markerNewLineIndex < 0)
            {
                return string.Empty;
            }

            // Find the last StringToken — that is the closing delimiter.
            int closingDelimiterIndex = -1;
            for (int i = TokenList.Count - 1; i >= 0; i--)
            {
                if (TokenList[i] is StringToken)
                {
                    closingDelimiterIndex = i;
                    break;
                }
            }

            // Body tokens are the StringTokens between the marker newline and the closing delimiter.
            if (closingDelimiterIndex < 0 || closingDelimiterIndex <= markerNewLineIndex)
            {
                return string.Empty;
            }

            return string.Concat(
                TokenList
                    .Skip(markerNewLineIndex + 1)
                    .Take(closingDelimiterIndex - markerNewLineIndex - 1)
                    .Select(t => t.ToString()));
        }
    }
}
