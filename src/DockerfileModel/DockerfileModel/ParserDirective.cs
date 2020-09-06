using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public class ParserDirective : DockerfileLine
    {
        public const string EscapeDirective = "escape";

        private ParserDirective(string text)
            : base(text, DockerfileParser.ParserDirectiveParser())
        {
        }

        public KeywordToken Directive => Tokens.OfType<KeywordToken>().First();
        public LiteralToken Value => Tokens.OfType<LiteralToken>().First();
        public override LineType Type => LineType.ParserDirective;

        public static bool IsParserDirective(string text) =>
            DockerfileParser.ParserDirectiveParser().TryParse(text).WasSuccessful;

        public static ParserDirective Create(string directive, string value) =>
            Parse($"#{directive}={value}");

        public static ParserDirective Parse(string text) =>
            new ParserDirective(text);
    }
}
