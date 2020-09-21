using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    public class Whitespace : DockerfileConstruct
    {
        private Whitespace(string text)
            : base(text, GetParser())
        {
        }

        public WhitespaceToken? Text => Tokens.OfType<WhitespaceToken>().FirstOrDefault();

        public NewLineToken? NewLine => Tokens.OfType<NewLineToken>().FirstOrDefault();

        public override ConstructType Type => ConstructType.Whitespace;

        public static Whitespace Create(string text) =>
            new Whitespace(text);

        public static bool IsWhitespace(string text) =>
            GetParser().TryParse(text).WasSuccessful;

        public static Parser<IEnumerable<Token>> GetParser() =>
            ParseHelper.Whitespace().End();
    }
}
