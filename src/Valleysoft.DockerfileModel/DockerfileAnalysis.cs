namespace Valleysoft.DockerfileModel;

/// <summary>
/// A static snapshot, without registry metadata or named build-context configuration.
/// Collections and captured values remain stable; original model handles remain editable.
/// </summary>
public sealed class DockerfileAnalysis
{
    private readonly Dictionary<string, IReadOnlyList<AnalyzedStage>> stagesByName;

    internal DockerfileAnalysis(IEnumerable<AnalyzedStage> stages,
        IEnumerable<DockerfileReference> references, IEnumerable<AnalysisDiagnostic> diagnostics)
    {
        Stages = Array.AsReadOnly(stages.ToArray());
        References = Array.AsReadOnly(references.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        Dependencies = Array.AsReadOnly(References
            .Where(reference => reference.Classification == DockerfileReferenceClassification.Stage).ToArray());
        ExternalImages = Array.AsReadOnly(References
            .Where(reference => reference.Classification == DockerfileReferenceClassification.ExternalImage).ToArray());
        UnresolvedReferences = Array.AsReadOnly(References
            .Where(reference => reference.Classification == DockerfileReferenceClassification.Unresolved).ToArray());
        stagesByName = Stages.Where(stage => stage.Name is not null)
            .GroupBy(stage => stage.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyList<AnalyzedStage>)Array.AsReadOnly(group.ToArray()),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AnalyzedStage> Stages { get; }
    public IReadOnlyList<DockerfileReference> References { get; }
    public IReadOnlyList<DockerfileReference> Dependencies { get; }

    /// <summary>
    /// Effective external-image occurrences, not deduplicated or verified against a registry.
    /// Unresolved and deferred operands are available through References instead.
    /// </summary>
    public IReadOnlyList<DockerfileReference> ExternalImages { get; }

    public IReadOnlyList<DockerfileReference> UnresolvedReferences { get; }
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }

    /// <summary>Gets the captured stage at a zero-based source-order index.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index does not identify a captured stage.</exception>
    public AnalyzedStage GetStage(int index)
    {
        if (index < 0 || index >= Stages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return Stages[index];
    }

    /// <summary>
    /// Requires a unique case-insensitive name match. This lookup does not apply FROM's
    /// distinct binding rules. Use FindStages to inspect duplicate declarations.
    /// </summary>
    public AnalyzedStage GetStage(string name)
    {
        IReadOnlyList<AnalyzedStage> matches = FindStages(name);
        return matches.Count switch
        {
            0 => throw new KeyNotFoundException($"No stage named '{name}' exists."),
            1 => matches[0],
            _ => throw new InvalidOperationException($"Multiple stages are named '{name}'.")
        };
    }

    /// <summary>Gets all captured stages with a case-insensitive matching name, or an empty list if none match.</summary>
    /// <remarks>Duplicate declarations are returned rather than resolved to a single binding.</remarks>
    public IReadOnlyList<AnalyzedStage> FindStages(string name)
    {
        Guard.NotNull(name, nameof(name));
        return stagesByName.TryGetValue(name, out IReadOnlyList<AnalyzedStage>? matches)
            ? matches : Array.AsReadOnly(Array.Empty<AnalyzedStage>());
    }
}
