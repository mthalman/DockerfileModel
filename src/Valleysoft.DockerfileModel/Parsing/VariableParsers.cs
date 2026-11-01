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
    /// Parses a literal with variable references, joining adjacent quoted and unquoted word fragments.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from parsing.</param>
    /// <param name="whitespaceMode">Where whitespace may occur within the literal.</param>
    /// <param name="allowUnmatchedQuoteFragments">Retain unmatched trailing fragments even after a leading quote wrapper.</param>
    /// <returns>A parsed aggregate token.</returns>
    internal static TextParser<LiteralToken> LiteralWithVariables(
        char escapeChar, IEnumerable<char>? excludedChars = null, WhitespaceMode whitespaceMode = WhitespaceMode.Disallowed,
        bool allowUnmatchedQuoteFragments = false)
    {
        char[] excluded = excludedChars?.ToArray() ?? Array.Empty<char>();
        TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)> parser =
            LiteralWithVariablesTokens(escapeChar, excluded, whitespaceMode, allowUnmatchedQuoteFragments);
        TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)>? wordParser =
            whitespaceMode != WhitespaceMode.Allowed && !excluded.Contains(StringParsers.DoubleQuote)
                ? parser.AtEnd() : null;
        return from result in parser
               select new LiteralToken(result.Tokens, canContainVariables: true, escapeChar, wordParser: wordParser)
               {
                   QuoteChar = result.QuoteChar
               };
    }

    /// <summary>
    /// Parses a literal without treating a leading quote as a wrapper. This handles syntax where quotes are part of a larger raw value.
    /// </summary>
    internal static TextParser<LiteralToken> UnquotedLiteralWithVariables(
        char escapeChar, IEnumerable<char>? excludedChars = null, WhitespaceMode whitespaceMode = WhitespaceMode.Disallowed) =>
        from tokens in UnquotedLiteralWithVariablesTokens(escapeChar, excludedChars, whitespaceMode)
        select new LiteralToken(tokens, canContainVariables: true, escapeChar);

    /// <summary>
    /// Parses literal contents and quote context. Word fragments retain their own quote wrappers;
    /// freeform values and JSON contents retain their existing single-fragment grammar.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from parsing.</param>
    /// <param name="whitespaceMode">Where whitespace may occur within the literal.</param>
    /// <param name="allowUnmatchedQuoteFragments">Retain unmatched trailing fragments even after a leading quote wrapper.</param>
    /// <returns>A parsed aggregate token.</returns>
    internal static TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)> LiteralWithVariablesTokens(
        char escapeChar, IEnumerable<char>? excludedChars = null, WhitespaceMode whitespaceMode = WhitespaceMode.Disallowed,
        bool allowUnmatchedQuoteFragments = false)
    {
        char[] excluded = excludedChars?.ToArray() ?? Array.Empty<char>();
        if (excluded.Contains(StringParsers.DoubleQuote) || whitespaceMode == WhitespaceMode.Allowed)
        {
            return LiteralWithVariablesFragment(escapeChar, excluded, whitespaceMode);
        }

        var parser = new Lazy<TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)>>(
            () => WordFragments(escapeChar, excluded, whitespaceMode, allowUnmatchedQuoteFragments));
        return input => parser.Value(input);
    }

    private static TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)> WordFragments(
        char escapeChar, char[] excluded, WhitespaceMode whitespaceMode, bool allowUnmatchedQuoteFragments)
    {
        TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)> fragment =
            LiteralWithVariablesFragment(escapeChar, excluded, whitespaceMode, separateQuotes: true);
        // Unquoted words historically include raw quotes; preserve them when a later quoted fragment is unmatched.
        TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)> continuation =
            fragment.Try().Or(
                from tokens in UnquotedLiteralWithVariablesTokens(escapeChar, excluded, whitespaceMode)
                select (tokens, (char?)null));
        return
            from first in fragment.Try()
            from rest in (allowUnmatchedQuoteFragments || first.QuoteChar is null ? continuation : fragment).Try().Many()
            select CombineFragments(new[] { first }.Concat(rest).ToArray(), escapeChar);
    }

    private static (IEnumerable<Token> Tokens, char? QuoteChar) CombineFragments(
        (IEnumerable<Token> Tokens, char? QuoteChar)[] fragments, char escapeChar)
    {
        if (fragments.Length == 1)
        {
            return fragments[0];
        }

        return (TokenHelper.CollapseStringTokens(fragments.SelectMany(fragment =>
            fragment.QuoteChar is null
                ? fragment.Tokens
                : new Token[]
                {
                    new LiteralToken(fragment.Tokens, canContainVariables: true, escapeChar)
                    {
                        QuoteChar = fragment.QuoteChar
                    }
                })), null);
    }

    private static TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)> LiteralWithVariablesFragment(
        char escapeChar, IEnumerable<char>? excludedChars, WhitespaceMode whitespaceMode, bool separateQuotes = false)
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
                UnquotedLiteralWithVariablesTokens(escapeChar,
                    separateQuotes ? excludedChars.Concat(new[] { '\'', '"' }) : excludedChars, whitespaceMode),
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
