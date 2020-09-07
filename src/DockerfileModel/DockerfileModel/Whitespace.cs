using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public class Whitespace : DockerfileLine
    {
        private Whitespace(string text)
            : base(text, DockerfileParser.Whitespace())
        {
        }

        public WhitespaceToken? Text => Tokens.OfType<WhitespaceToken>().FirstOrDefault();

        public NewLineToken? NewLine => Tokens.OfType<NewLineToken>().FirstOrDefault();

        public override LineType Type => LineType.Whitespace;

        public static Whitespace Create(string text) =>
            new Whitespace(text);

        public static bool IsWhitespace(string text) =>
            DockerfileParser.Whitespace().TryParse(text).WasSuccessful;
    }
}
