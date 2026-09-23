using System.Text;
using System.Text.RegularExpressions;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

internal static class DockerfileParser
{
    // Matches heredoc markers: <<[-][QUOTE]DELIMITER[QUOTE]
    // Supports any non-whitespace for unquoted delimiters and any non-quote for quoted delimiters.
    private static readonly Regex HeredocDelimiterRegex = new(
        @"<<(-?)(?:(['""])(.+?)\2|([^\s'""]+))");

    /// <summary>
    /// Represents a detected heredoc delimiter with its properties.
    /// </summary>
    public class HeredocDelimiterInfo
    {
        public HeredocDelimiterInfo(string delimiter, bool hasChomp)
        {
            Delimiter = delimiter;
            HasChomp = hasChomp;
        }

        public string Delimiter { get; }
        public bool HasChomp { get; }
    }

    /// <summary>
    /// Extracts all heredoc delimiter markers from a line of text.
    /// Strips trailing comments before scanning for markers.
    /// </summary>
    public static List<HeredocDelimiterInfo> ExtractHeredocDelimiters(
        string line,
        char escapeChar = Dockerfile.DefaultEscapeChar)
    {
        List<HeredocDelimiterInfo> result = new();

        // Strip trailing comment before searching for heredoc markers
        string strippedLine = StripTrailingComment(line, escapeChar);

        // Use a quote-aware scan so that `<<` inside quoted strings
        // (e.g. `RUN echo "<<EOF"`) is not treated as a heredoc marker.
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        for (int i = 0; i < strippedLine.Length; i++)
        {
            char ch = strippedLine[i];

            // Skip escaped characters (the active escape char is not special inside single quotes)
            if (ch == escapeChar && !inSingleQuote && i + 1 < strippedLine.Length)
            {
                i++; // skip next character
                continue;
            }

            if (ch == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (ch == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            // Only match << when outside quotes
            if (!inSingleQuote && !inDoubleQuote
                && ch == '<' && i + 1 < strippedLine.Length && strippedLine[i + 1] == '<')
            {
                Match match = HeredocDelimiterRegex.Match(strippedLine, i);
                if (match.Success && match.Index == i)
                {
                    bool hasChomp = match.Groups[1].Value == "-";
                    string delimiterName = match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
                    result.Add(new HeredocDelimiterInfo(delimiterName, hasChomp));
                    // Advance past the matched marker text
                    i += match.Length - 1;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Strips a trailing comment from a line, respecting quoted strings and escape characters.
    /// A '#' character inside single or double quotes is NOT treated as a comment.
    /// A '#' character preceded by the escape character (outside single quotes) is NOT treated as a comment.
    /// </summary>
    public static string StripTrailingComment(string line, char escapeChar = Dockerfile.DefaultEscapeChar)
    {
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool previousCharWasUnescapedWhitespace = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];

            // Skip escaped characters (escape char is not special inside single quotes)
            if (ch == escapeChar && !inSingleQuote && i + 1 < line.Length)
            {
                previousCharWasUnescapedWhitespace = false;
                i++; // skip next character
                continue;
            }

            if (ch == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (ch == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (ch == '#' && !inSingleQuote && !inDoubleQuote && (i == 0 || previousCharWasUnescapedWhitespace))
            {
                return line.Substring(0, i);
            }

            previousCharWasUnescapedWhitespace = char.IsWhiteSpace(ch);
        }

        return line;
    }

    public static Dockerfile ParseContent(string text)
    {
        DirectiveHeader header = new();
        List<DockerfileConstruct> dockerfileConstructs = new();

        List<string> constructLines = new();
        StringBuilder constructBuilder = new();
        StringBuilder lineBuilder = new();
        List<HeredocDelimiterInfo> pendingDelimiters = new();

        bool bom = text.StartsWith("\uFEFF", StringComparison.Ordinal);
        int contentStart = bom ? 1 : 0;
        while (contentStart < text.Length)
        {
            int end = DirectiveHeader.LineEnd(text, contentStart);
            string line = text.Substring(contentStart, end - contentStart);
            ParserDirective? directive = header.Read(line, out string? error);
            if (error is not null)
            {
                SourcePosition position = new SourceMap(text).GetSpan(contentStart, contentStart).Start;
                throw new ParseException(error, new Position(contentStart, position.Line, position.Column));
            }
            if (directive is null)
            {
                break;
            }
            dockerfileConstructs.Add(directive);
            constructLines.Add(line);
            contentStart = end;
        }
        char escapeChar = header.EscapeChar;

        for (int i = contentStart; i < text.Length; i++)
        {
            char ch = text[i];

            lineBuilder.Append(ch);

            if (ch == '\n')
            {
                string line = lineBuilder.ToString();
                // If we have pending heredoc delimiters, check if this line closes one
                if (pendingDelimiters.Count > 0)
                {
                    constructBuilder.Append(line);

                    // Check if current line is the closing delimiter for the first pending heredoc
                    string lineWithoutNewline = line.TrimEnd('\r', '\n');
                    HeredocDelimiterInfo currentDelimiter = pendingDelimiters[0];

                    // For chomp mode, strip leading tabs before checking
                    string trimmedLine = currentDelimiter.HasChomp
                        ? lineWithoutNewline.TrimStart('\t')
                        : lineWithoutNewline;

                    if (trimmedLine == currentDelimiter.Delimiter)
                    {
                        pendingDelimiters.RemoveAt(0);

                        // If all delimiters are closed, finish this construct
                        if (pendingDelimiters.Count == 0)
                        {
                            constructLines.Add(constructBuilder.ToString());
                            constructBuilder = new StringBuilder();
                        }
                    }

                    lineBuilder = new StringBuilder();
                    continue;
                }

                bool inLineContinuation = constructBuilder.Length > 0;
                bool isComment = Comment.IsComment(line);

                constructBuilder.Append(line);

                // Check for heredoc markers in the line — only for RUN, COPY, and ADD
                // (heredoc syntax is not supported for other instructions like ENV)
                if (!isComment && IsHeredocCapableInstruction(constructBuilder.ToString()))
                {
                    List<HeredocDelimiterInfo> delimiters = ExtractHeredocDelimiters(line, escapeChar);
                    if (delimiters.Count > 0)
                    {
                        pendingDelimiters.AddRange(delimiters);
                        lineBuilder = new StringBuilder();
                        continue;
                    }
                }

                if ((isComment && !inLineContinuation) ||
                    (!EndsInLineContinuation(escapeChar).TryParse(line).HasValue &&
                        !(isComment && inLineContinuation)))
                {
                    constructLines.Add(constructBuilder.ToString());
                    constructBuilder = new StringBuilder();
                }

                lineBuilder = new StringBuilder();
            }
        }

        string lastConstruct = constructBuilder.ToString() + lineBuilder.ToString();

        // If we still have pending delimiters and text, it's an unterminated heredoc
        // but we still need to add the construct
        if (lastConstruct.Length > 0)
        {
            constructLines.Add(lastConstruct);
        }

        for (int i = dockerfileConstructs.Count; i < constructLines.Count; i++)
        {
            string line = constructLines[i];
            if (Whitespace.IsWhitespace(line))
            {
                dockerfileConstructs.Add(new Whitespace(line));
            }
            else if (Comment.IsComment(line))
            {
                dockerfileConstructs.Add(Comment.Parse(line));
            }
            else
            {
                dockerfileConstructs.Add(Instruction.CreateInstruction(line, escapeChar));
            }
        }

        if (bom)
        {
            if (dockerfileConstructs.Count == 0)
            {
                dockerfileConstructs.Add(new Whitespace(""));
                constructLines.Add("");
            }
            dockerfileConstructs[0].TokenList.Insert(0, new StringToken("\uFEFF"));
            constructLines[0] = "\uFEFF" + constructLines[0];
        }

        SourceMap sourceMap = new(text);
        int offset = 0;
        for (int i = 0; i < dockerfileConstructs.Count; i++)
        {
            int end = offset + constructLines[i].Length;
            dockerfileConstructs[i].SourceSpan = sourceMap.GetSpan(offset, end);
            offset = end;
        }

        return new Dockerfile(dockerfileConstructs);
    }

    /// <summary>
    /// Returns true when the accumulated construct text begins with an instruction
    /// keyword that supports heredoc syntax (RUN, COPY, ADD).
    /// </summary>
    private static bool IsHeredocCapableInstruction(string constructText)
    {
        string trimmed = constructText.TrimStart();
        return StartsWithInstructionKeyword(trimmed, "RUN")
            || StartsWithInstructionKeyword(trimmed, "COPY")
            || StartsWithInstructionKeyword(trimmed, "ADD");
    }

    /// <summary>
    /// Checks whether <paramref name="text"/> starts with the given instruction
    /// <paramref name="keyword"/> followed by whitespace or end-of-string, ensuring
    /// a proper word boundary (e.g. "RUN" does not match "RUNNING").
    /// </summary>
    private static bool StartsWithInstructionKeyword(string text, string keyword)
    {
        if (!text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Ensure word boundary: next character must be whitespace or end-of-string
        return text.Length == keyword.Length || char.IsWhiteSpace(text[keyword.Length]);
    }

    private static TextParser<LineContinuationToken> EndsInLineContinuation(char escapeChar) =>
        from text in Superpower.Parse.Not(LineContinuationToken.GetParser(escapeChar))
            .IgnoreThen(Character.AnyChar)
            .Try().Many()
            .Text()
        from lineCont in LineContinuationToken.GetParser(escapeChar)
        select lineCont;
}
