using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Validation;

namespace Valleysoft.DockerfileModel
{
    public class Comment : DockerfileConstruct
    {
        public Comment(string comment)
            : this(GetTokens(comment))
        {
        }

        private Comment(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public string? Value
        {
            get => ValueToken.Text;
            set => ValueToken.Text = value;
        }

        public CommentToken ValueToken
        {
            get => Tokens.OfType<CommentToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(ValueToken, value);
            }
        }

        public override ConstructType Type => ConstructType.Comment;

        public static Comment Parse(string text)
        {
            Requires.NotNullOrEmpty(text, nameof(text));
            return new Comment(GetTokens(text, ParseHelper.CommentText()));
        }

        private static IEnumerable<Token> GetTokens(string comment)
        {
            Requires.NotNullOrEmpty(comment, nameof(comment));
            return GetTokens($"#{comment}", ParseHelper.CommentText());
        }

        public static bool IsComment(string text)
        {
            Requires.NotNullOrEmpty(text, nameof(text));
            return ParseHelper.CommentText().TryParse(text).WasSuccessful;
        }
    }
}
