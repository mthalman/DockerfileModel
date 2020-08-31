using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public class ParserDirective : IDockerfileLine
    {
        public const string EscapeDirective = "escape";

        private readonly static Parser<WhitespaceToken> WhitespaceChars =
            from whitespace in Parse.WhiteSpace.Many().Text()
            select whitespace != "" ? new WhitespaceToken(whitespace) : null;

        private readonly static Parser<CommentToken> CommentChar =
            from comment in Parse.String("#").Text()
            select new CommentToken(comment);

        private readonly static Parser<KeywordToken> DirectiveName =
            from name in DockerfileParser.Identifier()
            select new KeywordToken(name);

        private readonly static Parser<OperatorToken> OperatorChar =
            from op in Parse.String("=").Text()
            select new OperatorToken(op);

        private readonly static Parser<LiteralToken> DirectiveValue =
            from val in Parse.AnyChar.Except(Parse.WhiteSpace).Many().Text()
            select new LiteralToken(val);

        private static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Parser<Token> parser) =>
            from token in parser
            from trailingWhitespace in WhitespaceChars
            select Token.ConcatTokens(token, trailingWhitespace);

        private static Parser<IEnumerable<Token>> AsEnumerable(Parser<Token> parser) =>
            from token in parser
            select new Token[] { token };

        private readonly static Parser<IEnumerable<Token>> ParserDirectiveParser =
            from leading in AsEnumerable(WhitespaceChars)
            from commentChar in TokenWithTrailingWhitespace(CommentChar)
            from directive in TokenWithTrailingWhitespace(DirectiveName)
            from op in TokenWithTrailingWhitespace(OperatorChar)
            from value in TokenWithTrailingWhitespace(DirectiveValue)
            select Token.ConcatTokens(
                leading,
                commentChar,
                directive,
                op,
                value);

        private ParserDirective(string text)
        {
            Tokens = ParserDirectiveParser.Parse(text);
        }

        public IEnumerable<Token> Tokens { get; }

        public KeywordToken Directive => Tokens.OfType<KeywordToken>().First();
        public LiteralToken Value => Tokens.OfType<LiteralToken>().First();
        public LineType Type => LineType.ParserDirective;

        public static bool IsParserDirective(string text) =>
            ParserDirectiveParser.TryParse(text).WasSuccessful;

        public static ParserDirective Create(string directive, string value) =>
            CreateFromRawText($"#{directive}={value}");

        public static ParserDirective CreateFromRawText(string text) =>
            new ParserDirective(text);

        public override string ToString() =>
            String.Join("", Tokens
                .Select(token => token.Value)
                .ToArray());
    }
}
