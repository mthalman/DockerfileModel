using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>A token-backed document element, such as an instruction, directive, comment, or whitespace.</summary>
/// <remarks>Constructs are mutable reference objects; editing a shared construct changes every model that holds it.</remarks>
public abstract class DockerfileConstruct : AggregateToken
{
    protected DockerfileConstruct(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public abstract ConstructType Type { get; }

    /// <summary>
    /// Original-input provenance from a full Dockerfile parse, or null for standalone/constructed items.
    /// Editing or moving this construct does not update its original source span.
    /// </summary>
    public SourceSpan? SourceSpan { get; internal set; }
}
