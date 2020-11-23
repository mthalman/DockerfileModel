using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ParserDirective : DockerfileConstruct
    {
        public const string EscapeDirective = "escape";
        public const string SyntaxDirective = "syntax";

        public ParserDirective(string directive, string value)
            : this(GetTokens(directive, value))
        {
        }

        private ParserDirective(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public string DirectiveName
        {
            get => DirectiveNameToken.Value;
        }

        public KeywordToken DirectiveNameToken => Tokens.OfType<KeywordToken>().First();

        public string DirectiveValue
        {
            get => DirectiveValueToken.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                DirectiveValueToken.Value = value;
            }
        }

        public LiteralToken DirectiveValueToken
        {
            get => Tokens.OfType<LiteralToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(DirectiveValueToken, value);
            }
        }

        public override ConstructType Type => ConstructType.ParserDirective;

        public static ParserDirective Parse(string text) =>
            new ParserDirective(GetTokens(text, GetParser()));

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

        private static IEnumerable<Token> GetTokens(string directive, string value)
        {
            Requires.NotNullOrEmpty(directive, nameof(directive));
            Requires.NotNullOrEmpty(value, nameof(value));
            return GetTokens($"#{directive}={value}", GetParser());
        }

        private static Parser<KeywordToken> DirectiveNameParser() =>
            from name in Sprache.Parse.Identifier(Sprache.Parse.Letter, Sprache.Parse.LetterOrDigit)
            select new KeywordToken(name);

        private static Parser<LiteralToken> DirectiveValueParser() =>
            from val in NonWhitespace().Many().Text()
            select new LiteralToken(val);
    }
}
