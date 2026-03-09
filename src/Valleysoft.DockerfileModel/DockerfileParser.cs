using System.Text;
using System.Text.RegularExpressions;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

internal static class DockerfileParser
{
    // Matches <<[-]["]DELIM["] or <<[-][']DELIM['] or <<[-]DELIM on an instruction line.
    // Group 1 captures the optional chomp flag '-'.
    // Groups 2/3/4 capture the delimiter name (double-quoted / single-quoted / unquoted).
    // The delimiter character class [A-Za-z0-9_.\-]+ is intentionally the same set that
    // IsHeredocDelimiterChar in ParseHelper accepts, so detection and parsing stay aligned.
    //
    // Known limitation: this regex is not quote-aware and can produce false positives when
    // a <<DELIM sequence appears inside a quoted string or JSON exec-form argument
    // (e.g. RUN ['echo', 'a <<EOF']). Making it fully quote-aware would require tracking
    // quote state across the entire line, which is complex and invasive; that improvement
    // is deferred. In practice, valid Dockerfiles do not place heredoc markers inside
    // quoted exec-form arrays, so the risk of false positives is low.
    private static readonly Regex HeredocMarkerRegex = new(
        @"<<(-?)(?:""([A-Za-z0-9_.\-]+)""|'([A-Za-z0-9_.\-]+)'|([A-Za-z_][A-Za-z0-9_.\-]*))",
        RegexOptions.Compiled);

    public static Dockerfile ParseContent(string text)
    {
        bool parserDirectivesComplete = false;
        char escapeChar = Dockerfile.DefaultEscapeChar;

        List<DockerfileConstruct> dockerfileConstructs = new();

        List<string> constructLines = new();
        StringBuilder constructBuilder = new();
        StringBuilder lineBuilder = new();

        // Heredoc state: when we detect a heredoc marker on an instruction line,
        // we keep consuming lines until all heredoc delimiters are closed.
        // Each entry tracks the delimiter name and whether the chomp flag (<<-) was used.
        List<(string Delimiter, bool HasChomp)> pendingHeredocDelimiters = new();

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

                bool inLineContinuation = constructBuilder.Length > 0;

                constructBuilder.Append(line);

                // If we are inside a heredoc body, check if this line closes
                // the current heredoc delimiter.
                if (pendingHeredocDelimiters.Count > 0)
                {
                    string lineContent = line.TrimEnd('\r', '\n');
                    // Only apply tab-trimming for chomp (<<-) heredocs; non-chomp
                    // heredocs require an exact match for the closing delimiter.
                    string effectiveLine = pendingHeredocDelimiters[0].HasChomp
                        ? lineContent.TrimStart('\t')
                        : lineContent;
                    if (effectiveLine == pendingHeredocDelimiters[0].Delimiter)
                    {
                        pendingHeredocDelimiters.RemoveAt(0);
                    }

                    // If all heredocs are closed, flush the construct.
                    if (pendingHeredocDelimiters.Count == 0)
                    {
                        constructLines.Add(constructBuilder.ToString());
                        constructBuilder = new StringBuilder();
                    }

                    lineBuilder = new StringBuilder();
                    continue;
                }

                // Check if this line (when not inside a heredoc) opens any heredocs.
                if (!Comment.IsComment(line))
                {
                    List<(string Delimiter, bool HasChomp)> delimiters = ExtractHeredocDelimiters(line);
                    if (delimiters.Count > 0)
                    {
                        // The instruction parser currently only supports a single heredoc
                        // per instruction line, so only track the first marker even if
                        // multiple are detected on the same line.
                        pendingHeredocDelimiters.Add(delimiters[0]);
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
    /// Extracts heredoc delimiter names and chomp flags from a line of text.
    /// Returns the list of (delimiter, hasChomp) tuples that need to be closed (in order).
    /// </summary>
    internal static List<(string Delimiter, bool HasChomp)> ExtractHeredocDelimiters(string line)
    {
        List<(string Delimiter, bool HasChomp)> delimiters = new();
        MatchCollection matches = HeredocMarkerRegex.Matches(line);
        foreach (Match match in matches)
        {
            // Group 1 = chomp flag '-' (may be empty), Group 2 = double-quoted,
            // Group 3 = single-quoted, Group 4 = unquoted
            bool hasChomp = match.Groups[1].Value == "-";
            string delimiter = match.Groups[2].Success ? match.Groups[2].Value :
                               match.Groups[3].Success ? match.Groups[3].Value :
                               match.Groups[4].Value;
            delimiters.Add((delimiter, hasChomp));
        }
        return delimiters;
    }

    private static Parser<LineContinuationToken> EndsInLineContinuation(char escapeChar) =>
        from text in Parse.AnyChar.Except(LineContinuationToken.GetParser(escapeChar)).Many().Text()
        from lineCont in LineContinuationToken.GetParser(escapeChar)
        select lineCont;
}
