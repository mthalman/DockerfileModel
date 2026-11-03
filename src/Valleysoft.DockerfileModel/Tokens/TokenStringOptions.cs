namespace Valleysoft.DockerfileModel.Tokens;

/// <summary>Immutable filters for token serialization; all filters are disabled by default.</summary>
/// <remarks>Filtered output is a value-oriented projection, not fidelity-preserving Dockerfile serialization.</remarks>
public class TokenStringOptions
{
    public TokenStringOptions(bool excludeLineContinuations = false, bool excludeQuotes = false, bool excludeComments = false)
        : this(excludeLineContinuations, excludeQuotes, excludeComments, excludeNewLines: false)
    {
    }

    public TokenStringOptions(bool excludeLineContinuations, bool excludeQuotes, bool excludeComments, bool excludeNewLines)
    {
        this.ExcludeLineContinuations = excludeLineContinuations;
        this.ExcludeQuotes = excludeQuotes;
        this.ExcludeComments = excludeComments;
        this.ExcludeNewLines = excludeNewLines;
    }

    /// <summary>Gets whether aggregate serialization omits line-continuation tokens.</summary>
    public bool ExcludeLineContinuations { get; }
    /// <summary>Gets whether quote wrappers are omitted.</summary>
    public bool ExcludeQuotes { get; }
    /// <summary>Gets whether aggregate serialization omits comment tokens.</summary>
    public bool ExcludeComments { get; }
    /// <summary>Gets whether aggregate serialization omits newline tokens.</summary>
    public bool ExcludeNewLines { get; }

    /// <summary>Creates filters excluding continuations, quotes, comments, and newlines for semantic value access.</summary>
    /// <remarks>This does not perform general shell expansion or JSON unescaping.</remarks>
    public static TokenStringOptions CreateOptionsForValueString() =>
        new(excludeLineContinuations: true, excludeQuotes: true, excludeComments: true, excludeNewLines: true);
}
