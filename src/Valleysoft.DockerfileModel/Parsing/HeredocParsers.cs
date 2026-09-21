using System.Text;
using System.Text.RegularExpressions;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class HeredocParsers
{
    /// <summary>
    /// Parses one or more heredoc markers and bodies from the input.
    /// Produces HeredocMarkerTokens inline in the command stream, StringTokens for text between markers,
    /// a NewLineToken for the end of the command line, then HeredocBodyTokens sequentially.
    /// </summary>
    /// <returns>A parser that produces a flat list of instruction-level tokens.</returns>
    internal static Parser<IEnumerable<Token>> HeredocTokenParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        input => HeredocTokenParseImpl(input, escapeChar);

    private static readonly Regex HeredocMarkerRegex = new(
        @"<<(-?)(?:(['""])([^\r\n]+?)\2|([^\s]+))");
    // Regex for scanning the rest of the line for additional markers.
    private static readonly Regex HeredocMarkerScanRegex = new(
        @"<<(-?)(?:(['""])([^\r\n]+?)\2|([^\s]+))");

    /// <summary>
    /// Holds parsed information about a heredoc marker found on the command line.
    /// </summary>
    private class HeredocMarkerInfo
    {
        public string DelimiterName { get; set; } = "";
        public bool Chomp { get; set; }
        public char? QuoteChar { get; set; }
        /// <summary>Position in source where this marker starts.</summary>
        public int MarkerStartPos { get; set; }
        /// <summary>Position in source immediately after this marker.</summary>
        public int MarkerEndPos { get; set; }
    }

    /// <summary>
    /// Produces a flat list of instruction-level tokens for heredoc syntax:
    /// HeredocMarkerToken(s) inline, StringToken(s) for text between markers,
    /// NewLineToken for end of command line, HeredocBodyToken(s) in marker order.
    /// </summary>
    private static IResult<IEnumerable<Token>> HeredocTokenParseImpl(IInput input, char escapeChar)
    {
        string source = input.Source;
        int pos = input.Position;

        // Must start with <<
        if (pos + 1 >= source.Length || source[pos] != '<' || source[pos + 1] != '<')
        {
            return Result.Failure<IEnumerable<Token>>(
                input, "Expected heredoc marker <<", Array.Empty<string>());
        }

        // Match the first marker using regex starting from current position
        Match firstMatch = HeredocMarkerRegex.Match(source, pos);
        if (!firstMatch.Success || firstMatch.Index != pos)
        {
            return Result.Failure<IEnumerable<Token>>(
                input, "Invalid heredoc marker", Array.Empty<string>());
        }

        // Find end of the command line (the line containing the marker(s))
        int lineEndPos = pos;
        while (lineEndPos < source.Length && source[lineEndPos] != '\n' && source[lineEndPos] != '\r')
        {
            lineEndPos++;
        }

        string commandLine = source.Substring(pos, lineEndPos - pos);

        // Strip trailing comments before scanning so that `RUN <<EOF # <<BAR`
        // does not pick up `<<BAR` as a second marker (matches DockerfileParser behaviour).
        string strippedCommandLine = DockerfileParser.StripTrailingComment(commandLine, escapeChar);

        // Scan the (comment-stripped) command line for all heredoc markers,
        // respecting shell quoting so that e.g. `echo "<<BAR"` does not
        // produce a spurious marker for <<BAR.
        List<HeredocMarkerInfo> markers = new();
        {
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            for (int scanIdx = 0; scanIdx < strippedCommandLine.Length; scanIdx++)
            {
                char sc = strippedCommandLine[scanIdx];

                // Skip escaped characters (the active escape char is not special inside single quotes)
                if (sc == escapeChar && !inSingleQuote && scanIdx + 1 < strippedCommandLine.Length)
                {
                    scanIdx++; // skip next character
                    continue;
                }

                if (sc == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                    continue;
                }

                if (sc == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }

                // Only look for << when outside quotes
                if (!inSingleQuote && !inDoubleQuote
                    && sc == '<' && scanIdx + 1 < strippedCommandLine.Length && strippedCommandLine[scanIdx + 1] == '<')
                {
                    Match m = HeredocMarkerScanRegex.Match(strippedCommandLine, scanIdx);
                    if (m.Success && m.Index == scanIdx)
                    {
                        markers.Add(new HeredocMarkerInfo
                        {
                            DelimiterName = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value,
                            Chomp = m.Groups[1].Value == "-",
                            QuoteChar = m.Groups[2].Success && m.Groups[2].Value != "" ? m.Groups[2].Value[0] : null,
                            MarkerStartPos = pos + m.Index,
                            MarkerEndPos = pos + m.Index + m.Length
                        });
                        // Advance past the matched marker text
                        scanIdx += m.Length - 1;
                    }
                }
            }
        }

        // Build the flat list of instruction-level tokens
        List<Token> resultTokens = new();

        // Emit inline tokens: HeredocMarkerToken for each marker, StringToken for text between markers
        int cursor = pos;
        for (int i = 0; i < markers.Count; i++)
        {
            // Text before this marker (gap between previous marker/start and this marker)
            if (markers[i].MarkerStartPos > cursor)
            {
                string gap = source.Substring(cursor, markers[i].MarkerStartPos - cursor);
                resultTokens.Add(new StringToken(gap));
            }

            // The marker itself — decomposed into child tokens
            List<Token> markerChildren = new()
            {
                new SymbolToken('<'),
                new SymbolToken('<')
            };
            if (markers[i].Chomp)
            {
                markerChildren.Add(new SymbolToken('-'));
            }
            char? quoteChar = markers[i].QuoteChar;
            if (quoteChar.HasValue)
            {
                markerChildren.Add(new SymbolToken(quoteChar.Value));
            }
            markerChildren.Add(new HeredocDelimiterToken(markers[i].DelimiterName));
            if (quoteChar.HasValue)
            {
                markerChildren.Add(new SymbolToken(quoteChar.Value));
            }
            resultTokens.Add(new HeredocMarkerToken(markerChildren));

            cursor = markers[i].MarkerEndPos;
        }

        // Text after the last marker to end of command line —
        // tokenize into WhitespaceToken + LiteralToken segments so that
        // FileTransferInstruction.DestinationToken can find the destination.
        // Strip trailing comments first so that `# comment` is not tokenized
        // as LiteralTokens (which would confuse DestinationToken).
        if (lineEndPos > cursor)
        {
            string restOfLine = source.Substring(cursor, lineEndPos - cursor);
            string argsOnly = DockerfileParser.StripTrailingComment(restOfLine, escapeChar);
            TokenizeRestOfLine(argsOnly, resultTokens, escapeChar);

            // Preserve the comment portion as a CommentToken so that
            // Instruction.Comments and ExcludeComments work correctly.
            if (argsOnly.Length < restOfLine.Length)
            {
                string commentSegment = restOfLine.Substring(argsOnly.Length);
                resultTokens.Add(CommentToken.Parse(commentSegment));
            }
        }

        // Newline after command line
        if (lineEndPos < source.Length)
        {
            string newLine = ReadNewLine(source, lineEndPos);
            resultTokens.Add(new NewLineToken(newLine));
            lineEndPos += newLine.Length;
        }
        else
        {
            // No newline after marker - no bodies possible
            IInput advancedInput = AdvanceInput(input, lineEndPos - pos);
            return Result.Success<IEnumerable<Token>>(resultTokens, advancedInput);
        }

        // Read bodies for all markers sequentially
        int currentPos = lineEndPos;
        for (int i = 0; i < markers.Count; i++)
        {
            List<Token> bodyChildren = new();
            bool found = false;
            StringBuilder bodyContent = new();

            while (currentPos < source.Length)
            {
                int lineStart = currentPos;
                int lineEnd = FindLineEnd(source, lineStart);
                string lineContent = source.Substring(lineStart, lineEnd - lineStart);

                string trimmedLine = markers[i].Chomp ? lineContent.TrimStart('\t') : lineContent;
                if (trimmedLine == markers[i].DelimiterName)
                {
                    // Emit accumulated body content as a single StringToken (if non-empty)
                    if (bodyContent.Length > 0)
                    {
                        bodyChildren.Add(new StringToken(bodyContent.ToString()));
                    }

                    // Closing delimiter — use HeredocDelimiterToken for the delimiter name
                    // lineContent may include leading tabs (for chomp mode); preserve them as StringToken
                    if (lineContent.Length > markers[i].DelimiterName.Length)
                    {
                        string prefix = lineContent.Substring(0, lineContent.Length - markers[i].DelimiterName.Length);
                        bodyChildren.Add(new StringToken(prefix));
                    }
                    bodyChildren.Add(new HeredocDelimiterToken(markers[i].DelimiterName));
                    int afterDelim = lineEnd;
                    if (afterDelim < source.Length)
                    {
                        string delimNewLine = ReadNewLine(source, afterDelim);
                        bodyChildren.Add(new NewLineToken(delimNewLine));
                        afterDelim += delimNewLine.Length;
                    }
                    currentPos = afterDelim;
                    found = true;
                    break;
                }

                string bodyNewLine = lineEnd < source.Length ? ReadNewLine(source, lineEnd) : "";
                bodyContent.Append(lineContent + bodyNewLine);
                currentPos = lineEnd + bodyNewLine.Length;
            }

            if (!found)
            {
                // Unterminated heredoc — emit accumulated body content as a single StringToken
                if (bodyContent.Length > 0)
                {
                    bodyChildren.Add(new StringToken(bodyContent.ToString()));
                }
            }

            resultTokens.Add(new HeredocBodyToken(bodyChildren));
        }

        IInput advancedInput2 = AdvanceInput(input, currentPos - pos);
        return Result.Success<IEnumerable<Token>>(resultTokens, advancedInput2);
    }

    /// <summary>
    /// Tokenizes the rest-of-line text (after the last heredoc marker) into alternating
    /// WhitespaceToken and LiteralToken segments. This allows FileTransferInstruction.DestinationToken
    /// to find the destination path as a LiteralToken for COPY/ADD heredocs.
    /// </summary>
    private static void TokenizeRestOfLine(string text, List<Token> tokens, char escapeChar)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                int start = i;
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
                tokens.Add(new WhitespaceToken(text.Substring(start, i - start)));
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
                tokens.Add(new LiteralToken(text.Substring(start, i - start), canContainVariables: true, escapeChar));
            }
        }
    }

    /// <summary>
    /// Reads a newline sequence (\n or \r\n) at the given position.
    /// </summary>
    private static string ReadNewLine(string source, int position)
    {
        if (source[position] == '\r' && position + 1 < source.Length && source[position + 1] == '\n')
        {
            return "\r\n";
        }
        return source[position].ToString();
    }

    /// <summary>
    /// Finds the end of a line (position of \n or \r, or end of string).
    /// </summary>
    private static int FindLineEnd(string source, int start)
    {
        int pos = start;
        while (pos < source.Length && source[pos] != '\n' && source[pos] != '\r')
        {
            pos++;
        }
        return pos;
    }

    /// <summary>
    /// Advances the input by the specified number of characters using direct position offset.
    /// Line and column are computed by scanning the source up to the target position,
    /// so the runtime complexity scales with the position in the source.
    /// </summary>
    private static IInput AdvanceInput(IInput input, int count)
    {
        return new OffsetInput(input.Source, input.Position + count, input.Memos);
    }

    /// <summary>
    /// Lightweight <see cref="IInput"/> that jumps to an arbitrary position
    /// without walking character-by-character.  Line and column are computed
    /// by scanning the source string up to the target position.
    /// </summary>
    private sealed class OffsetInput : IInput, IEquatable<IInput>
    {
        private readonly string _source;
        private readonly int _position;
        private readonly int _line;
        private readonly int _column;

        public OffsetInput(string source, int position, IDictionary<object, object> memos)
        {
            _source = source;
            _position = position;

            // Compute line and column by scanning source up to position
            int line = 1;
            int lastNewlinePos = -1;
            for (int i = 0; i < position && i < source.Length; i++)
            {
                if (source[i] == '\n')
                {
                    line++;
                    lastNewlinePos = i;
                }
            }
            _line = line;
            _column = position - lastNewlinePos;

            Memos = memos;
        }

        private OffsetInput(string source, int position, int line, int column, IDictionary<object, object> memos)
        {
            _source = source;
            _position = position;
            _line = line;
            _column = column;
            Memos = memos;
        }

        public string Source => _source;
        public int Position => _position;
        public bool AtEnd => _position >= _source.Length;
        public char Current => _source[_position];
        public int Line => _line;
        public int Column => _column;
        public IDictionary<object, object> Memos { get; }

        public IInput Advance()
        {
            if (AtEnd)
            {
                throw new InvalidOperationException("The input is already at the end of the source.");
            }

            int newLine = _line;
            int newColumn = _column + 1;
            if (_source[_position] == '\n')
            {
                newLine++;
                newColumn = 1;
            }
            return new OffsetInput(_source, _position + 1, newLine, newColumn, Memos);
        }

        public bool Equals(IInput? other)
        {
            return other is not null && _source == other.Source && _position == other.Position;
        }

        public override bool Equals(object? obj) => obj is IInput other && Equals(other);

        public override int GetHashCode() => (_source?.GetHashCode() ?? 0) ^ _position;
    }
}
