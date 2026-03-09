using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

internal static class ParseHelper
{
    private const char SingleQuote = '\'';
    public const char DoubleQuote = '\"';
    private static readonly char[] Quotes = new char[] { SingleQuote, DoubleQuote };

    /// <summary>
    /// Filters out null items from an enumerable.
    /// </summary>
    /// <typeparam name="T">Type of items contained in the enumerable.</typeparam>
    /// <param name="items">The enumerable to filter nulls from.</param>
    /// <returns>An enumerable with no null items.</returns>
    public static IEnumerable<T> FilterNulls<T>(IEnumerable<T?>? items) where T : class
    {
        if (items is null)
        {
            yield break;
        }

        foreach (T? item in items.Where(item => item != null))
        {
            yield return item!;
        }
    }

    /// <summary>
    /// Parses whitespace.
    /// </summary>
    /// <returns>Set of tokens representing whitespace.</returns>
    public static Parser<IEnumerable<Token>> Whitespace() =>
        from whitespace in WhitespaceWithoutNewLine()
        from newLine in OptionalNewLine()
        select ConcatTokens(whitespace, newLine);

    /// <summary>
    /// Parses the text of a comment, including leading whitespace.
    /// </summary>
    /// <returns>Set of tokens representing comment text.</returns>
    public static Parser<IEnumerable<Token>> CommentText() =>
        from leading in Whitespace()
        from comment in CommentToken.GetParser()
        from lineEnd in OptionalNewLine().AsEnumerable()
        select ConcatTokens(leading, new Token[] { new CommentToken(ConcatTokens(comment, lineEnd)) });

    /// <summary>
    /// Optionally parses a line continuation surrounded by optional whitespace.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    public static Parser<IEnumerable<Token>> OptionalWhitespaceOrLineContinuation(char escapeChar) =>
        from leading in Whitespace().Optional()
        from lineContinuation in LineContinuations(escapeChar).Optional()
        from trailing in Whitespace().Optional()
        select ConcatTokens(
            leading.GetOrDefault(),
            lineContinuation.IsDefined ? lineContinuation.GetOrDefault() : Enumerable.Empty<Token>(),
            trailing.GetOrDefault());

    /// <summary>
    /// Concatenates sets of tokens into a single set, removing any nulls.
    /// </summary>
    /// <param name="tokens">Sets of tokens.</param>
    /// <returns>Concatenation of all tokens.</returns>
    public static IEnumerable<Token> ConcatTokens(params Token?[]? tokens) =>
        FilterNulls(tokens).ToList();

    /// <summary>
    /// Concatenates sets of tokens into a single set, removing any nulls.
    /// </summary>
    /// <param name="tokens">Sets of tokens.</param>
    /// <returns>Concatenation of all tokens.</returns>
    public static IEnumerable<Token> ConcatTokens(params IEnumerable<Token?>[] tokens) =>
        ConcatTokens(
            FilterNulls(tokens)
                .SelectMany(tokens => tokens)
                .ToArray());

    /// <summary>
    /// Parses identifiers, delimited by a character.
    /// </summary>
    /// <param name="firstCharParser">Parser for the first character of the identifier.</param>
    /// <param name="tailCharParser">Parser for the rest of the characters of the identifier.</param>
    /// <param name="delimiter">Character which delimits segments of the string.</param>
    /// <param name="minimumDelimiters">Minimum number of delimiter characters that must exist in the string.</param>
    /// <returns>Delimited identifiers.</returns>
    public static Parser<IEnumerable<Token>> DelimitedIdentifier(char escapeChar,
        Parser<char> firstCharParser, Parser<char> tailCharParser, char delimiter, int minimumDelimiters = 0) =>
        from segments in IdentifierString(escapeChar, firstCharParser, tailCharParser).Many().DelimitedBy(Parse.Char(delimiter))
        where (segments.Count() > minimumDelimiters)
        select
            segments
                .Flatten()
                .Aggregate((tokens1, tokens2) =>
                    TokenHelper.CollapseStringTokens(ConcatTokens(tokens1, new Token[] { new StringToken(delimiter.ToString()) }, tokens2)));

    /// <summary>
    /// Parses a new line that is optional.
    /// </summary>
    /// <returns>The new line token if a new line exists; otherwise, null.</returns>
    public static Parser<NewLineToken> OptionalNewLine() =>
        from lineEnd in Parse.LineEnd.Optional()
        select lineEnd.IsDefined ? new NewLineToken(lineEnd.Get()) : null;

