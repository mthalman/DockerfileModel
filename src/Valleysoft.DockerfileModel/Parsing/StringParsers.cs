using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class StringParsers
{
    private const char SingleQuote = '\'';
    internal const char DoubleQuote = '\"';
    private static readonly char[] Quotes = new char[] { SingleQuote, DoubleQuote };
    /// <summary>
    /// Parses identifiers, delimited by a character.
    /// </summary>
    /// <param name="escapeChar">The escape character used for line continuations.</param>
    /// <param name="firstCharParser">Parser for the first character of the identifier.</param>
    /// <param name="tailCharParser">Parser for the rest of the characters of the identifier.</param>
    /// <param name="delimiter">Character which delimits segments of the string.</param>
    /// <param name="minimumDelimiters">Minimum number of delimiter characters that must exist in the string.</param>
    /// <returns>Delimited identifiers.</returns>
    internal static Parser<IEnumerable<Token>> DelimitedIdentifier(char escapeChar,
        Parser<char> firstCharParser, Parser<char> tailCharParser, char delimiter, int minimumDelimiters = 0) =>
        from segments in IdentifierString(escapeChar, firstCharParser, tailCharParser).Many().DelimitedBy(P.Parse.Char(delimiter))
        where (segments.Count() > minimumDelimiters)
        select
            segments
                .Flatten()
                .Aggregate((tokens1, tokens2) =>
                    TokenHelper.CollapseStringTokens(TokenSequences.ConcatTokens(tokens1, new Token[] { new StringToken(delimiter.ToString()) }, tokens2)));

    /// <summary>
    /// Parses a string.
    /// </summary>
    /// <param name="value">Value of the string.</param>
    /// <param name="escapeChar">Escape character.</param>
    internal static Parser<IEnumerable<Token>> StringToken(string value, char escapeChar)
    {
        Parser<IEnumerable<Token>>? parser = null;
        for (int i = 0; i < value.Length; i++)
        {
            int currentIndex = i;
            if (parser is null)
            {
                parser = BasicParsers.ToStringTokens(P.Parse.IgnoreCase(value[currentIndex]));
            }
            else
            {
                parser = from previousTokens in parser
                            from nextTokens in StringTokenCharWithOptionalLineContinuation(escapeChar, P.Parse.IgnoreCase(value[currentIndex]))
                            select TokenSequences.ConcatTokens(previousTokens, nextTokens);
            }
        }

        return from tokens in parser
                select TokenHelper.CollapseStringTokens(tokens);
    }

    /// <summary>
    /// Parses a single character preceded by an optional line continuation.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="charParser">Character parser.</param>
    /// <returns>Parsed tokens.</returns>
    internal static Parser<IEnumerable<Token>> StringTokenCharWithOptionalLineContinuation(char escapeChar, Parser<char> charParser) =>
        BasicParsers.CharWithOptionalLineContinuation(escapeChar, charParser, ch => new StringToken(ch.ToString()));

    /// <summary>
    /// Parses the tokens within an identifier.
    /// </summary>
    /// <param name="firstCharacterParser">Parser of the first character of the identifier.</param>
    /// <param name="tailCharacterParser">Parser of the rest of the characters of the identifier.</param>
    /// <param name="escapeChar">Escape character.</param>
    internal static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> IdentifierTokens(Parser<char> firstCharacterParser, Parser<char> tailCharacterParser, char escapeChar) =>
        WrappedInOptionalQuotes(
            (char escapeChar, IEnumerable<char> excludedChars, TokenWrapper tokenWrapper) =>
                WrappedInQuotesIdentifier(escapeChar, firstCharacterParser, tailCharacterParser,
                    wrappingQuoteChar: tokenWrapper.OpeningString[0]),
            (char escapeChar, IEnumerable<char> excludedChars) =>
                IdentifierString(escapeChar, firstCharacterParser, tailCharacterParser),
            escapeChar,
            Enumerable.Empty<char>());

    /// <summary>
    /// Parses the tokens within a LABEL key. Unquoted keys use identifier character
    /// restrictions; quoted keys allow characters like whitespace and special characters
    /// (e.g. apostrophes) that aren't valid in unquoted keys, but still exclude variable
    /// reference characters (<c>$</c>), the key-value separator (<c>=</c>), and treat
    /// the escape character specially.
    /// </summary>
    /// <param name="firstCharacterParser">Parser of the first character of an unquoted identifier.</param>
    /// <param name="tailCharacterParser">Parser of the rest of the characters of an unquoted identifier.</param>
    /// <param name="escapeChar">Escape character.</param>
    internal static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> LabelKeyTokens(Parser<char> firstCharacterParser, Parser<char> tailCharacterParser, char escapeChar) =>
        WrappedInOptionalQuotes(
            (char escapeChar, IEnumerable<char> excludedChars, TokenWrapper tokenWrapper) =>
                WrappedInQuotesLiteralString(escapeChar, excludedChars, isWhitespaceAllowed: true,
                    excludeVariableRefChars: true, wrappingQuoteChar: tokenWrapper.OpeningString[0]),
            (char escapeChar, IEnumerable<char> excludedChars) =>
                IdentifierString(escapeChar, firstCharacterParser, tailCharacterParser),
            escapeChar,
            new[] { '=' });

    /// <summary>
    /// Parses a literal string that is not wrapped in quotes.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsing.</param>
    /// <param name="excludeVariableRefChars">A value indicating whether to exclude the variable ref characters.</param>
    /// <returns>Parser for a literal string that is not wrapped in quotes.</returns>
    internal static Parser<IEnumerable<Token>> LiteralString(char escapeChar, IEnumerable<char> excludedChars, bool excludeVariableRefChars = true) =>
        BasicParsers.OrConcat(
            LiteralStringWithoutSpaces(escapeChar, excludedChars, excludeVariableRefChars),
            EscapedChar(escapeChar));

    /// <summary>
    /// Parses a literal string, including spaces, that is optionally wrapped in quotes.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludeVariableRefChars">A value indicating whether to exclude the variable ref characters.</param>
    internal static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> WrappedInOptionalQuotesLiteralStringWithSpaces(
        char escapeChar, bool excludeVariableRefChars = true) =>
        from tokenSets in WrappedInOptionalQuotesLiteralStringWithSpacesCore(escapeChar, excludeVariableRefChars).AtLeastOnce()
        select CollapseOptionalQuotesLiteralStringTokenSets(tokenSets);

    /// <summary>
    /// Parses a literal string, including spaces, that is optionally wrapped in quotes.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludeVariableRefChars">A value indicating whether to exclude the variable ref characters.</param>
    private static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> WrappedInOptionalQuotesLiteralStringWithSpacesCore(
        char escapeChar, bool excludeVariableRefChars) =>
        WrappedInOptionalQuotes(
            (char escapeChar, IEnumerable<char> excludedChars, TokenWrapper tokenWrapper) =>
                WrappedInQuotesLiteralString(escapeChar, excludedChars, isWhitespaceAllowed: true,
                    excludeVariableRefChars: excludeVariableRefChars, wrappingQuoteChar: tokenWrapper.OpeningString[0]),
            (char escapeChar, IEnumerable<char> excludedChars) =>
                from tokens in LiteralString(escapeChar, excludedChars, excludeVariableRefChars: excludeVariableRefChars)
                    .Or(BasicParsers.Whitespace()).Many().Flatten()
                select TokenHelper.CollapseTokens(
                    ExtractLiteralTokenContents(tokens),
                    token => token is StringToken || token.GetType() == typeof(WhitespaceToken),
                    val => new StringToken(val)),
            escapeChar,
            Enumerable.Empty<char>());

    private static (IEnumerable<Token> Tokens, char? QuoteChar) CollapseOptionalQuotesLiteralStringTokenSets(
        IEnumerable<(IEnumerable<Token> Tokens, char? QuoteChar)> tokenSets)
    {
        if (!tokenSets.Skip(1).Any())
        {
            return tokenSets.First();
        }

        IEnumerable<Token> tokens = tokenSets
            .SelectMany(tokenSet => TokenSequences.ConcatTokens(
                new Token?[] { tokenSet.QuoteChar is null ? null : new StringToken(((char)tokenSet.QuoteChar).ToString()) },
                tokenSet.Tokens,
                new Token?[] { tokenSet.QuoteChar is null ? null : new StringToken(((char)tokenSet.QuoteChar).ToString()) }));
        return (TokenHelper.CollapseStringTokens(tokens), null);
    }

    /// <summary>
    /// Parses a literal token.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
    internal static Parser<LiteralToken> LiteralToken(char escapeChar, IEnumerable<char> excludedChars) =>
        from literal in LiteralString(escapeChar, excludedChars, excludeVariableRefChars: false).Many().Flatten()
        where literal.Any()
        select new LiteralToken(TokenHelper.CollapseStringTokens(literal), canContainVariables: false, escapeChar);

    /// <summary>
    /// Excludes parsing of the specified characters from a parser.
    /// </summary>
    /// <param name="parser">The character parser to apply the exclusion to.</param>
    /// <param name="chars">Set of characters to be excluded from parsing.</param>
    /// <returns>Character parser that excludes the specified characters.</returns>
    private static Parser<char> ExceptChars(this Parser<char> parser, IEnumerable<char> chars) =>
        chars
            .Select(ch => P.Parse.Char(ch))
            .Aggregate(parser, (current, next) => current.Except(next));

    /// <summary>
    /// Collapses any sequential string or whitespace tokens and wraps them in a literal token.
    /// </summary>
    /// <param name="tokens">Set of tokens to process.</param>
    /// <param name="canContainVariables">Whether later literal value updates recognize variable references.</param>
    /// <param name="escapeChar">The escape context retained by the resulting literal.</param>
    /// <param name="quoteChar">The quote character associated with the literal.</param>
    internal static IEnumerable<Token> CollapseLiteralTokens(IEnumerable<Token> tokens,
        bool canContainVariables, char escapeChar, char? quoteChar = null)
    {
        Guard.NotNullEmptyOrNullElements(tokens, nameof(tokens));
        return new Token[]
        {
            new LiteralToken(
                TokenHelper.CollapseTokens(ExtractLiteralTokenContents(tokens),
                    token => token is StringToken || token.GetType() == typeof(WhitespaceToken),
                    val => new StringToken(val)),
                canContainVariables,
                escapeChar)
            {
                QuoteChar = quoteChar
            }
        };
    }

    /// <summary>
    /// Enumerates the tokens while extracting the contents of any literal tokens encountered.
    /// </summary>
    /// <param name="tokens">Tokens to enumerate.</param>
    private static IEnumerable<Token> ExtractLiteralTokenContents(IEnumerable<Token> tokens)
    {
        foreach (Token token in tokens)
        {
            if (token is LiteralToken literal)
            {
                foreach (Token literalItem in literal.Tokens)
                {
                    yield return literalItem;
                }
            }
            else
            {
                yield return token;
            }
        }
    }

    /// <summary>
    /// Parses an identifier string wrapped in quotes.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="firstCharacterParser">Parser of the first character of the identifier.</param>
    /// <param name="tailCharacterParser">Parser of the rest of the characters of the identifier.</param>
    /// <param name="wrappingQuoteChar">The quote character wrapping this identifier. Only this quote is excluded from content.</param>
    /// <returns>Parser for an identifier string wrapped in quotes.</returns>
    private static Parser<IEnumerable<Token>> WrappedInQuotesIdentifier(char escapeChar, Parser<char> firstCharacterParser,
        Parser<char> tailCharacterParser, char? wrappingQuoteChar = null) =>
        IdentifierString(
            escapeChar,
            ExceptQuote(firstCharacterParser, wrappingQuoteChar),
            ExceptQuote(tailCharacterParser, wrappingQuoteChar));

    /// <summary>
    /// Parses an identifier string.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="firstCharacterParser">Parser of the first character of the identifier.</param>
    /// <param name="tailCharacterParser">Parser of the rest of the characters of the identifier.</param>
    /// <returns>Parser for an identifier string.</returns>
    internal static Parser<IEnumerable<Token>> IdentifierString(char escapeChar, Parser<char> firstCharacterParser,
        Parser<char> tailCharacterParser) =>
        from first in BasicParsers.ToStringTokens(firstCharacterParser)
        from rest in BasicParsers.OrConcat(
            StringTokenCharWithOptionalLineContinuation(escapeChar, tailCharacterParser),
            EscapedChar(escapeChar))
            .Many()
            .Flatten()
        select TokenHelper.CollapseStringTokens(TokenSequences.ConcatTokens(first, rest));

    /// <summary>
    /// Parses a literal string wrapped in quotes.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsing.</param>
    /// <param name="isWhitespaceAllowed">A value indicating whether whitespace is allowed in the string.</param>
    /// <param name="excludeVariableRefChars">A value indicating whether to exclude the variable ref characters.</param>
    /// <param name="wrappingQuoteChar">The quote character wrapping this string. Only this quote is excluded from content.</param>
    /// <returns>Parser for a literal string wrapped in quotes.</returns>
    internal static Parser<IEnumerable<Token>> WrappedInQuotesLiteralString(char escapeChar, IEnumerable<char> excludedChars,
        bool isWhitespaceAllowed = false, bool excludeVariableRefChars = true, char? wrappingQuoteChar = null)
    {
        Parser<char> parser = ExceptQuote(LiteralChar(escapeChar, excludedChars, isWhitespaceAllowed, excludeVariableRefChars), wrappingQuoteChar);
        return
            from first in BasicParsers.ToStringTokens(parser).Or(EscapedChar(escapeChar))
            from rest in BasicParsers.OrConcat(
                StringTokenCharWithOptionalLineContinuation(escapeChar, parser)
                    .Many()
                    .Flatten(),
                EscapedChar(escapeChar))
            select TokenHelper.CollapseStringTokens(TokenSequences.ConcatTokens(first, rest));
    }

    /// <summary>
    /// Parses a literal string that does not contain any spaces.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsing.</param>
    /// <param name="excludeVariableRefChars">A value indicating whether to exclude the variable ref characters.</param>
    /// <returns>Parser for a literal string that does not contain any spaces.</returns>
    private static Parser<IEnumerable<Token>> LiteralStringWithoutSpaces(char escapeChar, IEnumerable<char> excludedChars,
        bool excludeVariableRefChars = true)
    {
        Parser<char> parser = LiteralChar(escapeChar, excludedChars, excludeVariableRefChars: excludeVariableRefChars);
        return
            from first in BasicParsers.ToStringTokens(parser).Or(EscapedChar(escapeChar))
            from rest in StringTokenCharWithOptionalLineContinuation(escapeChar, parser)
                .Many()
                .Flatten()
            select TokenHelper.CollapseStringTokens(TokenSequences.ConcatTokens(first, rest));
    }

    /// <summary>
    /// Parses a literal string that allows any non-newline characters (including spaces and tabs),
    /// stopping only at excluded characters.
    /// Used for variable modifier values inside braces (e.g., "must set" in ${VAR:?must set}).
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsing.</param>
    /// <param name="excludeVariableRefChars">A value indicating whether to exclude the variable ref characters.</param>
    /// <returns>Parser for a literal string that allows any non-newline characters.</returns>
    internal static Parser<IEnumerable<Token>> LiteralStringAllowingSpaces(char escapeChar, IEnumerable<char> excludedChars,
        bool excludeVariableRefChars = true)
    {
        // Allow any non-newline character except excluded chars and the escape char itself.
        Parser<char> parser = P.Parse.AnyChar
            .Except(P.Parse.LineTerminator)
            .ExceptChars(excludedChars)
            .Except(P.Parse.Char(escapeChar));

        if (excludeVariableRefChars)
        {
            parser = parser.Except(VariableRefChars());
        }

        // Mirror LiteralStringWithoutSpaces: use StringTokenCharWithOptionalLineContinuation
        // for subsequent characters so that escaped characters and line continuations are
        // handled consistently throughout the entire value, not just at the first character.
        Parser<IEnumerable<Token>> charParser =
            StringTokenCharWithOptionalLineContinuation(escapeChar, parser)
                .Or(EscapedChar(escapeChar));

        return
            from first in BasicParsers.ToStringTokens(parser).Or(EscapedChar(escapeChar))
            from rest in charParser
                .Many()
                .Flatten()
            select TokenHelper.CollapseStringTokens(TokenSequences.ConcatTokens(first, rest));
    }

    /// <summary>
    /// Parses a literal character.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
    /// <param name="isWhitespaceAllowed">A value indicating whether whitespace is allowed.</param>
    /// <param name="excludeVariableRefChars">A value indicating whether to exclude the variable ref characters.</param>
    private static Parser<char> LiteralChar(char escapeChar, IEnumerable<char> excludedChars,
        bool isWhitespaceAllowed = false, bool excludeVariableRefChars = true)
    {
        Parser<char> parser = (isWhitespaceAllowed ? P.Parse.AnyChar : BasicParsers.NonWhitespace())
            .ExceptChars(excludedChars)
            .Except(P.Parse.Char(escapeChar));

        if (excludeVariableRefChars)
        {
            parser = parser.Except(VariableRefChars());
        }

        return parser;
    }

    /// <summary>
    /// Parses variable ref characters.
    /// </summary>
    private static Parser<char> VariableRefChars() =>
        P.Parse.Char('$').Then(ch => P.Parse.LetterOrDigit.Or(P.Parse.Char('{')).Or(P.Parse.Char('_')));

    /// <summary>
    /// Parses an escaped character.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    private static Parser<IEnumerable<Token>> EscapedChar(char escapeChar) =>
        from esc in P.Parse.Char(escapeChar)
        from v in P.Parse.AnyChar.AsEnumerable()
            .Except(P.Parse.LineEnd)
            .Text()
        select new Token[] { new StringToken(esc + v) };

    /// <summary>
    /// Parses a token that is optionally wrapped in quotes.
    /// </summary>
    /// <param name="createWrappedParser">A delegate to create a token parser for a wrapped value.</param>
    /// <param name="nonWrappedParser">A delegate to create a token parser for a value that isn't wrapped.</param>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
    internal static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> WrappedInOptionalQuotes(CreateWrappedTokenParserDelegate createWrappedParser,
        CreateTokenParserDelegate nonWrappedParser, char escapeChar, IEnumerable<char> excludedChars) =>
        from result in WrappedInOptionalCharacters(
            createWrappedParser,
            nonWrappedParser,
            escapeChar,
            excludedChars,
            new TokenWrapper(SingleQuote.ToString(), SingleQuote.ToString()),
            new TokenWrapper(DoubleQuote.ToString(), DoubleQuote.ToString()))
        select (result.Tokens, result.TokenWrapper?.OpeningString[0]);

    /// <summary>
    /// Parses a token that is optionally wrapped in a set of characters.
    /// </summary>
    /// <param name="createWrappedParser">A delegate to create a token parser for a wrapped value.</param>
    /// <param name="createNonWrappedParser">A delegate to create a token parser for a value that isn't wrapped.</param>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
    /// <param name="tokenWrappers">Set of token wrappers describing the characters that can optionally wrap the value.</param>
    /// <returns></returns>
    private static Parser<(IEnumerable<Token> Tokens, TokenWrapper? TokenWrapper)> WrappedInOptionalCharacters(CreateWrappedTokenParserDelegate createWrappedParser,
        CreateTokenParserDelegate createNonWrappedParser, char escapeChar, IEnumerable<char> excludedChars,
        params TokenWrapper[] tokenWrappers) =>
            tokenWrappers
                .Select(tokenWrapper =>
                    from tokens in WrappedInCharacters(
                        createWrappedParser,
                        escapeChar,
                        tokenWrapper,
                        excludedChars)
                    select (tokens, tokenWrapper))
                .Aggregate((current, next) => current.Or(next))
                .Select(result => (result.tokens, (TokenWrapper?)result.tokenWrapper))
            .XOr(
                from tokens in createNonWrappedParser(escapeChar, excludedChars)
                select (tokens, (TokenWrapper?)null));

    /// <summary>
    /// Parses a character that excludes quotes.
    /// </summary>
    /// <param name="parser">A character parser to exclude quotes from.</param>
    private static Parser<char> ExceptQuotes(Parser<char> parser) =>
        parser.ExceptChars(Quotes);

    /// <summary>
    /// Parses a character that excludes only the specified wrapping quote character.
    /// If no wrapping quote is specified, falls back to excluding both quote types.
    /// </summary>
    /// <param name="parser">A character parser to exclude the quote from.</param>
    /// <param name="wrappingQuoteChar">The wrapping quote character to exclude, or null to exclude both.</param>
    private static Parser<char> ExceptQuote(Parser<char> parser, char? wrappingQuoteChar) =>
        wrappingQuoteChar.HasValue
            ? parser.Except(P.Parse.Char(wrappingQuoteChar.Value))
            : ExceptQuotes(parser);

    /// <summary>
    /// Parses a token that is wrapped by a set of characters.
    /// </summary>
    /// <param name="createParser">A delegate that creates a token parser.</param>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="tokenWrapper">A token wrapper describing the set of characters wrapping the value.</param>
    /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
    private static Parser<IEnumerable<Token>> WrappedInCharacters(CreateWrappedTokenParserDelegate createParser,
        char escapeChar, TokenWrapper tokenWrapper, IEnumerable<char> excludedChars) =>
        from opening in P.Parse.String(tokenWrapper.OpeningString).AsEnumerable()
        from val in createParser(escapeChar, excludedChars, tokenWrapper)
        from closing in P.Parse.String(tokenWrapper.ClosingString).AsEnumerable()
        select val;

    /// <summary>
    /// Delegate for creating a parser of a token that is wrapped by a set of characters.
    /// </summary>
    /// <param name="escapeChar">The escape character.</param>
    /// <param name="excludedChars">Characters to be excluded from parsing.</param>
    /// <param name="tokenWrapper">Description of characters are wrapping the token.</param>
    /// <returns>The token parser.</returns>
    internal delegate Parser<IEnumerable<Token>> CreateWrappedTokenParserDelegate(
        char escapeChar,
        IEnumerable<char> excludedChars,
        TokenWrapper tokenWrapper);

    /// <summary>
    /// Delegate for creating a parser of a primitive string.
    /// </summary>
    ///  <param name="escapeChar">The escape character.</param>
    /// <param name="excludedChars">Characters to be excluded from parsing.</param>
    private delegate Parser<IEnumerable<Token>> CreateValueParserDelegate(char escapeChar, IEnumerable<char> excludedChars);

    /// <summary>
    /// Describes the opening and closing strings that wrap a token.
    /// </summary>
    internal class TokenWrapper
    {
        public TokenWrapper(string openingString, string closingString)
        {
            OpeningString = openingString;
            ClosingString = closingString;
        }

        public string OpeningString { get; }
        public string ClosingString { get; }
    }
}
