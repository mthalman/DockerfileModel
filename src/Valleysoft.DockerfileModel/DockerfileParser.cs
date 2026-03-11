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
    public static List<HeredocDelimiterInfo> ExtractHeredocDelimiters(string line)
    {
        List<HeredocDelimiterInfo> result = new();

        // Strip trailing comment before searching for heredoc markers
        string strippedLine = StripTrailingComment(line);

        // Use a quote-aware scan so that `<<` inside quoted strings
        // (e.g. `RUN echo "<<EOF"`) is not treated as a heredoc marker.
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        for (int i = 0; i < strippedLine.Length; i++)
        {
            char ch = strippedLine[i];

            // Skip escaped characters (backslash is not special inside single quotes)
            if (ch == '\\' && !inSingleQuote && i + 1 < strippedLine.Length)
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
    /// Strips a trailing comment from a line, respecting quoted strings.
    /// A '#' character inside single or double quotes is NOT treated as a comment.
    /// </summary>
    public static string StripTrailingComment(string line)
    {
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];

            if (ch == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (ch == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (ch == '#' && !inSingleQuote && !inDoubleQuote && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                return line.Substring(0, i);
            }
        }

        return line;
    }

    public static Dockerfile ParseContent(string text)
    {
        bool parserDirectivesComplete = false;
        char escapeChar = Dockerfile.DefaultEscapeChar;

        List<DockerfileConstruct> dockerfileConstructs = new();

        List<string> constructLines = new();
        StringBuilder constructBuilder = new();
        StringBuilder lineBuilder = new();
        List<HeredocDelimiterInfo> pendingDelimiters = new();

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];

            lineBuilder.Append(ch);

            if (ch == '\n')
            {
                string line = lineBuilder.ToString();
                if (!parserDirectivesComplete)
                {
                    if (ParserDirective.IsParserDirective(line))
                    {
                        ParserDirective? parserDirective = ParserDirective.Parse(line);
                        dockerfileConstructs.Add(parserDirective);
                        constructLines.Add(line);

                        if (parserDirective.DirectiveName.Equals(
                            ParserDirective.EscapeDirective, StringComparison.OrdinalIgnoreCase))
                        {
                            escapeChar = parserDirective.DirectiveValue[0];
                        }
                        lineBuilder = new StringBuilder();
                        continue;
                    }
                    else
                    {
                        parserDirectivesComplete = true;
                    }
                }

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

                constructBuilder.Append(line);

                // Check for heredoc markers in the line — only for RUN, COPY, and ADD
                // (heredoc syntax is not supported for other instructions like ENV)
                if (!Comment.IsComment(line) && IsHeredocCapableInstruction(constructBuilder.ToString()))
                {
                    List<HeredocDelimiterInfo> delimiters = ExtractHeredocDelimiters(line);
                    if (delimiters.Count > 0)
                    {
                        pendingDelimiters.AddRange(delimiters);
                        lineBuilder = new StringBuilder();
                        continue;
                    }
                }

                if (!EndsInLineContinuation(escapeChar).TryParse(line).WasSuccessful &&
                    !(Comment.IsComment(line) && inLineContinuation))
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

        return new Dockerfile(dockerfileConstructs);
    }

    /// <summary>
    /// Returns true when the accumulated construct text begins with an instruction
    /// keyword that supports heredoc syntax (RUN, COPY, ADD).
    /// </summary>
    private static bool IsHeredocCapableInstruction(string constructText)
    {
        string trimmed = constructText.TrimStart();
        return trimmed.StartsWith("RUN", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("COPY", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ADD", StringComparison.OrdinalIgnoreCase);
    }

    private static Parser<LineContinuationToken> EndsInLineContinuation(char escapeChar) =>
        from text in Parse.AnyChar.Except(LineContinuationToken.GetParser(escapeChar)).Many().Text()
        from lineCont in LineContinuationToken.GetParser(escapeChar)
        select lineCont;
}
