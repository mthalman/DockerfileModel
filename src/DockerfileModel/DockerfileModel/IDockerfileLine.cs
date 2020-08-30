namespace DockerfileModel
{
    public interface IDockerfileLine
    {
        int LineNumber { get; }
        LineType Type { get; }
        string LeadingWhitespace { get; }
    }

    public enum LineType
    {
        Instruction,
        Comment,
        ParserDirective,
        Whitespace
    }
}
