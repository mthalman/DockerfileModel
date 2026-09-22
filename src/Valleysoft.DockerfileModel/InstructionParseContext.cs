using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

// Explicit per-parse context keeps the diagnostic path out of legacy parsers
// without ambient state. Nested ONBUILD parsing shares the same original regions.
internal sealed class InstructionParseContext
{
    private readonly ConstructReader.Region region;
    private readonly int sourceStart;

    public InstructionParseContext(ConstructReader.Region region, int sourceStart)
    {
        this.region = region;
        this.sourceStart = sourceStart;
    }

    public bool HasHeredocs => region.Heredocs.Count > 0;

    public InstructionParseContext Slice(int offset) => new(region, sourceStart + offset);

    public TextParser<IEnumerable<Token>> HeredocParser(char escapeChar, bool canContainVariables = false) =>
        input =>
        {
            string inputSource = input.Source!;
            int inputOffset = input.Position.Absolute;
            if (!HasHeredocs || inputOffset > region.Heredocs[0].MarkerStart - sourceStart ||
                region.End - sourceStart > inputSource.Length)
            {
                return Result.Empty<IEnumerable<Token>>(input, "complete heredoc");
            }

            List<Token> tokens = new();
            int position = inputOffset;
            foreach (ConstructReader.HeredocRegion heredoc in region.Heredocs)
            {
                int markerStart = heredoc.MarkerStart - sourceStart;
                tokens.AddRange(ParseGap(inputSource, position, markerStart - position,
                    escapeChar, canContainVariables));

                List<Token> marker = new() { new SymbolToken('<') };
                int secondOperatorStart = heredoc.SecondOperatorStart - sourceStart;
                marker.AddRange(ParseGap(inputSource, markerStart + 1, secondOperatorStart - markerStart - 1,
                    escapeChar, false));
                marker.Add(new SymbolToken('<'));
                int operatorEnd = secondOperatorStart + 1;
                if (heredoc.ChompStart is int chompStart)
                {
                    chompStart -= sourceStart;
                    marker.AddRange(ParseGap(inputSource, operatorEnd, chompStart - operatorEnd, escapeChar, false));
                    marker.Add(new SymbolToken('-'));
                    operatorEnd = chompStart + 1;
                }
                int delimiterStart = heredoc.DelimiterStart - sourceStart;
                marker.AddRange(ParseGap(inputSource, operatorEnd, delimiterStart - operatorEnd,
                    escapeChar, false));
                string rawDelimiter = inputSource.Substring(delimiterStart, heredoc.MarkerEnd - sourceStart - delimiterStart);
                char? quote = rawDelimiter.Length >= 2 && (rawDelimiter[0] == '\'' || rawDelimiter[0] == '"') &&
                    rawDelimiter[rawDelimiter.Length - 1] == rawDelimiter[0] ? rawDelimiter[0] : null;
                if (quote is char quoteChar)
                {
                    marker.Add(new SymbolToken(quoteChar));
                    rawDelimiter = rawDelimiter.Substring(1, rawDelimiter.Length - 2);
                }
                marker.Add(new HeredocDelimiterToken(RawDelimiterTokens(rawDelimiter, escapeChar)));
                if (quote is char closingQuote)
                {
                    marker.Add(new SymbolToken(closingQuote));
                }
                HeredocMarkerToken markerToken = new(marker, diagnostic: true);
                if (markerToken.DelimiterName != heredoc.Name)
                {
                    return Result.Empty<IEnumerable<Token>>(input, "representable heredoc delimiter");
                }
                tokens.Add(markerToken);
                position = heredoc.MarkerEnd - sourceStart;
            }

            int argumentsEnd = (region.TrailingCommentStart ?? region.HeaderEnd) - sourceStart;
            tokens.AddRange(ParseGap(inputSource, position, argumentsEnd - position,
                escapeChar, canContainVariables));
            if (region.TrailingCommentStart.HasValue)
            {
                TextParser<IEnumerable<Token>> commentParser =
                    from marker in Symbol('#')
                    from leading in WhitespaceWithoutNewLine()
                    from text in Superpower.Parse.Not(NativeParsers.LineEnd.AtEnd())
                        .IgnoreThen(Character.AnyChar)
                        .Try().Many()
                        .Text()
                    from lineEnd in OptionalNewLine().AsEnumerable()
                    select ConcatTokens(new Token[]
                    {
                        new CommentToken(ConcatTokens(ConcatTokens(marker, leading),
                            ConcatTokens(new StringToken(text.Trim()), GetTrailingWhitespaceToken(text))))
                    }, lineEnd);
                tokens.AddRange(commentParser.AtEnd().Parse(inputSource.Substring(argumentsEnd,
                    region.HeaderEnd - sourceStart - argumentsEnd)));
            }
            foreach (ConstructReader.HeredocRegion heredoc in region.Heredocs)
            {
                List<Token> body = new();
                if (heredoc.ClosingLineStart > heredoc.BodyStart)
                {
                    body.Add(new StringToken(inputSource.Substring(heredoc.BodyStart - sourceStart,
                        heredoc.ClosingLineStart - heredoc.BodyStart)));
                }
                if (heredoc.ClosingNameStart > heredoc.ClosingLineStart)
                {
                    body.Add(new StringToken(inputSource.Substring(heredoc.ClosingLineStart - sourceStart,
                        heredoc.ClosingNameStart - heredoc.ClosingLineStart)));
                }
                body.Add(new HeredocDelimiterToken(new Token[] { new StringToken(heredoc.Name) }));
                if (heredoc.End > heredoc.ClosingNameEnd)
                {
                    body.Add(new NewLineToken(inputSource.Substring(heredoc.ClosingNameEnd - sourceStart,
                        heredoc.End - heredoc.ClosingNameEnd)));
                }
                tokens.Add(new HeredocBodyToken(body));
            }

            TextSpan remainder = input.Skip(region.End - sourceStart - inputOffset);
            return Result.Value<IEnumerable<Token>>(tokens, input, remainder);
        };

    private static IEnumerable<Token> ParseGap(string source, int offset, int length, char escapeChar, bool canContainVariables)
    {
        TextParser<LiteralToken> literal = canContainVariables
            ? LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes)
            : LiteralToken(escapeChar, Array.Empty<char>());
        TextParser<IEnumerable<Token>> parser =
            from leading in ArgTrailingWhitespace(escapeChar)
            from args in ArgTokens(literal.AsEnumerable(), escapeChar).Try().Many()
            from trailing in (from whitespace in Whitespace()
                              where whitespace.Any()
                              select whitespace).Try().Many()
            select ConcatTokens(leading, args.Flatten(), trailing.Flatten());
        Result<IEnumerable<Token>> result = parser.AtEnd().TryParse(source.Substring(offset, length));
        if (!result.HasValue)
        {
            int position = offset + result.ErrorPosition.Absolute;
            SourcePosition location = new SourceMap(source).GetSpan(position, position).Start;
            throw new ParseException(
                result.ErrorMessage ?? result.ToString(),
                new Position(position, location.Line, location.Column));
        }
        return result.Value;
    }

    private static IEnumerable<Token> RawDelimiterTokens(string text, char escapeChar)
    {
        TextParser<Token> continuation = LineContinuationToken.GetParser(escapeChar).Cast<LineContinuationToken, Token>();
        TextParser<Token> content =
            from value in Superpower.Parse.Not(continuation)
                .IgnoreThen(Character.AnyChar)
                .Try().AtLeastOnce()
                .Text()
            select (Token)new StringToken(value);
        return continuation.Try().Or(content).Try().Many().AtEnd().Parse(text);
    }
}
