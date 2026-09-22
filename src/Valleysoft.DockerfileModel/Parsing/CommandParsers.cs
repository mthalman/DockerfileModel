using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class CommandParsers
{
    /// <summary>
    /// Parses a set of argument literals.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    internal static Parser<IEnumerable<Token>> ArgumentListAsLiteral(char escapeChar) =>
        ArgumentListAsLiteral(escapeChar, requireContent: false);

    internal static Parser<IEnumerable<Token>> ArgumentListAsLiteral(char escapeChar, bool requireContent) =>
        from literals in
            InstructionParsers.ArgTokens(
                from literal in StringParsers.LiteralToken(escapeChar, Enumerable.Empty<char>()).Optional()
                select new Token[] { literal.GetOrDefault() },
                escapeChar).Many()
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
    internal static Parser<IEnumerable<Token>> JsonArray(char escapeChar, bool canContainVariables, bool allowEmpty = false) =>
        from openingBracket in BasicParsers.Symbol('[').AsEnumerable()
        // Consume optional whitespace after '[' at the array level (not inside
        // the element parser) so the empty-array fallback works correctly.
        // Without this, JsonArrayElement would consume whitespace and then fail
        // at the opening quote, preventing backtracking to the empty-array case.
        // This matches the Lean/BuildKit parser structure, which parses
        // interElementSpace before attempting the optional first element.
        from leadingWs in BasicParsers.OptionalWhitespaceOrLineContinuation(escapeChar)
        from execFormArgs in JsonArrayElements(escapeChar, canContainVariables, allowEmpty)
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
        bool canContainVariables, char escapeChar)
    {
        var materializedTokens = tokens.ToList();
        if (!materializedTokens.Any())
        {
            return new Token[]
            {
                new LiteralToken(Enumerable.Empty<Token>(), canContainVariables, escapeChar)
                {
                    QuoteChar = StringParsers.DoubleQuote
                }
            };
        }

        return StringParsers.CollapseLiteralTokens(materializedTokens, canContainVariables, escapeChar, StringParsers.DoubleQuote);
    }

    /// <summary>
    /// Parses the elements of a JSON array. When <paramref name="allowEmpty"/> is true,
    /// an empty array (no elements) is accepted via a lookahead-guarded fallback that
    /// checks for <c>]</c> before returning an empty result. The <c>.Or()</c> combinator
    /// (as opposed to <c>.XOr()</c>) is used so that even if the element parser partially
    /// consumes whitespace before failing, it backtracks cleanly to the empty-array path
    /// for inputs like <c>[ ]</c> or <c>[\n]</c>.
    /// When false, at least one element is required and no empty fallback is added,
    /// giving clearer error messages.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="canContainVariables">A value indicating whether the string can contain variables.</param>
    /// <param name="allowEmpty">A value indicating whether an empty array is allowed.</param>
    private static Parser<IEnumerable<Token>> JsonArrayElements(char escapeChar, bool canContainVariables, bool allowEmpty)
    {
        var elements =
            from firstArg in JsonArrayElement(escapeChar, canContainVariables, consumeLeadingWhitespace: false).Once().Flatten()
            from tail in (
                from delimiter in JsonArrayElementDelimiter(escapeChar)
                from nextArg in JsonArrayElement(escapeChar, canContainVariables)
                select TokenSequences.ConcatTokens(delimiter, nextArg)).Many()
            select TokenSequences.ConcatTokens(firstArg, tail.Flatten());

        if (allowEmpty)
        {
            // Use a lookahead for ']' so only truly empty arrays (e.g. [] or [ ]) take
            // the empty path. Without this, inputs like [foo] would silently fall through
            // to the empty branch (because the element parser fails without consuming
            // input) and report "expected ']'" instead of "expected opening quote".
            var emptyArrayLookahead =
                from preview in BasicParsers.Symbol(']').Preview()
                where preview.IsDefined
                select Enumerable.Empty<Token>();

            // Use .Or() (not .XOr()) so that the empty-array lookahead is attempted
            // even if the element parser partially consumed input before failing.
            // This ensures whitespace-only empty arrays like [ ] or [\n] backtrack
            // correctly to the empty-array path.
            elements = elements.Or(emptyArrayLookahead);
        }

        return elements;
    }

    /// <summary>
    /// Parses a JSON array element delimiter (i.e. comma) with optional whitespace.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    private static Parser<IEnumerable<Token>> JsonArrayElementDelimiter(char escapeChar) =>
        from leading in BasicParsers.OptionalWhitespaceOrLineContinuation(escapeChar)
        from comma in BasicParsers.Symbol(',').AsEnumerable().Optional()
        from trailing in BasicParsers.OptionalWhitespaceOrLineContinuation(escapeChar)
        select TokenSequences.ConcatTokens(
            leading,
            comma.GetOrDefault(),
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
    private static Parser<IEnumerable<Token>> JsonArrayElement(char escapeChar, bool canContainVariables, bool consumeLeadingWhitespace = true)
    {
        Parser<LiteralToken> literalParser = canContainVariables ?
            VariableParsers.LiteralWithVariables(escapeChar, new char[] { StringParsers.DoubleQuote }) :
            StringParsers.LiteralToken(escapeChar, new char[] { StringParsers.DoubleQuote });

        if (consumeLeadingWhitespace)
        {
            return
                from leading in BasicParsers.OptionalWhitespaceOrLineContinuation(escapeChar)
                from openingQuote in BasicParsers.Symbol(StringParsers.DoubleQuote)
                from argValue in InstructionParsers.ArgTokens(literalParser.AsEnumerable(), escapeChar).Many()
                from closingQuote in BasicParsers.Symbol(StringParsers.DoubleQuote)
                from trailing in BasicParsers.OptionalWhitespaceOrLineContinuation(escapeChar)
                select TokenSequences.ConcatTokens(
                    leading,
                    CreateJsonArrayElementLiteral(argValue.Flatten(), canContainVariables, escapeChar),
                    trailing);
        }

        return
            from openingQuote in BasicParsers.Symbol(StringParsers.DoubleQuote)
            from argValue in InstructionParsers.ArgTokens(literalParser.AsEnumerable(), escapeChar).Many()
            from closingQuote in BasicParsers.Symbol(StringParsers.DoubleQuote)
            from trailing in BasicParsers.OptionalWhitespaceOrLineContinuation(escapeChar)
            select TokenSequences.ConcatTokens(
                CreateJsonArrayElementLiteral(argValue.Flatten(), canContainVariables, escapeChar),
                trailing);
    }
}
