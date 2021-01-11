using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel.Tokens
{
    public class CommentToken : AggregateToken
    {
        public CommentToken(string comment)
            : this(GetTokens($"#{comment}", GetParser()))
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
                if (textToken != null && !String.IsNullOrEmpty(value))
                {
                    textToken.Value = value!;
                }
                else
                {
                    TextToken = String.IsNullOrEmpty(value) ? null : new StringToken(value!);
                }
            }
        }

        public StringToken? TextToken
        {
            get => Tokens.OfType<StringToken>().FirstOrDefault();
            set => SetToken(TextToken, value);
        }

        public static CommentToken Parse(string text) =>
            new CommentToken(GetTokens(text, GetParser()));

        public static Parser<IEnumerable<Token>> GetParser() =>
            from commentChar in CommentCharParser()
            from text in TokenWithTrailingWhitespace(val => new StringToken(val))
            select ConcatTokens(commentChar, text);

        internal static Parser<IEnumerable<Token>> CommentCharParser() =>
            TokenWithTrailingWhitespace(Symbol('#'));
    }
}
