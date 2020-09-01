using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public class ParserDirective : IDockerfileLine
    {
        public const string EscapeDirective = "escape";

        private ParserDirective(string text)
        {
            Tokens = DockerfileParser.ParserDirectiveParser().Parse(text);
        }

        public IEnumerable<Token> Tokens { get; }

        public KeywordToken Directive => Tokens.OfType<KeywordToken>().First();
        public LiteralToken Value => Tokens.OfType<LiteralToken>().First();
        public LineType Type => LineType.ParserDirective;

        public static bool IsParserDirective(string text) =>
            DockerfileParser.ParserDirectiveParser().TryParse(text).WasSuccessful;

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
