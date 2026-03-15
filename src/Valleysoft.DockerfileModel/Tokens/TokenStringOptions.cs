namespace Valleysoft.DockerfileModel.Tokens;

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

    public bool ExcludeLineContinuations { get; }
    public bool ExcludeQuotes { get; }
    public bool ExcludeComments { get; }
    public bool ExcludeNewLines { get; }

    public static TokenStringOptions CreateOptionsForValueString() =>
        new(excludeLineContinuations: true, excludeQuotes: true, excludeComments: true, excludeNewLines: true);
}
