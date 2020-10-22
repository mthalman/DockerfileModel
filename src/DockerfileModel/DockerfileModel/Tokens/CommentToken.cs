using System.Collections.Generic;
using System.Linq;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel.Tokens
{
    public class CommentToken : AggregateToken
    {
        private CommentToken(string text)
            : base(text, GetParser())
        {
        }

        internal CommentToken(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public string Text
        {
            get => this.Tokens.OfType<StringToken>().First().Value;
            set => this.Tokens.OfType<StringToken>().First().Value = value;
        }

        public static CommentToken Parse(string text) =>
            new CommentToken(text);

        public static Parser<IEnumerable<Token>> GetParser() =>
            from commentChar in CommentCharParser()
            from text in TokenWithTrailingWhitespace(val => new StringToken(val))
            select ConcatTokens(commentChar, text);

        internal static Parser<IEnumerable<Token>> CommentCharParser() =>
            TokenWithTrailingWhitespace(Symbol("#"));
    }
}
