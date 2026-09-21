namespace Valleysoft.DockerfileModel;

/// <summary>A diagnostic parse result with captured diagnostics and an optional mutable document.</summary>
public sealed class DockerfileParseResult
{
    internal DockerfileParseResult(Dockerfile? dockerfile, IEnumerable<DockerfileDiagnostic> diagnostics)
    {
        Dockerfile = dockerfile;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        Success = !Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Null on strict failure; recovery retains even wholly malformed input.</summary>
    public Dockerfile? Dockerfile { get; }
    /// <summary>Gets immutable diagnostics from the original parse; later document edits do not recompute them.</summary>
    public IReadOnlyList<DockerfileDiagnostic> Diagnostics { get; }
    /// <summary>Gets whether parsing produced no error-severity diagnostics.</summary>
    /// <remarks>A recovered document may be present when this is false; true does not certify Docker build validity.</remarks>
    public bool Success { get; }
}
