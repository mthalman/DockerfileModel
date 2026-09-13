namespace Valleysoft.DockerfileModel;

/// <summary>
/// Captured stage metadata. Source and FromInstruction retain mutable model objects;
/// analyze again after editing them to obtain updated facts.
/// </summary>
public sealed class AnalyzedStage
{
    internal AnalyzedStage(Stage source, int index)
    {
        Source = source;
        FromInstruction = source.FromInstruction;
        Index = index;
        Name = source.Name;
    }

    public int Index { get; }
    public string? Name { get; }
    public Stage Source { get; }
    public FromInstruction FromInstruction { get; }
}
