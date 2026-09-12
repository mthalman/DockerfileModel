using System.Text;

namespace Valleysoft.DockerfileModel;

internal readonly struct HeredocWord
{
    private HeredocWord(string value, int end, bool isTerminated, char? quoteChar, bool containsLessThan)
    {
        Value = value;
        End = end;
        IsTerminated = isTerminated;
        QuoteChar = quoteChar;
        ContainsLessThan = containsLessThan;
    }

    public string Value { get; }
    public int End { get; }
    public bool IsTerminated { get; }
    public char? QuoteChar { get; }
    public bool ContainsLessThan { get; }

    public static HeredocWord Read(string text, int start)
    {
        StringBuilder value = new();
        char? quote = null;
        char? firstQuote = null;
        bool containsLessThan = false;
        int position = start;
        while (position < text.Length)
        {
            char ch = text[position];
            if (quote is null && char.IsWhiteSpace(ch))
            {
                break;
            }

            containsLessThan |= ch == '<';
            if (ch == '\\' && quote is null && position + 1 == text.Length)
            {
                position++;
                continue;
            }

            // BuildKit's heredoc shell lexer always uses backslash. Inside double
            // quotes it escapes only quotes, dollars and backslashes, not arbitrary characters.
            if (ch == '\\' && quote != '\'' && position + 1 < text.Length &&
                (quote is null || text[position + 1] is '"' or '$' or '\\'))
            {
                containsLessThan |= text[position + 1] == '<';
                value.Append(text[position + 1]);
                position += 2;
            }
            else if ((ch == '\'' || ch == '"') && (quote is null || quote == ch))
            {
                firstQuote ??= ch;
                quote = quote is null ? ch : null;
                position++;
            }
            else
            {
                value.Append(ch);
                position++;
            }
        }

        return new HeredocWord(value.ToString(), position, quote is null, firstQuote, containsLessThan);
    }
}
