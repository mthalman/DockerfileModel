using System;
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

        public string? Text
        {
            get => TextToken?.Value;
            set
            {
                StringToken? textToken = TextToken;
                if (textToken != null && value is not null)
                {
                    textToken.Value = value;
                }
                else
                {
                    TextToken = value is null ? null : new StringToken(value!);
                }
            }
        }

        public StringToken? TextToken
        {
            get => Tokens.OfType<StringToken>().FirstOrDefault();
            set => SetToken(TextToken, value);
        }

        public static CommentToken Create(string comment) =>
            Parse($"#{comment}");

        public static CommentToken Parse(string text) =>
            new CommentToken(text);

        public static Parser<IEnumerable<Token>> GetParser() =>
            from commentChar in CommentCharParser()
            from text in TokenWithTrailingWhitespace(val => new StringToken(val))
            select ConcatTokens(commentChar, text);

        internal static Parser<IEnumerable<Token>> CommentCharParser() =>
            TokenWithTrailingWhitespace(Symbol('#'));
    }
}
