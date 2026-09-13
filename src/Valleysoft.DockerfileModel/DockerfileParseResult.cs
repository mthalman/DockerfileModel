namespace Valleysoft.DockerfileModel;

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
    public IReadOnlyList<DockerfileDiagnostic> Diagnostics { get; }
    public bool Success { get; }
}
