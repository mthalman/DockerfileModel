using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;

namespace DockerfileModel
{
    public class Comment : DockerfileConstruct
    {
        private Comment(string text)
            : base(text, ParseHelper.CommentText())
        {
        }

        public string? Text
        {
            get => TextToken.Text;
            set => TextToken.Text = value;
        }

        public CommentToken TextToken
        {
            get => Tokens.OfType<CommentToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(TextToken, value);
            }
        }

        public override ConstructType Type => ConstructType.Comment;

        public static Comment Create(string comment) =>
            new Comment($"# {comment}");

        public static Comment Parse(string text) =>
            new Comment(text);

        public static bool IsComment(string text) =>
            ParseHelper.CommentText().TryParse(text).WasSuccessful;
    }
}
