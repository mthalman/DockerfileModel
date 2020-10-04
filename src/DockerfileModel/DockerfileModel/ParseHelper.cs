using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    internal static class ParseHelper
    {
        private delegate TToken CreateWrappedTokenDelegate<TToken>(string value, (string OpeningString, string ClosingString)? chars)
            where TToken : Token;

        private delegate Parser<TToken> CreateTokenParserDelegate<TToken>(
            Parser<string> allowedSubstringsParser, (string OpeningString, string ClosingString)? chars = null)
            where TToken : Token;

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

        public static Parser<IEnumerable<Token>> Whitespace() =>
            from whitespace in Parse.WhiteSpace.Except(Parse.LineTerminator).XMany().Text()
            from newLine in OptionalNewLine()
            select ConcatTokens(
                whitespace.Length > 0 ? new WhitespaceToken(whitespace) : null,
                newLine);

        public static Parser<IEnumerable<Token>> CommentText() =>
            from leading in WhitespaceChars().AsEnumerable()
            from comment in CommentToken.GetParser()
            from lineEnd in OptionalNewLine().AsEnumerable()
            select ConcatTokens(leading, new Token[] { new CommentToken(comment) }, lineEnd);

        public static IEnumerable<Token> ConcatTokens(params Token?[]? tokens) =>
            FilterNulls(tokens).ToList();

        public static IEnumerable<Token> ConcatTokens(params IEnumerable<Token?>[] tokens) =>
            ConcatTokens(
                FilterNulls(tokens)
                    .SelectMany(tokens => tokens)
                    .ToArray());

        public static Parser<string> DelimitedIdentifier(char delimiter, int minimumDelimiters = 0) =>
            from segments in Parse.Identifier(Parse.LetterOrDigit, Parse.LetterOrDigit).Many().DelimitedBy(Parse.Char(delimiter))
            where (segments.Count() > minimumDelimiters)
            select String.Join(delimiter.ToString(), segments.SelectMany(segment => segment).ToArray());

        public static Parser<NewLineToken> OptionalNewLine() =>
            from lineEnd in Parse.LineEnd.Optional()
            select lineEnd.IsDefined ? new NewLineToken(lineEnd.Get()) : null;

        public static Parser<WhitespaceToken> WhitespaceChars() =>
            from whitespace in Parse.WhiteSpace.Many().Text()
            select whitespace != "" ? new WhitespaceToken(whitespace) : null;

        public static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Parser<Token> parser) =>
            from token in parser.AsEnumerable()
            from trailingWhitespace in Whitespace()
            select ConcatTokens(token, trailingWhitespace);

        public static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Func<string, Token> createToken) =>
            from val in Parse.AnyChar.Except(Parse.LineEnd).Many().Text()
            select ConcatTokens(createToken(val.Trim()), GetTrailingWhitespaceToken(val)!);

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

        public static Parser<IEnumerable<Token>> ArgTokens(Parser<IEnumerable<Token>> tokenParser, char escapeChar) =>
            WithTrailingComments(
                from leadingWhitespace in WhitespaceChars().AsEnumerable()
                from token in tokenParser
                from trailingWhitespace in Whitespace().Optional()
                from lineContinuation in LineContinuation(escapeChar).Optional()
                from lineEnd in OptionalNewLine().AsEnumerable()
                select ConcatTokens(
                    leadingWhitespace,
                    token,
                    trailingWhitespace.GetOrDefault(),
                    lineContinuation.GetOrDefault(),
                    lineEnd));

        public static Parser<IEnumerable<Token>> LineContinuation(char escapeChar) =>
           from lineCont in Parse.Char(escapeChar)
           from whitespace in Parse.WhiteSpace.Except(Parse.LineEnd).Many()
           from lineEnding in Parse.LineEnd
           select ConcatTokens(
               new LineContinuationToken(lineCont.ToString()),
               whitespace.Any() ? new WhitespaceToken(new string(whitespace.ToArray())) : null,
               new NewLineToken(lineEnding));

        public static IEnumerable<Token?> GetInstructionArgLineContent(string text)
        {
            if (text.Length == 0)
            {
                yield break;
            }

            if (text.Trim().Length == 0)
            {
                yield return new WhitespaceToken(text);
                yield break;
            }

            yield return GetLeadingWhitespaceToken(text);
            yield return new LiteralToken(text.Trim());
            yield return GetTrailingWhitespaceToken(text);
        }

        public static Parser<KeywordToken> InstructionIdentifier(string instructionName) =>
            from text in Parse.IgnoreCase(instructionName).Text()
            select new KeywordToken(text);

        public static Parser<IEnumerable<Token>> Instruction(string instructionName, char escapeChar, Parser<IEnumerable<Token>> instructionArgsParser) =>
            from instructionNameTokens in InstructionNameWithTrailingContent(instructionName, escapeChar)
            from instructionArgs in instructionArgsParser
            select ConcatTokens(instructionNameTokens, instructionArgs);

        public static Parser<SymbolToken> Symbol(string value) =>
            from val in Parse.String(value).Text()
            select new SymbolToken(val);

        private static Parser<string> Identifier(Parser<char> firstLetterParser, Parser<string> tailParser) =>
            from firstLetter in firstLetterParser
            from tail in tailParser
            select firstLetter + tail;

        private static Parser<string> OrConcat(params Parser<string>[] parsers) =>
            from vals in (parsers.Aggregate((current, next) => current.Or(next))).Many()
            select String.Concat(vals);

        public static Parser<IdentifierToken> QuotableIdentifier(Parser<char> firstLetterParser, Parser<char> tailLetterParser, char escapeChar) =>
            WrappedInOptionalQuotes<IdentifierToken>(
                (Parser<string> allowedSubstringsParser, (string OpeningString, string ClosingString)? chars) =>
                    from identifier in Identifier(
                        ExceptLiteralOrIdentifierWrappingChars(firstLetterParser),
                        OrConcat(ExceptLiteralOrIdentifierWrappingChars(tailLetterParser).Many().Text(), allowedSubstringsParser))
                    select new IdentifierToken(identifier)
                    {
                        QuoteChar = chars?.OpeningString[0]
                    },
                (Parser<string> allowedSubstringsParser, (string OpeningString, string ClosingString)? chars) =>
                    from identifierSegments in
                        Identifier(
                            firstLetterParser,
                            OrConcat(tailLetterParser.Many().Text(), allowedSubstringsParser))
                    select new IdentifierToken(identifierSegments),
                escapeChar);

        public static Parser<LiteralToken> Literal(char escapeChar) =>
            WrappedInOptionalQuotes<LiteralToken>(
                (Parser<string> allowedSubstringsParser, (string OpeningString, string ClosingString)? chars) =>
                    from literal in OrConcat(
                        ExceptLiteralOrIdentifierWrappingChars(LiteralToken(escapeChar)).Many().Text(),
                        allowedSubstringsParser)
                    select new LiteralToken(literal)
                    {
                        QuoteChar = chars?.OpeningString[0]
                    },
                (Parser<string> allowedSubstringsParser, (string OpeningString, string ClosingString)? chars) =>
                    from literal in OrConcat(
                        LiteralToken(escapeChar).AtLeastOnce().Text(),
                        allowedSubstringsParser)
                    where !String.IsNullOrEmpty(literal)
                    select new LiteralToken(literal),
                escapeChar);

        public static Parser<char> NonWhitespace() =>
            Parse.AnyChar.Except(Parse.WhiteSpace);

        private static Parser<char> LiteralToken(char escapeChar) =>
            NonWhitespace().Except(Parse.Char(escapeChar));

        private static Parser<string> EscapedChar(char val, char escapeChar) =>
            Parse.String($"{escapeChar}{val}").Text();

        private static Parser<TToken> WrappedInOptionalQuotes<TToken>(CreateTokenParserDelegate<TToken> createWrappedParser,
            CreateTokenParserDelegate<TToken> nonWrappedParser, char escapeChar)
            where TToken : QuotableToken =>
            WrappedInOptionalCharacters(
                createWrappedParser,
                nonWrappedParser,
                escapeChar,
                new char[] { '\'', '\"' },
                ("\'", "\'"),
                ("\"", "\""));

        private static Parser<TToken> WrappedInOptionalCharacters<TToken>(CreateTokenParserDelegate<TToken> createWrappedParser,
            CreateTokenParserDelegate<TToken> createNonWrappedParser, char escapeChar, IEnumerable<char> escapableChars,
            params (string OpeningString, string ClosingString)[] wrappingChars)
            where TToken : Token =>
            wrappingChars
                .Select(chars =>
                    WrappedInCharacters(
                        createWrappedParser,
                        escapeChar,
                        escapableChars,
                        chars.OpeningString,
                        chars.ClosingString))
                .Aggregate((current, next) => current.Or(next))
            .Or(createNonWrappedParser(GetEscapedCharsParser(escapableChars, escapeChar)));

        private static Parser<char> ExceptLiteralOrIdentifierWrappingChars(Parser<char> parser) =>
            parser
                .Except(Parse.Char('\''))
                .Except(Parse.Char('\"'))
                .Except(Parse.Char('$'));

        private static Parser<string> GetEscapedCharsParser(IEnumerable<char> escapableChars, char escapeChar) =>
            escapableChars
                .Select(escapableChar => EscapedChar(escapableChar, escapeChar))
                .Aggregate((current, next) => current.Or(next));

        private static Parser<TToken> WrappedInCharacters<TToken>(CreateTokenParserDelegate<TToken> createParser,
            char escapeChar, IEnumerable<char> escapableChars, string openingString, string closingString)
            where TToken : Token =>
            from openingQuote in Parse.String(openingString).AsEnumerable()
            from val in createParser(GetEscapedCharsParser(escapableChars, escapeChar), (openingString, closingString))
            from closingQuote in Parse.String(closingString).AsEnumerable()
            select val;

        private static Parser<IEnumerable<Token>> InstructionNameWithTrailingContent(string instructionName, char escapeChar) =>
            WithTrailingComments(
                from leading in WhitespaceChars().AsEnumerable()
                from instruction in TokenWithTrailingWhitespace(InstructionIdentifier(instructionName))
                from lineContinuation in LineContinuation(escapeChar).Optional()
                select ConcatTokens(leading, instruction, lineContinuation.GetOrDefault()));

        private static WhitespaceToken? GetLeadingWhitespaceToken(string text)
        {
            string? whitespace = new string(
                text
                    .TakeWhile(ch => Char.IsWhiteSpace(ch))
                    .ToArray());

            if (whitespace == String.Empty)
            {
                return null;
            }

            return new WhitespaceToken(whitespace);
        }

        private static Parser<IEnumerable<Token>> WithTrailingComments(Parser<IEnumerable<Token?>> parser) =>
            from tokens in parser
            from commentSets in CommentText().Many()
            select ConcatTokens(tokens, commentSets.SelectMany(comments => comments));
    }
}
