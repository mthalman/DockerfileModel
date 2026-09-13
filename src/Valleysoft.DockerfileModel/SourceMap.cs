namespace Valleysoft.DockerfileModel;

internal sealed class SourceMap
{
    private readonly List<int> lineStarts = new() { 0 };

    public SourceMap(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lineStarts.Add(i + 1);
            }
        }
    }

    public SourceSpan GetSpan(int start, int end) => new(GetPosition(start), GetPosition(end));

    private SourcePosition GetPosition(int offset)
    {
        int lineIndex = lineStarts.BinarySearch(offset);
        if (lineIndex < 0)
        {
            lineIndex = ~lineIndex - 1;
        }
        return new SourcePosition(offset, lineIndex + 1, offset - lineStarts[lineIndex] + 1);
    }
}
