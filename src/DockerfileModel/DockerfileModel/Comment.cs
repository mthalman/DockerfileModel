namespace DockerfileModel
{
    public class Comment : IDockerfileLine
    {
        internal Comment(int lineNumber, string leadingWhitespace, string commentText)
        {
            this.LineNumber = lineNumber;
            this.LeadingWhitespace = leadingWhitespace;
            this.CommentText = commentText;
        }

        public string CommentText { get; }

        public int LineNumber { get; }

        public LineType Type => LineType.Comment;

        public string LeadingWhitespace { get; }
    }
}
