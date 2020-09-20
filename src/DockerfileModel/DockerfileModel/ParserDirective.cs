using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ParserDirective : DockerfileLine
    {
        public const string EscapeDirective = "escape";

        private ParserDirective(string text)
            : base(text, GetParser())
        {
        }

        public KeywordToken DirectiveName => Tokens.OfType<KeywordToken>().First();
        public LiteralToken DirectiveValue => Tokens.OfType<LiteralToken>().First();
        public override LineType Type => LineType.ParserDirective;

        public static ParserDirective Create(string directive, string value) =>
            Parse($"#{directive}={value}");

        public static ParserDirective Parse(string text) =>
            new ParserDirective(text);

        public static Parser<IEnumerable<Token>> GetParser() =>
            from leading in WhitespaceChars().AsEnumerable()
            from commentChar in CommentToken.CommentCharParser()
            from directive in TokenWithTrailingWhitespace(DirectiveNameParser())
            from op in TokenWithTrailingWhitespace(Symbol("="))
            from value in TokenWithTrailingWhitespace(DirectiveValueParser())
            select ConcatTokens(
                leading,
                commentChar,
                directive,
                op,
                value);

        internal static bool IsParserDirective(string text) =>
            GetParser().TryParse(text).WasSuccessful;

        private static Parser<KeywordToken> DirectiveNameParser() =>
            from name in Sprache.Parse.Identifier(Sprache.Parse.Letter, Sprache.Parse.LetterOrDigit)
            select new KeywordToken(name);

        private static Parser<LiteralToken> DirectiveValueParser() =>
            from val in Sprache.Parse.AnyChar.Except(Sprache.Parse.WhiteSpace).Many().Text()
            select new LiteralToken(val);
    }
}
