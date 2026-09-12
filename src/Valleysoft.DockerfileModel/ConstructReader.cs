using System.Text;

namespace Valleysoft.DockerfileModel;

// Recovery framing is separate from legacy framing: notably it finishes continued
// headers before reading bodies and reports missing terminators instead of accepting them.
internal static class ConstructReader
{
    internal readonly struct Region
    {
        public Region(int end, int? unterminatedMarker = null, int headerEnd = 0, IReadOnlyList<HeredocRegion>? heredocs = null)
        {
            End = end;
            UnterminatedMarker = unterminatedMarker;
            HeaderEnd = headerEnd;
            Heredocs = heredocs ?? Array.Empty<HeredocRegion>();
        }

        public int End { get; }
        public int? UnterminatedMarker { get; }
        public int HeaderEnd { get; }
        public IReadOnlyList<HeredocRegion> Heredocs { get; }
    }

    internal sealed class HeredocRegion
    {
        public int MarkerStart { get; set; }
        public int SecondOperatorStart { get; set; }
        public int? ChompStart { get; set; }
        public int DelimiterStart { get; set; }
        public int MarkerEnd { get; set; }
        public string Name { get; set; } = "";
        public bool Chomp => ChompStart.HasValue;
        public int BodyStart { get; set; }
        public int ClosingLineStart { get; set; }
        public int ClosingNameStart { get; set; }
        public int ClosingNameEnd { get; set; }
        public int End { get; set; }
    }

    public static Region Read(string text, int start, char escapeChar)
    {
        StringBuilder header = new();
        List<int> headerOffsets = new();
        int position = start;
        bool continued = false;

        while (position < text.Length)
        {
            int end = LineEnd(text, position);
            int contentEnd = ContentEnd(text, position, end);
            int first = position;
            while (first < contentEnd && char.IsWhiteSpace(text[first]))
            {
                first++;
            }

            bool commentOrEmpty = first == contentEnd || text[first] == '#';
            if (commentOrEmpty)
            {
                if (!continued)
                {
                    return new Region(end);
                }
                position = end;
                continue;
            }

            int last = contentEnd - 1;
            while (last >= position && (text[last] == ' ' || text[last] == '\t'))
            {
                last--;
            }

            // BuildKit's continuation rule excludes an escape preceded by another escape.
            continued = end > contentEnd && last >= position && text[last] == escapeChar &&
                (last == position || text[last - 1] != escapeChar);
            int headerEnd = continued ? last : contentEnd;
            for (int i = position; i < headerEnd; i++)
            {
                header.Append(text[i]);
                headerOffsets.Add(i);
            }

            position = end;
            if (!continued)
            {
                break;
            }
        }

        string logicalHeader = header.ToString();
        if (!CanContainHeredoc(logicalHeader))
        {
            return new Region(position);
        }

        int headerEndOffset = position;
        List<HeredocRegion> heredocs = FindHeredocs(logicalHeader, headerOffsets);
        foreach (HeredocRegion heredoc in heredocs)
        {
            heredoc.BodyStart = position;
            bool closed = false;
            while (position < text.Length)
            {
                int end = LineEnd(text, position);
                int contentEnd = ContentEnd(text, position, end);
                int delimiterStart = position;
                if (heredoc.Chomp)
                {
                    while (delimiterStart < contentEnd && text[delimiterStart] == '\t')
                    {
                        delimiterStart++;
                    }
                }

                closed = contentEnd - delimiterStart == heredoc.Name.Length &&
                    string.CompareOrdinal(text, delimiterStart, heredoc.Name, 0, heredoc.Name.Length) == 0;
                heredoc.ClosingLineStart = position;
                heredoc.ClosingNameStart = delimiterStart;
                heredoc.ClosingNameEnd = contentEnd;
                heredoc.End = end;
                position = end;
                if (closed)
                {
                    break;
                }
            }

            if (!closed)
            {
                return new Region(text.Length, heredoc.MarkerStart);
            }
        }

        return new Region(position, headerEnd: headerEndOffset, heredocs: heredocs);
    }

    private static List<HeredocRegion> FindHeredocs(string header, List<int> offsets)
    {
        List<HeredocRegion> result = new();
        int position = 0;
        while (position < header.Length)
        {
            while (position < header.Length && char.IsWhiteSpace(header[position]))
            {
                position++;
            }
            if (position == header.Length || header[position] == '#')
            {
                break;
            }

            int wordStart = position;
            int marker = wordStart;
            while (marker < header.Length && char.IsDigit(header[marker]))
            {
                marker++;
            }
            bool candidate = marker + 1 < header.Length && header[marker] == '<' && header[marker + 1] == '<';
            bool chomp = candidate && marker + 2 < header.Length && header[marker + 2] == '-';
            if (candidate)
            {
                position = marker + (chomp ? 3 : 2);
                while (position < header.Length && char.IsWhiteSpace(header[position]))
                {
                    position++;
                }
            }

            int delimiterStart = position;
            HeredocWord word = HeredocWord.Read(header, position);
            position = word.End;

            // BuildKit identifies complete heredoc words, not substrings such as
            // foo<<EOF or here-strings (<<<word). Keep those from swallowing later instructions.
            if (candidate && word.IsTerminated && !word.ContainsLessThan && position > delimiterStart &&
                word.Value.Length > 0)
            {
                result.Add(new HeredocRegion
                {
                    MarkerStart = offsets[marker],
                    SecondOperatorStart = offsets[marker + 1],
                    ChompStart = chomp ? offsets[marker + 2] : null,
                    DelimiterStart = offsets[delimiterStart],
                    MarkerEnd = offsets[position - 1] + 1,
                    Name = word.Value
                });
            }
        }
        return result;
    }

    private static bool CanContainHeredoc(string header)
    {
        int position = 0;
        string name = ReadWord(header, ref position);
        if (name.Equals("ONBUILD", StringComparison.OrdinalIgnoreCase))
        {
            name = ReadWord(header, ref position);
        }
        return name.Equals("RUN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("COPY", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ADD", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadWord(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
        int start = position;
        while (position < text.Length && !char.IsWhiteSpace(text[position]))
        {
            position++;
        }
        return text.Substring(start, position - start);
    }

    private static int LineEnd(string text, int start)
    {
        int newline = text.IndexOf('\n', start);
        return newline < 0 ? text.Length : newline + 1;
    }

    private static int ContentEnd(string text, int start, int end)
    {
        if (end > start && text[end - 1] == '\n')
        {
            end--;
            if (end > start && text[end - 1] == '\r')
            {
                end--;
            }
        }
        return end;
    }
}
