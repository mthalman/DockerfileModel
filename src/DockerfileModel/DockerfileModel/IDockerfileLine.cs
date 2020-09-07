namespace DockerfileModel
{
    public interface IDockerfileLine
    {
        LineType Type { get; }
    }

    public enum LineType
    {
        Instruction,
        Comment,
        ParserDirective,
        Whitespace
    }
}
