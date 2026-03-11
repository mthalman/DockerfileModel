using System.Text;

namespace Valleysoft.DockerfileModel.Tokens;

/// <summary>
/// Aggregate token representing a heredoc body: body lines + closing delimiter + optional trailing newline.
/// This token appears at the instruction level after the command line's NewLineToken, as a sibling
/// of HeredocMarkerToken and other instruction-level tokens.
/// </summary>
public class HeredocBodyToken : AggregateToken
{
    internal HeredocBodyToken(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    /// <summary>
    /// Gets the body content of the heredoc (text between the command line and closing delimiter).
    /// Does NOT include the closing delimiter itself or any leading-tab prefix on the delimiter line.
    /// </summary>
    public string Content
    {
        get
        {
            var tokenList = Tokens.ToList();
            if (tokenList.Count == 0)
            {
                return string.Empty;
            }

            // Find the HeredocDelimiterToken (closing delimiter)
            int delimIndex = -1;
            for (int i = 0; i < tokenList.Count; i++)
            {
                if (tokenList[i] is HeredocDelimiterToken)
                {
                    delimIndex = i;
                    break;
                }
            }

            if (delimIndex < 0)
            {
                // No closing delimiter found (unterminated heredoc) — all content is body
                StringBuilder sb = new();
                foreach (Token t in tokenList)
                {
                    sb.Append(t.ToString());
                }
                return sb.ToString();
            }

            // Determine where body content ends: before the closing delimiter and any tab-prefix
            int bodyEndIndex = delimIndex;

            // If the token immediately before the delimiter is a StringToken containing only tabs,
            // it's the chomp prefix of the closing delimiter line — exclude it from body content.
            if (bodyEndIndex > 0 && tokenList[bodyEndIndex - 1] is StringToken prefixToken)
            {
                string prefixValue = prefixToken.Value;
                if (prefixValue.Length > 0 && prefixValue.TrimStart('\t').Length == 0)
                {
                    bodyEndIndex--;
                }
            }

            // Body is everything before bodyEndIndex
            StringBuilder content = new();
            for (int i = 0; i < bodyEndIndex; i++)
            {
                content.Append(tokenList[i].ToString());
            }

            return content.ToString();
        }
    }

    /// <summary>
    /// Gets the closing delimiter name (e.g. "EOF").
    /// </summary>
    public string ClosingDelimiter
    {
        get
        {
            var tokenList = Tokens.ToList();
            if (tokenList.Count == 0)
            {
                return string.Empty;
            }

            // Find the HeredocDelimiterToken
            HeredocDelimiterToken? delim = tokenList.OfType<HeredocDelimiterToken>().FirstOrDefault();
            return delim?.Value ?? string.Empty;
        }
    }
}
