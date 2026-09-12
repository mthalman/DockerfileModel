namespace Valleysoft.DockerfileModel;

/// <summary>A half-open range in the original input, including Start and excluding End.</summary>
public readonly struct SourceSpan : IEquatable<SourceSpan>
{
    public SourceSpan(SourcePosition start, SourcePosition end)
    {
        if (start.Line < 1 || start.Column < 1)
        {
            throw new ArgumentException("A valid source position is required.", nameof(start));
        }
        if (end.Line < 1 || end.Column < 1 || end.Offset < start.Offset ||
            end.Line < start.Line || (end.Line == start.Line && end.Column < start.Column))
        {
            throw new ArgumentException("The end must be a valid position at or after the start.", nameof(end));
        }

        Start = start;
        End = end;
    }

    public SourcePosition Start { get; }
    public SourcePosition End { get; }
    public int Length => End.Offset - Start.Offset;

    public bool Equals(SourceSpan other) => Start == other.Start && End == other.End;
    public override bool Equals(object? obj) => obj is SourceSpan other && Equals(other);
    public override int GetHashCode() => unchecked(Start.GetHashCode() * 397 ^ End.GetHashCode());
    public static bool operator ==(SourceSpan left, SourceSpan right) => left.Equals(right);
    public static bool operator !=(SourceSpan left, SourceSpan right) => !left.Equals(right);
}
