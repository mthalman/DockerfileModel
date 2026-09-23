using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class VariableParsers
{
    /// <summary>
    /// Parses the characters of a variable reference.
    /// </summary>
    internal static TextParser<char> VariableRefCharParser => Character.LetterOrDigit.Try().Or(Character.EqualTo('_'));

    /// <summary>
    /// Parses a variable identifier reference.
    /// </summary>
    /// <returns>Parser for a variable identifier.</returns>
    internal static TextParser<string> VariableIdentifier() =>
        VariableRefCharParser.Try().AtLeastOnce().Text();

    /// <summary>
    /// Parses an aggregate containing literals. This handles any variable references.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from parsing.</param>
    /// <param name="whitespaceMode">Where whitespace may occur within the literal.</param>
    /// <returns>A parsed aggregate token.</returns>
    internal static TextParser<LiteralToken> LiteralWithVariables(
        char escapeChar, IEnumerable<char>? excludedChars = null, WhitespaceMode whitespaceMode = WhitespaceMode.Disallowed) =>
        from result in LiteralWithVariablesTokens(escapeChar, excludedChars, whitespaceMode)
        select new LiteralToken(result.Tokens, canContainVariables: true, escapeChar)
        {
            QuoteChar = result.QuoteChar
        };

    /// <summary>
    /// Parses a literal without treating a leading quote as a wrapper. This handles syntax where quotes are part of a larger raw value.
    /// </summary>
    internal static TextParser<LiteralToken> UnquotedLiteralWithVariables(
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
    internal static TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)> LiteralWithVariablesTokens(
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
                    .Try().Many()
                    .Flatten()
                select tokens,
            (char escapeChar, IEnumerable<char> excludedChars) =>
                UnquotedLiteralWithVariablesTokens(escapeChar, excludedChars, whitespaceMode),
            escapeChar,
            excludedChars);
    }

    private static TextParser<IEnumerable<Token>> UnquotedLiteralWithVariablesTokens(
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
                        StringParsers.LiteralString(escapeChar, excludedChars.Union(additionalExcludedChars))
                            .Try()
                            .Or(BasicParsers.Whitespace().Where(tokens => tokens.Any())
                                .Try()
                                .Or(BasicParsers.LineContinuations(escapeChar).Where(tokens => tokens.Any()))) :
                        StringParsers.LiteralString(escapeChar, excludedChars.Union(additionalExcludedChars)),
                excludedChars)
                .Try().Many()
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
    internal static TextParser<IEnumerable<Token>> ValueOrVariableRef(char escapeChar, CreateTokenParserDelegate createParser,
        IEnumerable<char> excludedChars) =>
        VariableRefToken.GetParser(escapeChar)
            .Cast<VariableRefToken, Token>()
            .AsEnumerable()
            .Try().Or(createParser(escapeChar, excludedChars));
}
