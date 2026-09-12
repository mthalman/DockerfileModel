using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

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

    public Parser<IEnumerable<Token>> HeredocParser(char escapeChar, bool canContainVariables = false) =>
        input =>
        {
            if (!HasHeredocs || input.Position > region.Heredocs[0].MarkerStart - sourceStart ||
                region.End - sourceStart > input.Source.Length)
            {
                return Result.Failure<IEnumerable<Token>>(input, "Expected a complete heredoc.", new[] { "heredoc" });
            }

            List<Token> tokens = new();
            int position = input.Position;
            foreach (ConstructReader.HeredocRegion heredoc in region.Heredocs)
            {
                int markerStart = heredoc.MarkerStart - sourceStart;
                tokens.AddRange(ParseGap(input.Source, position, markerStart - position,
                    escapeChar, canContainVariables));

                List<Token> marker = new() { new SymbolToken('<') };
                int secondOperatorStart = heredoc.SecondOperatorStart - sourceStart;
                marker.AddRange(ParseGap(input.Source, markerStart + 1, secondOperatorStart - markerStart - 1,
                    escapeChar, false));
                marker.Add(new SymbolToken('<'));
                int operatorEnd = secondOperatorStart + 1;
                if (heredoc.ChompStart is int chompStart)
                {
                    chompStart -= sourceStart;
                    marker.AddRange(ParseGap(input.Source, operatorEnd, chompStart - operatorEnd, escapeChar, false));
                    marker.Add(new SymbolToken('-'));
                    operatorEnd = chompStart + 1;
                }
                int delimiterStart = heredoc.DelimiterStart - sourceStart;
                marker.AddRange(ParseGap(input.Source, operatorEnd, delimiterStart - operatorEnd,
                    escapeChar, false));
                string rawDelimiter = input.Source.Substring(delimiterStart, heredoc.MarkerEnd - sourceStart - delimiterStart);
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
                    return Result.Failure<IEnumerable<Token>>(input,
                        "The heredoc delimiter could not be represented without changing its value.", new[] { "heredoc delimiter" });
                }
                tokens.Add(markerToken);
                position = heredoc.MarkerEnd - sourceStart;
            }

            int argumentsEnd = (region.TrailingCommentStart ?? region.HeaderEnd) - sourceStart;
            tokens.AddRange(ParseGap(input.Source, position, argumentsEnd - position,
                escapeChar, canContainVariables));
            if (region.TrailingCommentStart.HasValue)
            {
                Parser<IEnumerable<Token>> commentParser =
                    from marker in Symbol('#')
                    from leading in WhitespaceWithoutNewLine()
                    from text in Sprache.Parse.AnyChar.Except(Sprache.Parse.LineEnd.End()).Many().Text()
                    from lineEnd in OptionalNewLine().AsEnumerable()
                    select ConcatTokens(new Token[]
                    {
                        new CommentToken(ConcatTokens(ConcatTokens(marker, leading),
                            ConcatTokens(new StringToken(text.Trim()), GetTrailingWhitespaceToken(text))))
                    }, lineEnd);
                tokens.AddRange(commentParser.End().Parse(input.Source.Substring(argumentsEnd,
                    region.HeaderEnd - sourceStart - argumentsEnd)));
            }
            foreach (ConstructReader.HeredocRegion heredoc in region.Heredocs)
            {
                List<Token> body = new();
                if (heredoc.ClosingLineStart > heredoc.BodyStart)
                {
                    body.Add(new StringToken(input.Source.Substring(heredoc.BodyStart - sourceStart,
                        heredoc.ClosingLineStart - heredoc.BodyStart)));
                }
                if (heredoc.ClosingNameStart > heredoc.ClosingLineStart)
                {
                    body.Add(new StringToken(input.Source.Substring(heredoc.ClosingLineStart - sourceStart,
                        heredoc.ClosingNameStart - heredoc.ClosingLineStart)));
                }
                body.Add(new HeredocDelimiterToken(new Token[] { new StringToken(heredoc.Name) }));
                if (heredoc.End > heredoc.ClosingNameEnd)
                {
                    body.Add(new NewLineToken(input.Source.Substring(heredoc.ClosingNameEnd - sourceStart,
                        heredoc.End - heredoc.ClosingNameEnd)));
                }
                tokens.Add(new HeredocBodyToken(body));
            }

            IInput remainder = input;
            while (remainder.Position < region.End - sourceStart)
            {
                remainder = remainder.Advance();
            }
            return Result.Success<IEnumerable<Token>>(tokens, remainder);
        };

    private static IEnumerable<Token> ParseGap(string source, int offset, int length, char escapeChar, bool canContainVariables)
    {
        Parser<LiteralToken> literal = canContainVariables
            ? LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes)
            : LiteralToken(escapeChar, Array.Empty<char>());
        Parser<IEnumerable<Token>> parser =
            from leading in ArgTrailingWhitespace(escapeChar)
            from args in ArgTokens(literal.AsEnumerable(), escapeChar).Many()
            from trailing in (from whitespace in Whitespace()
                              where whitespace.Any()
                              select whitespace).Many()
            select ConcatTokens(leading, args.Flatten(), trailing.Flatten());
        IResult<IEnumerable<Token>> result = parser.End().TryParse(source.Substring(offset, length));
        if (!result.WasSuccessful)
        {
            int position = offset + result.Remainder.Position;
            SourcePosition location = new SourceMap(source).GetSpan(position, position).Start;
            throw new ParseException(result.Message, new Position(position, location.Line, location.Column));
        }
        return result.Value;
    }

    private static IEnumerable<Token> RawDelimiterTokens(string text, char escapeChar)
    {
        Parser<Token> continuation = LineContinuationToken.GetParser(escapeChar).Cast<LineContinuationToken, Token>();
        Parser<Token> content =
            from value in Sprache.Parse.AnyChar.Except(continuation).AtLeastOnce().Text()
            select (Token)new StringToken(value);
        return continuation.Or(content).Many().End().Parse(text);
    }
}
