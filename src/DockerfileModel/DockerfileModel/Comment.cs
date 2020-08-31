namespace DockerfileModel
{
    public class Comment : IDockerfileLine
    {
        internal Comment(string commentText)
        {
            this.CommentText = commentText;
        }

        public string CommentText { get; }

        public LineType Type => LineType.Comment;
    }
}
