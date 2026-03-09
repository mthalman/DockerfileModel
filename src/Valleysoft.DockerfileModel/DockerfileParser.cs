using System.Text;
using System.Text.RegularExpressions;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

internal static class DockerfileParser
{
    // Matches <<[-]["]DELIM["] or <<[-][']DELIM['] or <<[-]DELIM on an instruction line.
    // Captures the delimiter name (without quotes). Multiple heredocs on one line are supported.
    private static readonly Regex HeredocMarkerRegex = new(
        @"<<-?(?:""([^""]+)""|'([^']+)'|([A-Za-z_][A-Za-z0-9_]*))",
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
        List<string> pendingHeredocDelimiters = new();

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
                    if (lineContent == pendingHeredocDelimiters[0] ||
                        lineContent.TrimStart('\t') == pendingHeredocDelimiters[0])
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
                    List<string> delimiters = ExtractHeredocDelimiters(line);
                    if (delimiters.Count > 0)
                    {
                        pendingHeredocDelimiters.AddRange(delimiters);
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
    /// Extracts heredoc delimiter names from a line of text.
    /// Returns the list of delimiter names that need to be closed (in order).
    /// </summary>
    internal static List<string> ExtractHeredocDelimiters(string line)
    {
        List<string> delimiters = new();
        MatchCollection matches = HeredocMarkerRegex.Matches(line);
        foreach (Match match in matches)
        {
            // Group 1 = double-quoted, Group 2 = single-quoted, Group 3 = unquoted
            string delimiter = match.Groups[1].Success ? match.Groups[1].Value :
                               match.Groups[2].Success ? match.Groups[2].Value :
                               match.Groups[3].Value;
            delimiters.Add(delimiter);
        }
        return delimiters;
    }

    private static Parser<LineContinuationToken> EndsInLineContinuation(char escapeChar) =>
        from text in Parse.AnyChar.Except(LineContinuationToken.GetParser(escapeChar)).Many().Text()
        from lineCont in LineContinuationToken.GetParser(escapeChar)
        select lineCont;
}
