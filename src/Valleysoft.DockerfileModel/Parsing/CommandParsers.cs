using System.Text.Json;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class CommandParsers
{
    private enum JsonArrayKind
    {
        Invalid,
        StringArray,
        NonStringArray
    }

    internal static TextParser<T> MaybeJson<T>(TextParser<T> jsonParser, TextParser<T> fallbackParser, char escapeChar) =>
        input => GetJsonArrayKind(input, escapeChar) == JsonArrayKind.Invalid
            ? fallbackParser(input)
            : jsonParser(input);

    internal static TextParser<T> RequireJson<T>(TextParser<T> parser, char escapeChar) =>
        input =>
        {
            Result<T> result = parser(input);
            return result.HasValue && GetJsonArrayKind(input, escapeChar) != JsonArrayKind.StringArray
                ? Result.Empty<T>(input, "JSON array of strings")
                : result;
        };

    private static JsonArrayKind GetJsonArrayKind(TextSpan input, char escapeChar)
    {
        string source = input.Source!;
        int first = input.Position.Absolute;
        while (first < source.Length && char.IsWhiteSpace(source[first]) && source[first] is not ('\r' or '\n'))
        {
            first++;
        }
        if (first == source.Length || (source[first] != '[' && source[first] != escapeChar))
        {
            return JsonArrayKind.Invalid;
        }

        // Recognition uses the whole logical header; token parsers still consume physical prefixes.
        string logical = ConstructReader.FoldHeader(source, input.Position.Absolute, escapeChar, out _);
        if (!logical.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return JsonArrayKind.Invalid;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(logical);
            return document.RootElement.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String)
                ? JsonArrayKind.StringArray
                : JsonArrayKind.NonStringArray;
        }
        catch (JsonException)
        {
            return JsonArrayKind.Invalid;
        }
    }

    /// <summary>
    /// Parses a set of argument literals.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    internal static TextParser<IEnumerable<Token>> ArgumentListAsLiteral(char escapeChar) =>
        ArgumentListAsLiteral(escapeChar, requireContent: false);

    internal static TextParser<IEnumerable<Token>> ArgumentListAsLiteral(char escapeChar, bool requireContent) =>
        from literals in
            InstructionParsers.ArgTokens(
                from literal in StringParsers.LiteralToken(escapeChar, Enumerable.Empty<char>())
                    .Try()
                    .OptionalOrDefault(null!)
                select (IEnumerable<Token>)(literal is null ? Array.Empty<Token>() : new Token[] { literal }),
                escapeChar)
                .Where(tokens => tokens.Any())
                .Try()
                .Many()
        let tokens = literals.Flatten().ToArray()
        where !requireContent || tokens.Length > 0
        select StringParsers.CollapseLiteralTokens(tokens, canContainVariables: false, escapeChar);

    /// <summary>
    /// Parses a JSON array of strings.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="canContainVariables">A value indicating whether variables are allowed to be contained in the strings.</param>
    /// <param name="allowEmpty">When true, allows parsing an empty array (e.g. [] or [ ]). Defaults to false.
    /// VOLUME, COPY, ADD, and exec-form command parsers all pass true to match BuildKit behavior,
    /// which accepts empty JSON arrays as a no-op.</param>
    /// <param name="useJsonStringSyntax">Separates JSON backslash escapes from Dockerfile continuations.
    /// False retains the historical operand grammar used by explicit command APIs and constructors.</param>
    internal static TextParser<IEnumerable<Token>> JsonArray(char escapeChar, bool canContainVariables, bool allowEmpty = false,
        bool useJsonStringSyntax = false) =>
        from openingBracket in BasicParsers.Symbol('[').AsEnumerable()
        // Consume optional whitespace after '[' at the array level (not inside
        // the element parser) so the empty-array fallback works correctly.
        // Without this, JsonArrayElement would consume whitespace and then fail
        // at the opening quote, preventing backtracking to the empty-array case.
        // This matches the Lean/BuildKit parser structure, which parses
        // interElementSpace before attempting the optional first element.
        from leadingWs in JsonWhitespace(escapeChar)
        from execFormArgs in JsonArrayElements(escapeChar, canContainVariables, allowEmpty, useJsonStringSyntax)
        from closingBracket in BasicParsers.Symbol(']').AsEnumerable()
        select TokenSequences.ConcatTokens(openingBracket, leadingWs, execFormArgs, closingBracket);

    /// <summary>
    /// Creates a <see cref="Tokens.LiteralToken"/> for a JSON array element from its parsed tokens.
    /// When the element is an empty string (e.g. ""), the token sequence is empty and
    /// <see cref="StringParsers.CollapseLiteralTokens"/> cannot be used because it requires non-empty input.
    /// In that case, a zero-length <see cref="Tokens.LiteralToken"/> with <see cref="Tokens.LiteralToken.QuoteChar"/>
    /// set to double-quote is returned directly.
    /// </summary>
    private static IEnumerable<Token> CreateJsonArrayElementLiteral(IEnumerable<Token> tokens,
        bool canContainVariables, char escapeChar, bool useJsonStringSyntax)
    {
        var materializedTokens = tokens.ToList();
        if (!materializedTokens.Any())
        {
            return new Token[]
            {
                new LiteralToken(Enumerable.Empty<Token>(), canContainVariables, escapeChar,
                    useJsonStringSyntax: useJsonStringSyntax)
                {
                    QuoteChar = StringParsers.DoubleQuote
                }
            };
        }

        return StringParsers.CollapseLiteralTokens(materializedTokens, canContainVariables, escapeChar,
            StringParsers.DoubleQuote, useJsonStringSyntax);
    }

    /// <summary>
    /// Parses the elements of a JSON array. When <paramref name="allowEmpty"/> is true,
    /// an empty array (no elements) is accepted via a lookahead-guarded fallback that
    /// checks for <c>]</c> before returning an empty result. The <c>.Try().Or()</c> combinator
    /// (as opposed to <c> .Try().Or()</c>) is used so that even if the element parser partially
    /// consumes whitespace before failing, it backtracks cleanly to the empty-array path
    /// for inputs like <c>[ ]</c> or <c>[\n]</c>.
    /// When false, at least one element is required and no empty fallback is added,
    /// giving clearer error messages.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="canContainVariables">A value indicating whether the string can contain variables.</param>
    /// <param name="allowEmpty">A value indicating whether an empty array is allowed.</param>
    /// <param name="useJsonStringSyntax">Whether to use JSON backslash escapes independently of Dockerfile continuations.</param>
    private static TextParser<IEnumerable<Token>> JsonArrayElements(char escapeChar, bool canContainVariables, bool allowEmpty,
        bool useJsonStringSyntax)
    {
        var elements =
            from firstArg in JsonArrayElement(escapeChar, canContainVariables, useJsonStringSyntax,
                consumeLeadingWhitespace: false).AsEnumerable().Flatten()
            from tail in (
                from delimiter in JsonArrayElementDelimiter(escapeChar)
                from nextArg in JsonArrayElement(escapeChar, canContainVariables, useJsonStringSyntax)
                select TokenSequences.ConcatTokens(delimiter, nextArg)).Try().Many()
            select TokenSequences.ConcatTokens(firstArg, tail.Flatten());

        if (allowEmpty)
        {
            // Use a lookahead for ']' so only truly empty arrays (e.g. [] or [ ]) take
            // the empty path. Without this, inputs like [foo] would silently fall through
            // to the empty branch (because the element parser fails without consuming
            // input) and report "expected ']'" instead of "expected opening quote".
            TextParser<IEnumerable<Token>> emptyArrayLookahead =
                Superpower.Parse.Not(Superpower.Parse.Not(BasicParsers.Symbol(']')))
                    .Value(Enumerable.Empty<Token>());

            // Use .Try().Or() (not  .Try().Or()) so that the empty-array lookahead is attempted
            // even if the element parser partially consumed input before failing.
            // This ensures whitespace-only empty arrays like [ ] or [\n] backtrack
            // correctly to the empty-array path.
            elements = elements.Try().Or(emptyArrayLookahead);
        }

        return elements;
    }

    /// <summary>
    /// Parses a JSON array element delimiter (i.e. comma) with optional whitespace.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    private static TextParser<IEnumerable<Token>> JsonArrayElementDelimiter(char escapeChar) =>
        from leading in JsonWhitespace(escapeChar)
        from comma in BasicParsers.Symbol(',').AsEnumerable()
        from trailing in JsonWhitespace(escapeChar)
        select TokenSequences.ConcatTokens(
            leading,
            comma,
            trailing);

    /// <summary>
    /// Parses a JSON array string element. When <paramref name="consumeLeadingWhitespace"/> is
    /// <c>false</c> (used for the first element), leading whitespace is not consumed because the
    /// caller (<see cref="JsonArray"/>) already handled it. When <c>true</c> (used for subsequent
    /// elements), leading whitespace is consumed as part of the element.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="canContainVariables">A value indicating whether the string can contain variables.</param>
    /// <param name="consumeLeadingWhitespace">Whether to consume leading whitespace before the element.</param>
    /// <param name="useJsonStringSyntax">Whether to use JSON backslash escapes independently of Dockerfile continuations.</param>
    private static TextParser<IEnumerable<Token>> JsonArrayElement(char escapeChar, bool canContainVariables,
        bool useJsonStringSyntax, bool consumeLeadingWhitespace = true)
    {
        TextParser<IEnumerable<Token>> contentParser;
        if (useJsonStringSyntax)
        {
            contentParser = JsonStringTokens(escapeChar, canContainVariables);
        }
        else
        {
            TextParser<LiteralToken> literalParser = canContainVariables ?
                VariableParsers.LiteralWithVariables(escapeChar, new char[] { StringParsers.DoubleQuote }) :
                StringParsers.LiteralToken(escapeChar, new char[] { StringParsers.DoubleQuote });
            contentParser = InstructionParsers.ArgTokens(literalParser.AsEnumerable(),
                escapeChar, excludeLeadingWhitespace: true).Try()
                .Or(BasicParsers.Whitespace().Where(tokens => tokens.Any()));
        }

        if (consumeLeadingWhitespace)
        {
            return
                from leading in JsonWhitespace(escapeChar)
                from openingQuote in BasicParsers.Symbol(StringParsers.DoubleQuote)
                from argValue in contentParser.Try().Many()
                from closingQuote in BasicParsers.Symbol(StringParsers.DoubleQuote)
                from trailing in JsonWhitespace(escapeChar)
                select TokenSequences.ConcatTokens(
                    leading,
                    CreateJsonArrayElementLiteral(argValue.Flatten(), canContainVariables, escapeChar, useJsonStringSyntax),
                    trailing);
        }

        return
            from openingQuote in BasicParsers.Symbol(StringParsers.DoubleQuote)
            from argValue in contentParser.Try().Many()
            from closingQuote in BasicParsers.Symbol(StringParsers.DoubleQuote)
            from trailing in JsonWhitespace(escapeChar)
            select TokenSequences.ConcatTokens(
                CreateJsonArrayElementLiteral(argValue.Flatten(), canContainVariables, escapeChar, useJsonStringSyntax),
                trailing);
    }

    internal static TextParser<IEnumerable<Token>> JsonStringTokens(char escapeChar, bool canContainVariables)
    {
        TextParser<IEnumerable<Token>> literalParser =
            from text in Character.Matching(ch =>
                ch != StringParsers.DoubleQuote && ch != '\\' && ch != escapeChar &&
                !char.IsWhiteSpace(ch) && (!canContainVariables || ch != '$'), "JSON string character")
                .AtLeastOnce().Text()
            select (IEnumerable<Token>)new Token[] { new StringToken(text) };

        if (canContainVariables)
        {
            literalParser = VariableRefToken.GetParser(escapeChar).Cast<VariableRefToken, Token>().AsEnumerable()
                .Try().Or(literalParser)
                .Try().Or(BasicParsers.ToStringTokens(Character.EqualTo('$')));
        }

        TextParser<IEnumerable<Token>> jsonEscape =
            from backslash in BasicParsers.ToStringTokens(Character.EqualTo('\\'))
            from escaped in BasicParsers.CharWithOptionalLineContinuation(escapeChar,
                Superpower.Parse.Not(NativeParsers.LineEnd).IgnoreThen(Character.AnyChar),
                ch => new StringToken(ch.ToString()))
            select TokenSequences.ConcatTokens(backslash, escaped);

        TextParser<IEnumerable<Token>> contentParser = JsonWhitespace(escapeChar)
            .Where(tokens => tokens.Any()).Try()
            .Or(jsonEscape.Try())
            .Or(literalParser);

        if (escapeChar != '\\')
        {
            contentParser = contentParser.Try().Or(BasicParsers.ToStringTokens(Character.EqualTo(escapeChar)));
        }

        return contentParser;
    }

    private static TextParser<IEnumerable<Token>> JsonWhitespace(char escapeChar)
    {
        // Blank-line indentation belongs to skipped physical trivia, not to the JSON string value.
        TextParser<IEnumerable<Token>> blankLine =
            from text in Span.MatchedBy(BasicParsers.WhitespaceWithoutNewLine().IgnoreThen(NativeParsers.LineEnd)).Text()
            select (IEnumerable<Token>)new Token[] { new NewLineToken(text) };
        TextParser<IEnumerable<Token>> commentLine =
            from leading in BasicParsers.WhitespaceWithoutNewLine()
            from comment in CommentToken.GetParser()
            from newline in BasicParsers.OptionalNewLine()
            select (IEnumerable<Token>)new Token[]
            {
                new CommentToken(TokenSequences.ConcatTokens(new Token?[] { leading }, comment, new Token?[] { newline }))
            };
        return
            from leading in BasicParsers.WhitespaceWithoutNewLine()
            from continued in (
                from continuation in LineContinuationToken.GetParser(escapeChar)
                from skipped in blankLine.Try().Or(commentLine).Try().Many().Flatten()
                from horizontal in BasicParsers.WhitespaceWithoutNewLine()
                select TokenSequences.ConcatTokens(new Token[] { continuation }, skipped, new Token?[] { horizontal }))
                .Try().Many().Flatten()
            from trailing in BasicParsers.Whitespace().Where(tokens => tokens.Any()).Try().Many().Flatten()
            select TokenSequences.ConcatTokens(new Token?[] { leading }, continued, trailing);
    }
}
