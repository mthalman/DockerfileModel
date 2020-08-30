using System;

namespace DockerfileModel
{
    public class Whitespace : IDockerfileLine
    {
        public Whitespace(int lineNumber, string whitespaceContent)
        {
            this.LineNumber = lineNumber;
            this.WhitespaceContent = whitespaceContent ?? throw new ArgumentNullException(nameof(whitespaceContent));
        }

        public string WhitespaceContent { get; }

        public int LineNumber { get; }

        public LineType Type => LineType.Whitespace;

        string IDockerfileLine.LeadingWhitespace => this.WhitespaceContent;
    }
}
