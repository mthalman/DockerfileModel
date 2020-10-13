using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    internal static class ParseHelper
    {
        private const char SingleQuote = '\'';
        private const char DoubleQuote = '\"';

        private delegate TToken CreateWrappedTokenDelegate<TToken>(string value, (string OpeningString, string ClosingString)? chars)
            where TToken : Token;

        private delegate Parser<TToken> CreateWrappedTokenParserDelegate<TToken>(
            char escapeChar, 
            (string OpeningString, string ClosingString) chars)
            where TToken : Token;

        private delegate Parser<TToken> CreateTokenParserDelegate<TToken>(
            char escapeChar)
            where TToken : Token;

        private delegate Parser<string> CreatePrimitiveParserDelegate(char escapeChar);

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

        public static Parser<char> ArgRefFirstLetterParser => Parse.Letter;
        public static Parser<char> ArgRefTailParser => Parse.LetterOrDigit.Or(Parse.Char('_'));

        public static Parser<IdentifierToken> QuotableIdentifier(Parser<char> firstLetterParser, Parser<char> tailLetterParser, char escapeChar) =>
            WrappedInOptionalQuotes<IdentifierToken>(
                (char escapeChar, (string OpeningString, string ClosingString) chars) =>
                    from identifier in Identifier(
                        ExceptQuotes(firstLetterParser),
                        OrConcat(ExceptQuotes(tailLetterParser).Many().Text(), EscapedChar(escapeChar)))
                    select new IdentifierToken(identifier)
                    {
                        QuoteChar = chars.OpeningString[0]
                    },
                (char escapeChar) =>
                    from identifierSegments in
                        Identifier(
                            firstLetterParser,
                            OrConcat(tailLetterParser.Many().Text(), EscapedChar(escapeChar)))
                    select new IdentifierToken(identifierSegments),
                escapeChar);

        private static Parser<string> WrappedInQuotesIdentifier(char escapeChar, Parser<char> firstLetterParser, Parser<char> tailLetterParser) =>
            Identifier(
                ExceptQuotes(firstLetterParser),
                OrConcat(ExceptQuotes(tailLetterParser).Many().Text(), EscapedChar(escapeChar)));

        private static Parser<string> IdentifierString(char escapeChar, Parser<char> firstLetterParser, Parser<char> tailLetterParser) =>
            Identifier(
                firstLetterParser,
                OrConcat(tailLetterParser.Many().Text(), EscapedChar(escapeChar)));

        private static Parser<string> WrappedInQuotesLiteralString(char escapeChar, bool isWhitespaceAllowed) =>
            OrConcat(
                ExceptQuotes(LiteralChar(escapeChar, isWhitespaceAllowed)).Many().Text(),
                EscapedChar(escapeChar));

        private static Parser<string> LiteralString(char escapeChar, bool isWhitespaceAllowed) =>
            OrConcat(
                isWhitespaceAllowed ? LiteralStringWithSpaces(escapeChar) : LiteralStringWithoutSpaces(escapeChar),
                EscapedChar(escapeChar));

        private static Parser<string> LiteralStringWithoutSpaces(char escapeChar) =>
            LiteralChar(escapeChar).AtLeastOnce().Text();

        private static Parser<string> LiteralStringWithSpaces(char escapeChar) =>
            from first in LiteralChar(escapeChar)
            from rest in
                (from charsPrecedingWhitespace in LiteralChar(escapeChar).Many().Text()
                 from whitespace in Parse.WhiteSpace.Many().Text()
                 from charsAfterWhitespace in LiteralChar(escapeChar).Many().Text().Or(EscapedChar(escapeChar)).Many()
                 let stringAfterWhitespace = String.Concat(charsAfterWhitespace)
                 where stringAfterWhitespace.Length > 0
                 select charsPrecedingWhitespace + whitespace + stringAfterWhitespace).Many()
            select first + String.Concat(rest);

        private static Parser<VariableRefToken> SimpleVariableReference() =>
            from variableChar in Parse.Char('$')
            from variableIdentifier in Parse.Identifier(ArgRefFirstLetterParser, ArgRefTailParser)
            select new VariableRefToken(new Token[] { new LiteralToken(variableIdentifier) });

        private static Parser<VariableRefToken> BracedVariableReference<TPrimitiveToken>(
            char escapeChar, CreateTokenParserDelegate<TPrimitiveToken> createNonQuotedPrimitiveTokenDelegate)
            where TPrimitiveToken : PrimitiveToken =>
            from start in Parse.String("${")
            from tokens in PrimitiveOrVariableRef(escapeChar, createNonQuotedPrimitiveTokenDelegate).Many()
            from end in Parse.Char('}')
            select new VariableRefToken(tokens)
            {
                WrappedInBraces = true
            };

        private static Parser<VariableRefToken> VariableReference<TPrimitiveToken>(
            char escapeChar, CreateTokenParserDelegate<TPrimitiveToken> createNonQuotedPrimitiveTokenDelegate)
            where TPrimitiveToken : PrimitiveToken =>
            SimpleVariableReference().Or(BracedVariableReference(escapeChar, createNonQuotedPrimitiveTokenDelegate));

        public static Parser<TContainerToken> LiteralContainer<TContainerToken>(
            char escapeChar, bool isWhitespaceAllowed, Func<IEnumerable<Token>, TContainerToken> createContainerToken)
            where TContainerToken : QuotableAggregateToken =>
            PrimitiveContainer(escapeChar, createContainerToken,
                CreatePrimitiveTokenDelegate(true,
                    escapeChar => WrappedInQuotesLiteralString(escapeChar, isWhitespaceAllowed),
                    val => new LiteralToken(val)),
                CreatePrimitiveTokenDelegate(false,
                    escapeChar => LiteralString(escapeChar, isWhitespaceAllowed),
                    val => new LiteralToken(val)));

        public static Parser<TContainerToken> IdentifierContainer<TContainerToken>(
            Parser<char> firstLetterParser, Parser<char> tailParser, char escapeChar, Func<IEnumerable<Token>, TContainerToken> createContainerToken)
            where TContainerToken : QuotableAggregateToken =>
            PrimitiveContainer(escapeChar, createContainerToken,
                CreatePrimitiveTokenDelegate(true,
                    escapeChar => WrappedInQuotesIdentifier(escapeChar, firstLetterParser, tailParser),
                    val => new IdentifierToken(val)),
                CreatePrimitiveTokenDelegate(false,
                    escapeChar => IdentifierString(escapeChar, firstLetterParser, tailParser),
                    val => new IdentifierToken(val)));

        private static Parser<TContainerToken> PrimitiveContainer<TContainerToken, TPrimitiveToken>(
            char escapeChar, Func<IEnumerable<Token>, TContainerToken> createContainerToken,
            CreateTokenParserDelegate<TPrimitiveToken> createQuotedPrimitiveTokenDelegate,
            CreateTokenParserDelegate<TPrimitiveToken> createNonQuotedPrimitiveTokenDelegate)
            where TContainerToken : QuotableAggregateToken
            where TPrimitiveToken : PrimitiveToken =>
            AggregateWrappedInOptionalQuotes(
                (char escapeChar, (string OpeningString, string ClosingString) chars) =>
                    from tokens in PrimitiveOrVariableRef(escapeChar, createQuotedPrimitiveTokenDelegate).Many()
                    select CreateContainerToken(createContainerToken, tokens, chars),
                escapeChar =>
                    from tokens in PrimitiveOrVariableRef(escapeChar, createNonQuotedPrimitiveTokenDelegate).Many()
                    where tokens.Any()
                    select createContainerToken(tokens),
                escapeChar);

        private static TContainerToken CreateContainerToken<TContainerToken>(
            Func<IEnumerable<Token>, TContainerToken> createContainerToken,
            IEnumerable<Token> childTokens,
            (string OpeningString, string ClosingString) chars)
            where TContainerToken : QuotableAggregateToken
        {
            TContainerToken container = createContainerToken(childTokens);
            container.QuoteChar = chars.OpeningString[0];
            return container;
        }

        private static CreateTokenParserDelegate<TToken> CreatePrimitiveTokenDelegate<TToken>(
            bool isValueRequired, CreatePrimitiveParserDelegate createPrimitiveParser,
            Func<string, TToken> createPrimitiveToken)
            where TToken : PrimitiveToken =>
            escapeChar =>
                from primitive in createPrimitiveParser(escapeChar)
                where isValueRequired || primitive.Length > 0
                select createPrimitiveToken(primitive);

        private static Parser<Token> PrimitiveOrVariableRef<TToken>(char escapeChar, CreateTokenParserDelegate<TToken> createParser)
            where TToken : PrimitiveToken =>
            VariableReference(escapeChar, createParser)
                .Select(varRef => (Token)varRef)
                .Or(createParser(escapeChar));

        public static Parser<LiteralToken> Literal(char escapeChar) =>
            WrappedInOptionalQuotes<LiteralToken>(
                (char escapeChar, (string OpeningString, string ClosingString) chars) =>
                    from literal in OrConcat(
                        ExceptQuotes(LiteralChar(escapeChar)).Many().Text(),
                        EscapedChar(escapeChar))
                    select new LiteralToken(literal)
                    {
                        QuoteChar = chars.OpeningString[0]
                    },
                escapeChar =>
                    from literal in OrConcat(
                        LiteralChar(escapeChar).AtLeastOnce().Text(),
                        EscapedChar(escapeChar))
                    where !String.IsNullOrEmpty(literal)
                    select new LiteralToken(literal),
                escapeChar);

        public static Parser<char> NonWhitespace() =>
            Parse.AnyChar.Except(Parse.WhiteSpace);

        private static Parser<char> LiteralChar(char escapeChar, bool isWhitespaceAllowed = false) =>
            (isWhitespaceAllowed ? Parse.AnyChar : NonWhitespace())
                .Except(Parse.Char(escapeChar))
                .Except(Parse.Char('$').Then(ch => Parse.LetterOrDigit.Or(Parse.Char('{'))))
                .Except(Parse.Char('}'));

        private static Parser<string> EscapedChar(char escapeChar) =>
            from esc in Parse.Char(escapeChar)
            from v in Parse.AnyChar.AsEnumerable()
                .Except(Parse.LineEnd)
                .Text()
            select esc + v;

        private static Parser<TToken> WrappedInOptionalQuotes<TToken>(CreateWrappedTokenParserDelegate<TToken> createWrappedParser,
            CreateTokenParserDelegate<TToken> nonWrappedParser, char escapeChar)
            where TToken : QuotableToken =>
            WrappedInOptionalCharacters(
                createWrappedParser,
                nonWrappedParser,
                escapeChar,
                (SingleQuote.ToString(), SingleQuote.ToString()),
                (DoubleQuote.ToString(), DoubleQuote.ToString()));

        private static Parser<TToken> AggregateWrappedInOptionalQuotes<TToken>(CreateWrappedTokenParserDelegate<TToken> createWrappedParser,
            CreateTokenParserDelegate<TToken> nonWrappedParser, char escapeChar)
            where TToken : QuotableAggregateToken =>
            WrappedInOptionalCharacters(
                createWrappedParser,
                nonWrappedParser,
                escapeChar,
                (SingleQuote.ToString(), SingleQuote.ToString()),
                (DoubleQuote.ToString(), DoubleQuote.ToString()));

        private static Parser<TToken> WrappedInOptionalCharacters<TToken>(CreateWrappedTokenParserDelegate<TToken> createWrappedParser,
            CreateTokenParserDelegate<TToken> createNonWrappedParser, char escapeChar,
            params (string OpeningString, string ClosingString)[] wrappingChars)
            where TToken : Token =>
            wrappingChars
                .Select(chars =>
                    WrappedInCharacters(
                        createWrappedParser,
                        escapeChar,
                        chars.OpeningString,
                        chars.ClosingString))
                .Aggregate((current, next) => current.Or(next))
            .Or(createNonWrappedParser(escapeChar));

        private static Parser<char> ExceptQuotes(Parser<char> parser) =>
            parser
                .Except(Parse.Char(SingleQuote))
                .Except(Parse.Char(DoubleQuote));

        private static Parser<TToken> WrappedInCharacters<TToken>(CreateWrappedTokenParserDelegate<TToken> createParser,
            char escapeChar, string openingString, string closingString)
            where TToken : Token =>
            from openingQuote in Parse.String(openingString).AsEnumerable()
            from val in createParser(escapeChar, (openingString, closingString))
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
