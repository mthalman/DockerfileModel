namespace Valleysoft.DockerfileModel;

/// <summary>
/// An original-source position: a zero-based UTF-16 offset and one-based line and column.
/// Tabs and surrogate code units each occupy one column.
/// </summary>
public readonly struct SourcePosition : IEquatable<SourcePosition>
{
    public SourcePosition(int offset, int line, int column)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }
        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        Offset = offset;
        Line = line;
        Column = column;
    }

    public int Offset { get; }
    public int Line { get; }
    public int Column { get; }

    public bool Equals(SourcePosition other) =>
        Offset == other.Offset && Line == other.Line && Column == other.Column;

    public override bool Equals(object? obj) => obj is SourcePosition other && Equals(other);
    public override int GetHashCode() => unchecked((Offset * 397 ^ Line) * 397 ^ Column);
    public static bool operator ==(SourcePosition left, SourcePosition right) => left.Equals(right);
    public static bool operator !=(SourcePosition left, SourcePosition right) => !left.Equals(right);
}
