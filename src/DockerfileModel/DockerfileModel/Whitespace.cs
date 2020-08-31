using System;

namespace DockerfileModel
{
    public class Whitespace : IDockerfileLine
    {
        public Whitespace(string whitespaceContent)
        {
            this.WhitespaceContent = whitespaceContent ?? throw new ArgumentNullException(nameof(whitespaceContent));
        }

        public string WhitespaceContent { get; }

        public LineType Type => LineType.Whitespace;
    }
}
