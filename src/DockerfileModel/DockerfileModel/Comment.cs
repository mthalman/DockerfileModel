using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public class Comment : DockerfileLine
    {
        private Comment(string text)
            : base(text, DockerfileParser.CommentText())
        {
        }

        public CommentTextToken Text => Tokens.OfType<CommentTextToken>().First();

        public override LineType Type => LineType.Comment;

        public static Comment Create(string comment) =>
            new Comment($"# {comment}");

        public static Comment CreateFromRawText(string text) =>
            new Comment(text);

        public static bool IsComment(string text) =>
            DockerfileParser.CommentText().TryParse(text).WasSuccessful;
    }
}
