using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class VariableParsers
{
    /// <summary>
    /// Parses the characters of a variable reference.
    /// </summary>
    internal static Parser<char> VariableRefCharParser => P.Parse.LetterOrDigit.Or(P.Parse.Char('_'));

    /// <summary>
    /// Parses a variable identifier reference.
    /// </summary>
    /// <returns>Parser for a variable identifier.</returns>
    internal static Parser<string> VariableIdentifier() =>
        VariableRefCharParser.AtLeastOnce().Text();

    /// <summary>
    /// Parses an aggregate containing literals. This handles any variable references.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from parsing.</param>
    /// <param name="whitespaceMode">Where whitespace may occur within the literal.</param>
    /// <returns>A parsed aggregate token.</returns>
    internal static Parser<LiteralToken> LiteralWithVariables(
        char escapeChar, IEnumerable<char>? excludedChars = null, WhitespaceMode whitespaceMode = WhitespaceMode.Disallowed) =>
        from result in LiteralWithVariablesTokens(escapeChar, excludedChars, whitespaceMode)
        select new LiteralToken(result.Tokens, canContainVariables: true, escapeChar)
        {
            QuoteChar = result.QuoteChar
        };

    /// <summary>
    /// Parses a literal without treating a leading quote as a wrapper. This handles syntax where quotes are part of a larger raw value.
    /// </summary>
    internal static Parser<LiteralToken> UnquotedLiteralWithVariables(
        char escapeChar, IEnumerable<char>? excludedChars = null, WhitespaceMode whitespaceMode = WhitespaceMode.Disallowed) =>
        from tokens in UnquotedLiteralWithVariablesTokens(escapeChar, excludedChars, whitespaceMode)
        select new LiteralToken(tokens, canContainVariables: true, escapeChar);

    /// <summary>
    /// Parses an aggregate containing literals. This handles any variable references.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from parsing.</param>
    /// <param name="whitespaceMode">Where whitespace may occur within the literal.</param>
    /// <returns>A parsed aggregate token.</returns>
    internal static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> LiteralWithVariablesTokens(
        char escapeChar, IEnumerable<char>? excludedChars = null, WhitespaceMode whitespaceMode = WhitespaceMode.Disallowed)
    {
        if (excludedChars is null)
        {
            excludedChars = Enumerable.Empty<char>();
        }

        return StringParsers.WrappedInOptionalQuotes(
            (char escapeChar, IEnumerable<char> excludedChars, StringParsers.TokenWrapper tokenWrapper) =>
                from tokens in ValueOrVariableRef(
                    escapeChar,
                    (char escapeChar, IEnumerable<char> additionalExcludedChars) =>
                        StringParsers.WrappedInQuotesLiteralString(
                            escapeChar,
                            excludedChars.Union(additionalExcludedChars),
                            whitespaceMode == WhitespaceMode.AllowedInQuotes || whitespaceMode == WhitespaceMode.Allowed,
                            wrappingQuoteChar: tokenWrapper.OpeningString[0]),
                    excludedChars)
                    .Many()
                    .Flatten()
                select tokens,
            (char escapeChar, IEnumerable<char> excludedChars) =>
                UnquotedLiteralWithVariablesTokens(escapeChar, excludedChars, whitespaceMode),
            escapeChar,
            excludedChars);
    }

    private static Parser<IEnumerable<Token>> UnquotedLiteralWithVariablesTokens(
        char escapeChar, IEnumerable<char>? excludedChars = null, WhitespaceMode whitespaceMode = WhitespaceMode.Disallowed)
    {
        if (excludedChars is null)
        {
            excludedChars = Enumerable.Empty<char>();
        }

        return
            from tokens in ValueOrVariableRef(
                escapeChar,
                (char escapeChar, IEnumerable<char> additionalExcludedChars) =>
                    whitespaceMode == WhitespaceMode.Allowed ?
                        StringParsers.LiteralString(escapeChar, excludedChars.Union(additionalExcludedChars)).Or(BasicParsers.Whitespace().Or(BasicParsers.LineContinuations(escapeChar))).Many().Flatten() :
                        StringParsers.LiteralString(escapeChar, excludedChars.Union(additionalExcludedChars)),
                excludedChars)
                .Many()
                .Flatten()
            where tokens.Any()
            select TokenHelper.CollapseStringTokens(tokens);
    }

    /// <summary>
    /// Parses a token for either a value or a variable reference.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="createParser">A delegate to create the token parser.</param>
    /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
    /// <returns>A token parser.</returns>
    internal static Parser<IEnumerable<Token>> ValueOrVariableRef(char escapeChar, CreateTokenParserDelegate createParser,
        IEnumerable<char> excludedChars) =>
        VariableRefToken.GetParser(escapeChar).AsEnumerable()
            .Or(createParser(escapeChar, excludedChars));
}
