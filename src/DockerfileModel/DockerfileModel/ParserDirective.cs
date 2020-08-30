namespace DockerfileModel
{
    public class ParserDirective : IDockerfileLine
    {
        public const string EscapeDirective = "escape";

        internal ParserDirective(int lineNumber, string leadingWhitespace, string directive, string value)
        {
            this.LineNumber = lineNumber;
            this.LeadingWhitespace = leadingWhitespace;
            this.Directive = directive;
            this.Value = value;
        }

        public string Directive { get; }
        public string Value { get; }

        public int LineNumber { get; }

        public LineType Type => LineType.ParserDirective;

        public string LeadingWhitespace { get; }
    }
}
