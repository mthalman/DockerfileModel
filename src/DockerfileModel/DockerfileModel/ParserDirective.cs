using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ParserDirective : DockerfileConstruct
    {
        public const string EscapeDirective = "escape";

        private ParserDirective(string text)
            : base(text, GetParser())
        {
        }

        public string DirectiveName
        {
            get => Tokens.OfType<KeywordToken>().First().Value;
        }

        public string DirectiveValue
        {
            get => Tokens.OfType<LiteralToken>().First().Value;
            set => Tokens.OfType<LiteralToken>().First().Value = value;
        }

        public override ConstructType Type => ConstructType.ParserDirective;

        public static ParserDirective Create(string directive, string value) =>
            Parse($"#{directive}={value}");

        public static ParserDirective Parse(string text) =>
            new ParserDirective(text);

        public static Parser<IEnumerable<Token>> GetParser() =>
            from leading in Whitespace()
            from commentChar in CommentToken.CommentCharParser()
            from directive in TokenWithTrailingWhitespace(DirectiveNameParser())
            from op in TokenWithTrailingWhitespace(Symbol('='))
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
            from val in NonWhitespace().Many().Text()
            select new LiteralToken(val);
    }
}
