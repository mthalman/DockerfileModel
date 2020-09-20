using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public class Comment : DockerfileLine
    {
        private Comment(string text)
            : base(text, ParseHelper.CommentText())
        {
        }

        public string Text
        {
            get => Tokens.OfType<CommentToken>().First().Text;
            set => Tokens.OfType<CommentToken>().First().Text = value;
        }

        public override LineType Type => LineType.Comment;

        public static Comment Create(string comment) =>
            new Comment($"# {comment}");

        public static Comment Parse(string text) =>
            new Comment(text);

        public static bool IsComment(string text) =>
            ParseHelper.CommentText().TryParse(text).WasSuccessful;
    }
}
