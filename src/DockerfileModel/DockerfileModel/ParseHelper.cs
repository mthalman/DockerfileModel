using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;

namespace DockerfileModel
{
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
            string? whitespace = new string(
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
        /// Tokenizes an argument of an instruction. This handles the parsing of whitespace and line continuations.
        /// </summary>
        /// <param name="tokenParser">Parser for the argument.</param>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="excludeTrailingWhitespace">A value indicating whether trailing whitespace should not be parsed.</param>
        /// <returns>Set of tokens.</returns>
        public static Parser<IEnumerable<Token>> ArgTokens(Parser<IEnumerable<Token>> tokenParser, char escapeChar, bool excludeTrailingWhitespace = false)
        {
            if (excludeTrailingWhitespace)
            {
                return
                    from leadingWhitespace in Whitespace()
                    from token in tokenParser
                    select ConcatTokens(leadingWhitespace, token);
            }
            else
            {
                return WithTrailingComments(
                    from leadingWhitespace in Whitespace()
                    from token in tokenParser
                    from trailingWhitespace in
                        (from trailingWhitespace in Whitespace()
                         from lineContinuation in LineContinuations(escapeChar)
                         select ConcatTokens(trailingWhitespace, lineContinuation)).Or(
                            from whitespace in WhitespaceWithoutNewLine()
                            from newLine in NewLine()
                            select ConcatTokens(whitespace, newLine)).Optional()
                    select ConcatTokens(
                        leadingWhitespace,
                        token,
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
                    WrappedInQuotesIdentifier(escapeChar, firstCharacterParser, tailCharacterParser),
                (char escapeChar, IEnumerable<char> excludedChars) =>
                    IdentifierString(escapeChar, firstCharacterParser, tailCharacterParser),
                escapeChar,
                Enumerable.Empty<char>());

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
        public static Parser<IEnumerable<Token>> JsonArray(char escapeChar, bool canContainVariables) =>
           from openingBracket in Symbol('[').AsEnumerable()
           from execFormArgs in
               from arg in JsonArrayElement(escapeChar, canContainVariables).Once().Flatten()
               from tail in (
                   from delimiter in JsonArrayElementDelimiter(escapeChar)
                   from nextArg in JsonArrayElement(escapeChar, canContainVariables)
                   select ConcatTokens(delimiter, nextArg)).Many()
               select ConcatTokens(arg, tail.Flatten())
           from closingBracket in Symbol(']').AsEnumerable()
           select ConcatTokens(openingBracket, execFormArgs, closingBracket);

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
                                whitespaceMode == WhitespaceMode.AllowedInQuotes || whitespaceMode == WhitespaceMode.Allowed),
                        excludedChars)
                        .Many()
                        .Flatten()
                    select tokens,
                (char escapeChar, IEnumerable<char> excludedChars) =>
                    from tokens in ValueOrVariableRef(
                        escapeChar,
                        (char escapeChar, IEnumerable<char> additionalExcludedChars) =>
                            whitespaceMode == WhitespaceMode.Allowed ?
                                LiteralString(escapeChar, excludedChars.Union(additionalExcludedChars)).Or(Whitespace()) :
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
                    WrappedInQuotesLiteralString(escapeChar, excludedChars, isWhitespaceAllowed: true, excludeVariableRefChars: excludeVariableRefChars),
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
        /// Parses a literal token.
        /// </summary>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
        private static Parser<LiteralToken> LiteralToken(char escapeChar, IEnumerable<char> excludedChars) =>
            from literal in LiteralString(escapeChar, excludedChars, excludeVariableRefChars: false).Many().Flatten()
            where literal.Any()
            select new LiteralToken(TokenHelper.CollapseStringTokens(literal), canContainVariables: false, escapeChar);

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
        /// Parses a JSON aray element delimiter (i.e. comma) with optional whitespace.
        /// </summary>
        /// <param name="escapeChar">Escape character.</param>
        private static Parser<IEnumerable<Token>> JsonArrayElementDelimiter(char escapeChar) =>
            from leading in OptionalWhitespaceOrLineContinuation(escapeChar)
            from comma in Symbol(',').AsEnumerable()
            from trailing in OptionalWhitespaceOrLineContinuation(escapeChar)
            select ConcatTokens(
                leading,
                comma,
                trailing);

        /// <summary>
        /// Parses a JSON array string element.
        /// </summary>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="canContainVariables">A value indicating whether the string can contain variables.</param>
        private static Parser<IEnumerable<Token>> JsonArrayElement(char escapeChar, bool canContainVariables)
        {
            Parser<LiteralToken> literalParser = canContainVariables ?
                LiteralWithVariables(escapeChar, new char[] { DoubleQuote }) :
                LiteralToken(escapeChar, new char[] { DoubleQuote });

            return
                from leading in OptionalWhitespaceOrLineContinuation(escapeChar)
                from openingQuote in Symbol(DoubleQuote)
                from argValue in ArgTokens(literalParser.AsEnumerable(), escapeChar).Many()
                from closingQuote in Symbol(DoubleQuote)
                from trailing in OptionalWhitespaceOrLineContinuation(escapeChar)
                select ConcatTokens(
                    leading,
                    CollapseLiteralTokens(argValue.Flatten(), canContainVariables, escapeChar, DoubleQuote),
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
        /// <returns>Parser for an identifier string wrapped in quotes.</returns>
        private static Parser<IEnumerable<Token>> WrappedInQuotesIdentifier(char escapeChar, Parser<char> firstCharacterParser,
            Parser<char> tailCharacterParser) =>
            IdentifierString(
                escapeChar,
                ExceptQuotes(firstCharacterParser),
                ExceptQuotes(tailCharacterParser));

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
        /// <returns>Parser for a literal string wrapped in quotes.</returns>
        private static Parser<IEnumerable<Token>> WrappedInQuotesLiteralString(char escapeChar, IEnumerable<char> excludedChars,
            bool isWhitespaceAllowed = false, bool excludeVariableRefChars = true)
        {
            Parser<char> parser = ExceptQuotes(LiteralChar(escapeChar, excludedChars, isWhitespaceAllowed, excludeVariableRefChars));
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
            Parse.Char('$').Then(ch => Parse.LetterOrDigit.Or(Parse.Char('{')));

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
                this.OpeningString = openingString;
                this.ClosingString = closingString;
            }

            public string OpeningString { get; }
            public string ClosingString { get; }
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
}