    /// <summary>
    /// Parses a token and any trailing whitespace.
    /// </summary>
    /// <param name="parser">Token parser.</param>
    /// <returns>Set of parsed tokens.</returns>
    public static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Parser<Token> parser) =>
        from token in parser.AsEnumerable()
        from trailingWhitespace in Whitespace()
        select ConcatTokens(token, trailingWhitespace);

    /// <summary>
    /// Parses a token and any trailing whitespace.
    /// </summary>
    /// <param name="createToken">Delegate to create the token.</param>
    /// <returns>Set of parsed tokens.</returns>
    public static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Func<string, Token> createToken) =>
        from val in Parse.AnyChar.Except(Parse.LineEnd).Many().Text()
        select ConcatTokens(createToken(val.Trim()), GetTrailingWhitespaceToken(val)!);

    /// <summary>
    /// Returns a whitespace token for any trailing whitespace in the given string.
    /// </summary>
    /// <param name="text">String to parse.</param>
    public static WhitespaceToken? GetTrailingWhitespaceToken(string text)
    {
        string? whitespace = new(
            text
                .Reverse()
                .TakeWhile(ch => Char.IsWhiteSpace(ch))
                .Reverse()
                .ToArray());

        if (whitespace == String.Empty)
        {
            return null;
        }

        return new WhitespaceToken(whitespace);
    }

    /// <summary>
    /// Returns a whitespace token for any leading whitespace in the given string.
    /// </summary>
    /// <param name="text">String to parse.</param>
    public static WhitespaceToken? GetLeadingWhitespaceToken(string text)
    {
        string? whitespace = new(
            text
                .TakeWhile(ch => Char.IsWhiteSpace(ch))
                .ToArray());

        if (whitespace == String.Empty)
        {
            return null;
        }

        return new WhitespaceToken(whitespace);
    }

    /// <summary>
    /// Tokenizes an argument of an instruction. This handles the parsing of whitespace and line continuations.
    /// </summary>
    /// <param name="tokenParser">Parser for the argument.</param>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludeTrailingWhitespace">A value indicating whether trailing whitespace should not be parsed.</param>
    /// <param name="excludeLeadingWhitespace">A value indicating whether leading whitespace should not be parsed.</param>
    /// <returns>Set of tokens.</returns>
    public static Parser<IEnumerable<Token>> ArgTokens(Parser<IEnumerable<Token>> tokenParser, char escapeChar,
        bool excludeTrailingWhitespace = false, bool excludeLeadingWhitespace = false)
    {
        if (excludeTrailingWhitespace)
        {
            if (excludeLeadingWhitespace)
            {
                return tokenParser;
            }
            else
            {
                return
                    from leadingWhitespace in Whitespace()
                    from token in tokenParser
                    select ConcatTokens(leadingWhitespace, token);
            }
        }
        else
        {
            Parser<IEnumerable<Token>> primaryParser;
            if (excludeLeadingWhitespace)
            {
                primaryParser = tokenParser;
            }
            else
            {
                primaryParser =
                    from leadingWhitespace in Whitespace()
                    from token in tokenParser
                    select ConcatTokens(leadingWhitespace, token);
            }

            return WithTrailingComments(
                from tokens in primaryParser
                from trailingWhitespace in
                    (from trailingWhitespace in Whitespace()
                        from lineContinuation in LineContinuations(escapeChar)
                        select ConcatTokens(trailingWhitespace, lineContinuation)).Or(
                        from whitespace in WhitespaceWithoutNewLine()
                        from newLine in NewLine()
                        select ConcatTokens(whitespace, newLine)).Optional()
                select ConcatTokens(
                    tokens,
                    trailingWhitespace.GetOrDefault()));
        }
    }

    /// <summary>
    /// Parses a string.
    /// </summary>
    /// <param name="value">Value of the string.</param>
    /// <param name="escapeChar">Escape character.</param>
    public static Parser<IEnumerable<Token>> StringToken(string value, char escapeChar)
    {
        Parser<IEnumerable<Token>>? parser = null;
        for (int i = 0; i < value.Length; i++)
        {
            int currentIndex = i;
            if (parser is null)
            {
                parser = ToStringTokens(Parse.IgnoreCase(value[currentIndex]));
            }
            else
            {
                parser = from previousTokens in parser
                            from nextTokens in StringTokenCharWithOptionalLineContinuation(escapeChar, Parse.IgnoreCase(value[currentIndex]))
                            select ConcatTokens(previousTokens, nextTokens);
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
    /// <param name="createToken">Delegate to create the token containing the character.</param>
    /// <returns>Parsed tokens.</returns>
    public static Parser<IEnumerable<Token>> CharWithOptionalLineContinuation(char escapeChar, Parser<char> charParser,
        Func<char, Token> createToken) =>
        from lineContinuation in LineContinuations(escapeChar)
        from ch in charParser
        select ConcatTokens(lineContinuation, new Token[] { createToken(ch) });

    /// <summary>
    /// Parses a single character preceded by an optional line continuation.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="charParser">Character parser.</param>
    /// <returns>Parsed tokens.</returns>
    public static Parser<IEnumerable<Token>> StringTokenCharWithOptionalLineContinuation(char escapeChar, Parser<char> charParser) =>
        CharWithOptionalLineContinuation(escapeChar, charParser, ch => new StringToken(ch.ToString()));

    /// <summary>
    /// Parses an instruction.
    /// </summary>
    /// <param name="instructionName">Name of the instruction.</param>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="instructionArgsParser">Parser for the instruction's arguments.</param>
    /// <returns>Set of tokens.</returns>
    public static Parser<IEnumerable<Token>> Instruction(string instructionName, char escapeChar, Parser<IEnumerable<Token>> instructionArgsParser) =>
        from instructionNameTokens in InstructionNameWithTrailingContent(instructionName, escapeChar)
        from instructionArgs in instructionArgsParser
        select ConcatTokens(instructionNameTokens, instructionArgs);

    /// <summary>
    /// Parses a symbol.
    /// </summary>
    /// <param name="value">Symbol value.</param>
    /// <returns>A symbol token.</returns>
    public static Parser<SymbolToken> Symbol(char value) =>
        from val in Parse.Char(value)
        select new SymbolToken(val);

    /// <summary>
    /// Concatenates a set of string parsers with an 'or' operator.
    /// </summary>
    /// <param name="parsers">Set of string parsers to concatenate.</param>
    /// <returns>String parser that matches on any of the given parsers.</returns>
    public static Parser<string> OrConcat(params Parser<string>[] parsers) =>
        from vals in parsers.Aggregate((current, next) => current.Or(next)).Many()
        select String.Concat(vals);

    /// <summary>
    /// Parses the characters of a variable reference.
    /// </summary>
    public static Parser<char> VariableRefCharParser => Parse.LetterOrDigit.Or(Parse.Char('_'));

    /// <summary>
    /// Parses the tokens within an identifier.
    /// </summary>
    /// <param name="firstCharacterParser">Parser of the first character of the identifier.</param>
    /// <param name="tailCharacterParser">Parser of the rest of the characters of the identifier.</param>
    /// <param name="escapeChar">Escape character.</param>
    public static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> IdentifierTokens(Parser<char> firstCharacterParser, Parser<char> tailCharacterParser, char escapeChar) =>
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
    public static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> LabelKeyTokens(Parser<char> firstCharacterParser, Parser<char> tailCharacterParser, char escapeChar) =>
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
    public static Parser<IEnumerable<Token>> LiteralString(char escapeChar, IEnumerable<char> excludedChars, bool excludeVariableRefChars = true) =>
        OrConcat(
            LiteralStringWithoutSpaces(escapeChar, excludedChars, excludeVariableRefChars),
            EscapedChar(escapeChar));

    /// <summary>
    /// Parses a set of argument literals.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    public static Parser<IEnumerable<Token>> ArgumentListAsLiteral(char escapeChar) =>
        from literals in
            ArgTokens(
                from literal in LiteralToken(escapeChar, Enumerable.Empty<char>()).Optional()
                select new Token[] { literal.GetOrDefault() },
                escapeChar).Many()
        select CollapseLiteralTokens(literals.Flatten(), canContainVariables: false, escapeChar);

    /// <summary>
    /// Parses a JSON array of strings.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="canContainVariables">A value indicating whether variables are allowed to be contained in the strings.</param>
    /// <param name="allowEmpty">When true, allows parsing an empty array (e.g. [] or [ ]). Defaults to false.
    /// Only exec-form command parsers should pass true; file transfer instructions (COPY/ADD) should
    /// reject empty arrays to avoid runtime errors when accessing destination tokens.</param>
    public static Parser<IEnumerable<Token>> JsonArray(char escapeChar, bool canContainVariables, bool allowEmpty = false) =>
        from openingBracket in Symbol('[').AsEnumerable()
        // Consume optional whitespace after '[' at the array level (not inside
        // the element parser) so the empty-array fallback works correctly.
        // Without this, JsonArrayElement would consume whitespace and then fail
        // at the opening quote, preventing backtracking to the empty-array case.
        // This matches the Lean/BuildKit parser structure, which parses
        // interElementSpace before attempting the optional first element.
        from leadingWs in OptionalWhitespaceOrLineContinuation(escapeChar)
        from execFormArgs in JsonArrayElements(escapeChar, canContainVariables, allowEmpty)
        from closingBracket in Symbol(']').AsEnumerable()
        select ConcatTokens(openingBracket, leadingWs, execFormArgs, closingBracket);

    /// <summary>
    /// Parses a required new line.
    /// </summary>
    public static Parser<NewLineToken> NewLine() =>
        from lineEnd in Parse.LineEnd
        select new NewLineToken(lineEnd);

    /// <summary>
    /// Parses a variable identifier reference.
    /// </summary>
    /// <returns>Parser for a variable identifier.</returns>
    public static Parser<string> VariableIdentifier() =>
        VariableRefCharParser.AtLeastOnce().Text();

    /// <summary>
    /// Parses an aggregate containing literals. This handles any variable references.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from parsing.</param>
    /// <returns>A parsed aggregate token.</returns>
    public static Parser<LiteralToken> LiteralWithVariables(
        char escapeChar, IEnumerable<char>? excludedChars = null, WhitespaceMode whitespaceMode = WhitespaceMode.Disallowed) =>
        from result in LiteralWithVariablesTokens(escapeChar, excludedChars, whitespaceMode)
        select new LiteralToken(result.Tokens, canContainVariables: true, escapeChar)
        {
            QuoteChar = result.QuoteChar
        };

    /// <summary>
    /// Parses an aggregate containing literals. This handles any variable references.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from parsing.</param>
    /// <returns>A parsed aggregate token.</returns>
    public static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> LiteralWithVariablesTokens(
        char escapeChar, IEnumerable<char>? excludedChars = null, WhitespaceMode whitespaceMode = WhitespaceMode.Disallowed)
    {
        if (excludedChars is null)
        {
            excludedChars = Enumerable.Empty<char>();
        }

        return WrappedInOptionalQuotes(
            (char escapeChar, IEnumerable<char> excludedChars, TokenWrapper tokenWrapper) =>
                from tokens in ValueOrVariableRef(
                    escapeChar,
                    (char escapeChar, IEnumerable<char> additionalExcludedChars) =>
                        WrappedInQuotesLiteralString(
                            escapeChar,
                            excludedChars.Union(additionalExcludedChars),
                            whitespaceMode == WhitespaceMode.AllowedInQuotes || whitespaceMode == WhitespaceMode.Allowed,
                            wrappingQuoteChar: tokenWrapper.OpeningString[0]),
                    excludedChars)
                    .Many()
                    .Flatten()
                select tokens,
            (char escapeChar, IEnumerable<char> excludedChars) =>
                from tokens in ValueOrVariableRef(
                    escapeChar,
                    (char escapeChar, IEnumerable<char> additionalExcludedChars) =>
                        whitespaceMode == WhitespaceMode.Allowed ?
                            LiteralString(escapeChar, excludedChars.Union(additionalExcludedChars)).Or(Whitespace().Or(LineContinuations(escapeChar))).Many().Flatten() :
                            LiteralString(escapeChar, excludedChars.Union(additionalExcludedChars)),
                    excludedChars)
                    .Many()
                    .Flatten()
                where tokens.Any()
                select TokenHelper.CollapseStringTokens(tokens),
            escapeChar,
            excludedChars);
    }

    /// <summary>
    /// Parses a literal string, including spaces, that is optionally wrapped in quotes.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludeVariableRefChars">A value indicating whether to exclude the variable ref characters.</param>
    public static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> WrappedInOptionalQuotesLiteralStringWithSpaces(
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
                    .Or(Whitespace()).Many().Flatten()
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
            .SelectMany(tokenSet => ConcatTokens(
                new Token?[] { tokenSet.QuoteChar is null ? null : new StringToken(((char)tokenSet.QuoteChar).ToString()) },
                tokenSet.Tokens,
                new Token?[] { tokenSet.QuoteChar is null ? null : new StringToken(((char)tokenSet.QuoteChar).ToString()) }));
        return (TokenHelper.CollapseStringTokens(tokens), null);
    }

    /// <summary>
    /// Parses a token for either a value or a variable reference.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="createParser">A delegate to create the token parser.</param>
    /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
    /// <returns>A token parser.</returns>
    public static Parser<IEnumerable<Token>> ValueOrVariableRef(char escapeChar, CreateTokenParserDelegate createParser,
        IEnumerable<char> excludedChars) =>
        VariableRefToken.GetParser(createParser, escapeChar).AsEnumerable()
            .Or(createParser(escapeChar, excludedChars));

    /// <summary>
    /// Parses any character except for whitespace.
    /// </summary>
    public static Parser<char> NonWhitespace() =>
        Parse.AnyChar.Except(Parse.WhiteSpace);

    /// <summary>
    /// Parses multiple line continuations and any whitespace.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    public static Parser<IEnumerable<Token>> LineContinuations(char escapeChar) =>
        LineContinuationToken.GetParser(escapeChar).Many();

    /// <summary>
    /// Parses a literal token.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
    public static Parser<LiteralToken> LiteralToken(char escapeChar, IEnumerable<char> excludedChars) =>
        from literal in LiteralString(escapeChar, excludedChars, excludeVariableRefChars: false).Many().Flatten()
        where literal.Any()
        select new LiteralToken(TokenHelper.CollapseStringTokens(literal), canContainVariables: false, escapeChar);

    /// <summary>
    /// Parses all whitespace except a new line.
    /// </summary>
    private static Parser<WhitespaceToken?> WhitespaceWithoutNewLine() =>
        from whitespace in Parse.WhiteSpace.Except(Parse.LineTerminator).XMany().Text()
        select whitespace.Length > 0 ? new WhitespaceToken(whitespace) : null;

    /// <summary>
    /// Concatenates a set of token parsers with an 'or' operator.
    /// </summary>
    /// <param name="parsers">Set of string parsers to concatenate.</param>
    /// <returns>String parser that matches on any of the given parsers.</returns>
    private static Parser<IEnumerable<Token>> OrConcat(params Parser<IEnumerable<Token>>[] parsers) =>
        from vals in (parsers.Aggregate((current, next) => current.Or(next))).Many()
        select vals.SelectMany(val => val);

    /// <summary>
    /// Excludes parsing of the specified characters from a parser.
    /// </summary>
    /// <param name="parser">The character parser to apply the exclusion to.</param>
    /// <param name="chars">Set of characters to be excluded from parsing.</param>
    /// <returns>Character parser that excludes the specified characters.</returns>
    private static Parser<char> ExceptChars(this Parser<char> parser, IEnumerable<char> chars) =>
        chars
            .Select(ch => Parse.Char(ch))
            .Aggregate(parser, (current, next) => current.Except(next));

    /// <summary>
    /// Collapses any sequential string or whitespace tokens and wraps them in a literal token.
    /// </summary>
    /// <param name="tokens">Set of tokens to process.</param>
    /// <param name="quoteChar">The quote character associated with the literal.</param>
    private static IEnumerable<Token> CollapseLiteralTokens(IEnumerable<Token> tokens,
        bool canContainVariables, char escapeChar, char? quoteChar = null)
    {
        Requires.NotNullEmptyOrNullElements(tokens, nameof(tokens));
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
    /// Creates a <see cref="LiteralToken"/> for a JSON array element from its parsed tokens.
    /// When the element is an empty string (e.g. ""), the token sequence is empty and
    /// <see cref="CollapseLiteralTokens"/> cannot be used because it requires non-empty input.
    /// In that case, a zero-length <see cref="LiteralToken"/> with <see cref="LiteralToken.QuoteChar"/>
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
                    QuoteChar = DoubleQuote
                }
            };
        }

        return CollapseLiteralTokens(materializedTokens, canContainVariables, escapeChar, DoubleQuote);
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
                select ConcatTokens(delimiter, nextArg)).Many()
            select ConcatTokens(firstArg, tail.Flatten());

        if (allowEmpty)
        {
            // Use a lookahead for ']' so only truly empty arrays (e.g. [] or [ ]) take
            // the empty path. Without this, inputs like [foo] would silently fall through
            // to the empty branch (because the element parser fails without consuming
            // input) and report "expected ']'" instead of "expected opening quote".
            var emptyArrayLookahead =
                from preview in Symbol(']').Preview()
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
        from leading in OptionalWhitespaceOrLineContinuation(escapeChar)
        from comma in Symbol(',').AsEnumerable().Optional()
        from trailing in OptionalWhitespaceOrLineContinuation(escapeChar)
        select ConcatTokens(
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
            LiteralWithVariables(escapeChar, new char[] { DoubleQuote }) :
            LiteralToken(escapeChar, new char[] { DoubleQuote });

        if (consumeLeadingWhitespace)
        {
            return
                from leading in OptionalWhitespaceOrLineContinuation(escapeChar)
                from openingQuote in Symbol(DoubleQuote)
                from argValue in ArgTokens(literalParser.AsEnumerable(), escapeChar).Many()
                from closingQuote in Symbol(DoubleQuote)
                from trailing in OptionalWhitespaceOrLineContinuation(escapeChar)
                select ConcatTokens(
                    leading,
                    CreateJsonArrayElementLiteral(argValue.Flatten(), canContainVariables, escapeChar),
                    trailing);
        }

        return
            from openingQuote in Symbol(DoubleQuote)
            from argValue in ArgTokens(literalParser.AsEnumerable(), escapeChar).Many()
            from closingQuote in Symbol(DoubleQuote)
            from trailing in OptionalWhitespaceOrLineContinuation(escapeChar)
            select ConcatTokens(
                CreateJsonArrayElementLiteral(argValue.Flatten(), canContainVariables, escapeChar),
                trailing);
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
    public static Parser<IEnumerable<Token>> IdentifierString(char escapeChar, Parser<char> firstCharacterParser,
        Parser<char> tailCharacterParser) =>
        from first in ToStringTokens(firstCharacterParser)
        from rest in OrConcat(
            StringTokenCharWithOptionalLineContinuation(escapeChar, tailCharacterParser),
            EscapedChar(escapeChar))
            .Many()
            .Flatten()
        select TokenHelper.CollapseStringTokens(ConcatTokens(first, rest));

    /// <summary>
    /// Transforms a character parser into a parser for a set of tokens containing a single string token.
    /// </summary>
    /// <param name="parser">Character parser.</param>
    private static Parser<IEnumerable<Token>> ToStringTokens(Parser<char> parser) =>
        from ch in parser
        select new Token[] { new StringToken(ch.ToString()) };

    /// <summary>
    /// Parses a literal string wrapped in quotes.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsing.</param>
    /// <param name="isWhitespaceAllowed">A value indicating whether whitespace is allowed in the string.</param>
    /// <param name="excludeVariableRefChars">A value indicating whether to exclude the variable ref characters.</param>
    /// <param name="wrappingQuoteChar">The quote character wrapping this string. Only this quote is excluded from content.</param>
    /// <returns>Parser for a literal string wrapped in quotes.</returns>
    private static Parser<IEnumerable<Token>> WrappedInQuotesLiteralString(char escapeChar, IEnumerable<char> excludedChars,
        bool isWhitespaceAllowed = false, bool excludeVariableRefChars = true, char? wrappingQuoteChar = null)
    {
        Parser<char> parser = ExceptQuote(LiteralChar(escapeChar, excludedChars, isWhitespaceAllowed, excludeVariableRefChars), wrappingQuoteChar);
        return
            from first in ToStringTokens(parser).Or(EscapedChar(escapeChar))
            from rest in OrConcat(
                StringTokenCharWithOptionalLineContinuation(escapeChar, parser)
                    .Many()
                    .Flatten(),
                EscapedChar(escapeChar))
            select TokenHelper.CollapseStringTokens(ConcatTokens(first, rest));
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
            from first in ToStringTokens(parser).Or(EscapedChar(escapeChar))
            from rest in StringTokenCharWithOptionalLineContinuation(escapeChar, parser)
                .Many()
                .Flatten()
            select TokenHelper.CollapseStringTokens(ConcatTokens(first, rest));
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
        Parser<char> parser = (isWhitespaceAllowed ? Parse.AnyChar : NonWhitespace())
            .ExceptChars(excludedChars)
            .Except(Parse.Char(escapeChar));

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
        Parse.Char('$').Then(ch => Parse.LetterOrDigit.Or(Parse.Char('{')).Or(Parse.Char('_')));

    /// <summary>
    /// Parses an escaped character.
    /// </summary>
    /// <param name="escapeChar">Escape character.</param>
    private static Parser<IEnumerable<Token>> EscapedChar(char escapeChar) =>
        from esc in Parse.Char(escapeChar)
        from v in Parse.AnyChar.AsEnumerable()
            .Except(Parse.LineEnd)
            .Text()
        select new Token[] { new StringToken(esc + v) };

    /// <summary>
    /// Parses a token that is optionally wrapped in quotes.
    /// </summary>
    /// <param name="createWrappedParser">A delegate to create a token parser for a wrapped value.</param>
    /// <param name="nonWrappedParser">A delegate to create a token parser for a value that isn't wrapped.</param>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
    private static Parser<(IEnumerable<Token> Tokens, char? QuoteChar)> WrappedInOptionalQuotes(CreateWrappedTokenParserDelegate createWrappedParser,
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
            ? parser.Except(Parse.Char(wrappingQuoteChar.Value))
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
        from opening in Parse.String(tokenWrapper.OpeningString).AsEnumerable()
        from val in createParser(escapeChar, excludedChars, tokenWrapper)
        from closing in Parse.String(tokenWrapper.ClosingString).AsEnumerable()
        select val;

    /// <summary>
    /// Parses an instruction with any trailing content.
    /// </summary>
    /// <param name="instructionName">Name of the instruction.</param>
    /// <param name="escapeChar">Escape character.</param>
    private static Parser<IEnumerable<Token>> InstructionNameWithTrailingContent(string instructionName, char escapeChar) =>
        WithTrailingComments(
            from leading in Whitespace()
            from instruction in TokenWithTrailingWhitespace(KeywordToken.GetParser(instructionName, escapeChar))
            from lineContinuation in LineContinuations(escapeChar).Optional()
            select ConcatTokens(leading, instruction, lineContinuation.GetOrDefault()));

    /// <summary>
    /// Parses a set of tokens and any trailing comments.
    /// </summary>
    /// <param name="parser">Set of token parsers.</param>
    private static Parser<IEnumerable<Token>> WithTrailingComments(Parser<IEnumerable<Token>> parser) =>
        from tokens in parser
        from commentSets in CommentText().Many()
        select ConcatTokens(tokens, commentSets.SelectMany(comments => comments));

    /// <summary>
    /// Delegate for creating a parser of a token that is wrapped by a set of characters.
    /// </summary>
    /// <typeparam name="TToken">Type of the token.</typeparam>
    /// <param name="escapeChar">The escape character.</param>
    /// <param name="excludedChars">Characters to be excluded from parsing.</param>
    /// <param name="tokenWrapper">Description of characters are wrapping the token.</param>
    /// <returns>The token parser.</returns>
    private delegate Parser<IEnumerable<Token>> CreateWrappedTokenParserDelegate(
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
    private class TokenWrapper
    {
        public TokenWrapper(string openingString, string closingString)
        {
            OpeningString = openingString;
            ClosingString = closingString;
        }

        public string OpeningString { get; }
        public string ClosingString { get; }
    }

    /// <summary>
    /// Parses a heredoc construct: &lt;&lt;[-][QUOTE]DELIM[QUOTE] followed by body lines
    /// and a closing delimiter line.
    /// Returns a HeredocToken containing all the raw text as child tokens.
    /// </summary>
    public static Parser<HeredocToken> Heredoc() =>
        i => HeredocParseImpl(i);

    private static IResult<HeredocToken> HeredocParseImpl(IInput input)
    {
        IInput current = input;

        // Parse "<<"
        if (current.AtEnd || current.Current != '<')
            return Result.Failure<HeredocToken>(current, "expected '<<'", Enumerable.Empty<string>());
        current = current.Advance();

        if (current.AtEnd || current.Current != '<')
            return Result.Failure<HeredocToken>(current, "expected '<<'", Enumerable.Empty<string>());
        current = current.Advance();

        string markerPrefix = "<<";

        // Parse optional chomp flag '-'
        bool hasChomp = false;
        if (!current.AtEnd && current.Current == '-')
        {
            hasChomp = true;
            markerPrefix += "-";
            current = current.Advance();
        }

        // Parse optional quote character and delimiter name
        char? quoteChar = null;
        if (!current.AtEnd && (current.Current == '"' || current.Current == '\''))
        {
            quoteChar = current.Current;
            current = current.Advance();
        }

        // Parse delimiter name: alphanumeric + underscore characters
        var delimChars = new List<char>();
        while (!current.AtEnd && IsHeredocDelimiterChar(current.Current))
        {
            delimChars.Add(current.Current);
            current = current.Advance();
        }

        if (delimChars.Count == 0)
            return Result.Failure<HeredocToken>(current, "expected heredoc delimiter name", Enumerable.Empty<string>());

        string delimiter = new string(delimChars.ToArray());

        // Parse closing quote if we had an opening quote
        if (quoteChar.HasValue)
        {
            if (current.AtEnd || current.Current != quoteChar.Value)
                return Result.Failure<HeredocToken>(current, $"expected closing quote '{quoteChar.Value}'", Enumerable.Empty<string>());
            current = current.Advance();
        }

        // Build the marker string token
        string markerText = markerPrefix;
        if (quoteChar.HasValue) markerText += quoteChar.Value;
        markerText += delimiter;
        if (quoteChar.HasValue) markerText += quoteChar.Value;

        List<Token> tokens = new()
        {
            new StringToken(markerText)
        };

        // Consume the rest of the current line (any text after the heredoc marker on the same line)
        var restOfLineChars = new List<char>();
        while (!current.AtEnd && current.Current != '\n' && current.Current != '\r')
        {
            restOfLineChars.Add(current.Current);
            current = current.Advance();
        }

        if (restOfLineChars.Count > 0)
        {
            tokens.Add(new StringToken(new string(restOfLineChars.ToArray())));
        }

        // Consume newline after the marker line
        if (!current.AtEnd)
        {
            string newLine = ConsumeHeredocNewLine(ref current);
            tokens.Add(new NewLineToken(newLine));
        }
        else
        {
            // No body - just the marker with no closing delimiter
            return Result.Success(new HeredocToken(tokens), current);
        }

        // Consume body lines until we find the closing delimiter on its own line
        while (!current.AtEnd)
        {
            // Read a complete line
            var lineChars = new List<char>();
            while (!current.AtEnd && current.Current != '\n' && current.Current != '\r')
            {
                lineChars.Add(current.Current);
                current = current.Advance();
            }

            string lineContent = new string(lineChars.ToArray());

            // Check for newline
            string? lineNewLine = null;
            if (!current.AtEnd)
            {
                lineNewLine = ConsumeHeredocNewLine(ref current);
            }

            // Check if this line is the closing delimiter
            string trimmedLine = hasChomp ? lineContent.TrimStart('\t') : lineContent;
            if (trimmedLine == delimiter)
            {
                // This is the closing delimiter line
                tokens.Add(new StringToken(lineContent));
                if (lineNewLine != null)
                {
                    tokens.Add(new NewLineToken(lineNewLine));
                }
                break;
            }
            else
            {
                // This is a body line - store as string token including newline
                string bodyLineText = lineContent + (lineNewLine ?? "");
                tokens.Add(new StringToken(bodyLineText));
            }
        }

        return Result.Success(new HeredocToken(tokens), current);
    }

    private static bool IsHeredocDelimiterChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static string ConsumeHeredocNewLine(ref IInput current)
    {
        if (current.Current == '\r')
        {
            current = current.Advance();
            if (!current.AtEnd && current.Current == '\n')
            {
                current = current.Advance();
                return "\r\n";
            }
            return "\r";
        }
        else if (current.Current == '\n')
        {
            current = current.Advance();
            return "\n";
        }
        return "";
    }
}

/// <summary>
/// Delegate for creating a parser of a token.
/// </summary>
/// <param name="escapeChar">The escape character.</param>
/// <param name="excludedChars">Characters to be excluded from parsing.</param>
/// <returns>The token parser.</returns>
public delegate Parser<IEnumerable<Token>> CreateTokenParserDelegate(
    char escapeChar, IEnumerable<char> excludedChars);

internal enum WhitespaceMode
{
    Disallowed,
    AllowedInQuotes,
    Allowed
}
