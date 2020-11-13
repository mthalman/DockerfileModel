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
            from lineContinuation in LineContinuationToken.GetParser(escapeChar).Optional()
            from trailing in Whitespace().Optional()
            select ConcatTokens(
                leading.GetOrDefault(),
                lineContinuation.IsDefined ? new Token[] { lineContinuation.GetOrDefault() } : Enumerable.Empty<Token>(),
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
        public static Parser<string> DelimitedIdentifier(
            Parser<char> firstCharParser, Parser<char> tailCharParser, char delimiter, int minimumDelimiters = 0) =>
            from segments in Parse.Identifier(firstCharParser, tailCharParser).Many().DelimitedBy(Parse.Char(delimiter))
            where (segments.Count() > minimumDelimiters)
            select String.Join(delimiter.ToString(), segments.SelectMany(segment => segment).ToArray());

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
                         from lineContinuation in LineContinuationToken.GetParser(escapeChar).AsEnumerable()
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
        /// Parses a keyword.
        /// </summary>
        /// <param name="keyword">Name of the keyword.</param>
        /// <param name="escapeChar">Escape character.</param>
        /// <returns>Token for the keyword.</returns>
        public static Parser<KeywordToken> Keyword(string keyword, char escapeChar)
        {
            Parser<IEnumerable<Token>>? parser = null;
            for (int i = 0; i < keyword.Length; i++)
            {
                int currentIndex = i;
                if (parser is null)
                {
                    parser = ToStringTokens(Parse.IgnoreCase(keyword[currentIndex]));
                }
                else
                {
                    parser = from previousTokens in parser
                             from nextTokens in StringTokenCharWithOptionalLineContinuation(escapeChar, Parse.IgnoreCase(keyword[currentIndex]))
                             select ConcatTokens(previousTokens, nextTokens);
                }
            }

            return from tokens in parser
                select new KeywordToken(TokenHelper.CollapseStringTokens(tokens));
        }

        ///// <summary>
        ///// Parses a single character preceded by an optional line continuation.
        ///// </summary>
        ///// <param name="escapeChar">Escape character.</param>
        ///// <param name="charParser">Character parser.</param>
        ///// <returns>Parsed tokens.</returns>
        //public static Parser<IEnumerable<Token>> SymbolTokenCharWithOptionalLineContinuation(char escapeChar, Parser<char> charParser) =>
        //    CharWithOptionalLineContinuation(escapeChar, charParser, ch => new SymbolToken(ch));

        /// <summary>
        /// Parses a single character preceded by an optional line continuation.
        /// </summary>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="charParser">Character parser.</param>
        /// <returns>Parsed tokens.</returns>
        public static Parser<IEnumerable<Token>> CharWithOptionalLineContinuation(char escapeChar, Parser<char> charParser, Func<char, Token> createToken) =>
            from lineContinuation in LineContinuationToken.GetParser(escapeChar).Many()
            from ch in charParser
            select ConcatTokens(lineContinuation, new Token[] { createToken(ch) });

        /// <summary>
        /// Parses a single character preceded by an optional line continuation.
        /// </summary>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="charParser">Character parser.</param>
        /// <returns>Parsed tokens.</returns>
        private static Parser<IEnumerable<Token>> StringTokenCharWithOptionalLineContinuation(char escapeChar, Parser<char> charParser) =>
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
        /// Parses the first letter of an argument reference.
        /// </summary>
        public static Parser<char> ArgRefFirstLetterParser => Parse.Letter;

        /// <summary>
        /// Parses the tail characters of an argument reference.
        /// </summary>
        public static Parser<char> ArgRefTailParser => Parse.LetterOrDigit.Or(Parse.Char('_'));

        /// <summary>
        /// Parses an identifier token.
        /// </summary>
        /// <param name="firstCharacterParser">Parser of the first character of the identifier.</param>
        /// <param name="tailCharacterParser">Parser of the rest of the characters of the identifier.</param>
        /// <param name="escapeChar">Escape character.</param>
        /// <returns>Parser for an identifier token.</returns>
        public static Parser<IdentifierToken> IdentifierToken(Parser<char> firstCharacterParser, Parser<char> tailCharacterParser, char escapeChar) =>
            WrappedInOptionalQuotes<IdentifierToken>(
                (char escapeChar, IEnumerable<char> excludedChars, TokenWrapper tokenWrapper) =>
                    from identifier in WrappedInQuotesIdentifier(escapeChar, firstCharacterParser, tailCharacterParser)
                    select new IdentifierToken(identifier)
                    {
                        QuoteChar = tokenWrapper.OpeningString[0]
                    },
                (char escapeChar, IEnumerable<char> excludedChars) =>
                    from identifier in IdentifierString(escapeChar, firstCharacterParser, tailCharacterParser)
                    select new Token[] { new IdentifierToken(identifier) },
                escapeChar,
                Enumerable.Empty<char>())
                .Single()
                .Cast<Token, IdentifierToken>();

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

        public static Parser<IEnumerable<Token>> Flag(char escapeChar, Parser<IEnumerable<Token>> flagParser) =>
             from flag in ArgTokens(Symbol('-').AsEnumerable(), escapeChar).Repeat(2)
             from lineCont in LineContinuationToken.GetParser(escapeChar).AsEnumerable().Optional()
             from token in flagParser
             select ConcatTokens(flag.Flatten(), lineCont.GetOrDefault(), token);

        public static Parser<IEnumerable<Token>> ArgumentListAsLiteral(char escapeChar) =>
            from literals in ArgTokens(
                LiteralToken(escapeChar, Enumerable.Empty<char>()).AsEnumerable(),
                escapeChar).Many()
            select CollapseLiteralTokens(literals.Flatten());

        /// <summary>
        /// Parses a literal token.
        /// </summary>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
        public static Parser<LiteralToken> LiteralToken(char escapeChar, IEnumerable<char> excludedChars) =>
            from literal in LiteralString(escapeChar, excludedChars, excludeVariableRefChars: false).Many().Flatten()
            where literal.Any()
            select new LiteralToken(TokenHelper.CollapseStringTokens(literal));

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

        private static IEnumerable<Token> CollapseLiteralTokens(IEnumerable<Token> tokens, char? quoteChar = null)
        {
            Requires.NotNullEmptyOrNullElements(tokens, nameof(tokens));
            return new Token[]
           {
                new LiteralToken(
                    TokenHelper.CollapseTokens(ExtractLiteralTokenContents(tokens),
                        token => token is StringToken || token.GetType() == typeof(WhitespaceToken),
                        val => new StringToken(val)))
                {
                    QuoteChar = quoteChar
                }
           };
        }

        private static Parser<IEnumerable<Token>> JsonArrayElementDelimiter(char escapeChar) =>
            from leading in OptionalWhitespaceOrLineContinuation(escapeChar)
            from comma in Symbol(',').AsEnumerable()
            from trailing in OptionalWhitespaceOrLineContinuation(escapeChar)
            select ConcatTokens(
                leading,
                comma,
                trailing);

        private static Parser<IEnumerable<Token>> JsonArrayElement(char escapeChar, bool canContainVariables)
        {
            Parser<LiteralToken> literalParser = canContainVariables ?
                LiteralAggregate(escapeChar, new char[] { DoubleQuote }) :
                LiteralToken(escapeChar, new char[] { DoubleQuote });

            return
                from leading in OptionalWhitespaceOrLineContinuation(escapeChar)
                from openingQuote in Symbol(DoubleQuote)
                from argValue in ArgTokens(literalParser.AsEnumerable(), escapeChar).Many()
                from closingQuote in Symbol(DoubleQuote)
                from trailing in OptionalWhitespaceOrLineContinuation(escapeChar)
                select ConcatTokens(
                    leading,
                    CollapseLiteralTokens(argValue.Flatten(), DoubleQuote),
                    trailing);
        }
            

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
        private static Parser<IEnumerable<Token>> WrappedInQuotesIdentifier(char escapeChar, Parser<char> firstCharacterParser, Parser<char> tailCharacterParser) =>
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
        private static Parser<IEnumerable<Token>> IdentifierString(char escapeChar, Parser<char> firstCharacterParser, Parser<char> tailCharacterParser) =>
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
        /// <returns>Parser for a literal string wrapped in quotes.</returns>
        private static Parser<IEnumerable<Token>> WrappedInQuotesLiteralString(char escapeChar, IEnumerable<char> excludedChars)
        {
            Parser<char> parser = ExceptQuotes(LiteralChar(escapeChar, excludedChars));
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
        /// Parses a variable identifier reference.
        /// </summary>
        /// <returns>Parser for a variable identifier.</returns>
        public static Parser<string> VariableIdentifier() =>
            Parse.Identifier(ArgRefFirstLetterParser, ArgRefTailParser);

        /// <summary>
        /// Parses an aggregate containing literals. This handles any variable references.
        /// </summary>
        /// <typeparam name="TToken">Type of the aggregate token.</typeparam>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="createToken">A delegate to create the aggregate token.</param>
        /// <param name="excludedChars">Characters to exclude from parsing.</param>
        /// <returns>A parsed aggregate token.</returns>
        public static Parser<LiteralToken> LiteralAggregate(
            char escapeChar, IEnumerable<char>? excludedChars = null)
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
                            WrappedInQuotesLiteralString(escapeChar, excludedChars.Union(additionalExcludedChars)),
                        excludedChars)
                        .Many()
                        .Flatten()
                    select CreateAggregateToken(tokens => new LiteralToken(tokens), tokens, tokenWrapper),
                (char escapeChar, IEnumerable<char> excludedChars) =>
                    from tokens in ValueOrVariableRef(
                        escapeChar,
                        (char escapeChar, IEnumerable<char> additionalExcludedChars) =>
                            LiteralString(escapeChar, excludedChars.Union(additionalExcludedChars)),
                        excludedChars)
                        .Many()
                        .Flatten()
                    where tokens.Any()
                    select new Token[] { new LiteralToken(TokenHelper.CollapseStringTokens(tokens)) },
                escapeChar,
                excludedChars)
                .Single()
                .Cast<Token, LiteralToken>();
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
        /// Creates an aggregate token.
        /// </summary>
        /// <typeparam name="TAggregateToken">Type of the aggregate token.</typeparam>
        /// <param name="createToken">A delegate to create the aggregate token.</param>
        /// <param name="childTokens">The child tokens to be contained in the aggregate token.</param>
        /// <param name="tokenWrapper">The token wrapper describing the characters wrapping the token.</param>
        private static TAggregateToken CreateAggregateToken<TAggregateToken>(
            Func<IEnumerable<Token>, TAggregateToken> createToken,
            IEnumerable<Token> childTokens,
            TokenWrapper tokenWrapper)
            where TAggregateToken : AggregateToken, IQuotableToken
        {
            TAggregateToken container = createToken(TokenHelper.CollapseStringTokens(childTokens));
            container.QuoteChar = tokenWrapper.OpeningString[0];
            return container;
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
                parser = parser.Except(ExceptVariableRefChars());
            }

            return parser;
        }
            
        private static Parser<char> ExceptVariableRefChars() =>
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
        /// <typeparam name="TToken">Type of the token.</typeparam>
        /// <param name="createWrappedParser">A delegate to create a token parser for a wrapped value.</param>
        /// <param name="nonWrappedParser">A delegate to create a token parser for a value that isn't wrapped.</param>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
        private static Parser<IEnumerable<Token>> WrappedInOptionalQuotes<TToken>(CreateWrappedTokenParserDelegate<TToken> createWrappedParser,
            CreateTokenParserDelegate nonWrappedParser, char escapeChar, IEnumerable<char> excludedChars)
            where TToken : Token =>
            WrappedInOptionalCharacters(
                createWrappedParser,
                nonWrappedParser,
                escapeChar,
                excludedChars,
                new TokenWrapper(SingleQuote.ToString(), SingleQuote.ToString()),
                new TokenWrapper(DoubleQuote.ToString(), DoubleQuote.ToString()));

        /// <summary>
        /// Parses a token that is optionally wrapped in a set of characters.
        /// </summary>
        /// <typeparam name="TToken">Type of the token.</typeparam>
        /// <param name="createWrappedParser">A delegate to create a token parser for a wrapped value.</param>
        /// <param name="nonWrappedParser">A delegate to create a token parser for a value that isn't wrapped.</param>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
        /// <param name="tokenWrappers">Set of token wrappers describing the characters that can optionally wrap the value.</param>
        /// <returns></returns>
        private static Parser<IEnumerable<Token>> WrappedInOptionalCharacters<TToken>(CreateWrappedTokenParserDelegate<TToken> createWrappedParser,
            CreateTokenParserDelegate createNonWrappedParser, char escapeChar, IEnumerable<char> excludedChars,
            params TokenWrapper[] tokenWrappers)
            where TToken : Token =>
            tokenWrappers
                .Select(tokenWrapper =>
                    WrappedInCharacters(
                        createWrappedParser,
                        escapeChar,
                        tokenWrapper,
                        excludedChars))
                .Aggregate((current, next) => current.Or(next))
                .Select(token => new Token[] { token })
            .XOr(createNonWrappedParser(escapeChar, excludedChars));

        /// <summary>
        /// Parses a character that excludes quotes.
        /// </summary>
        /// <param name="parser">A character parser to exclude quotes from.</param>
        private static Parser<char> ExceptQuotes(Parser<char> parser) =>
            parser.ExceptChars(Quotes);

        /// <summary>
        /// Parses a token that is wrapped by a set of characters.
        /// </summary>
        /// <typeparam name="TToken">Type of the token.</typeparam>
        /// <param name="createParser">A delegate that creates a token parser.</param>
        /// <param name="escapeChar">Escape character.</param>
        /// <param name="tokenWrapper">A token wrapper describing the set of characters wrapping the value.</param>
        /// <param name="excludedChars">Characters to exclude from the parsed value.</param>
        private static Parser<TToken> WrappedInCharacters<TToken>(CreateWrappedTokenParserDelegate<TToken> createParser,
            char escapeChar, TokenWrapper tokenWrapper, IEnumerable<char> excludedChars)
            where TToken : Token =>
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
                from instruction in TokenWithTrailingWhitespace(Keyword(instructionName, escapeChar))
                from lineContinuation in LineContinuationToken.GetParser(escapeChar).Optional()
                select ConcatTokens(leading, instruction, new Token[] { lineContinuation.GetOrDefault() }));

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
        private delegate Parser<TToken> CreateWrappedTokenParserDelegate<TToken>(
            char escapeChar,
            IEnumerable<char> excludedChars,
            TokenWrapper tokenWrapper)
            where TToken : Token;

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
}
