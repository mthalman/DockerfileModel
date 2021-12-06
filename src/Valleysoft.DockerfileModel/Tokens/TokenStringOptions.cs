namespace Valleysoft.DockerfileModel.Tokens;

public class TokenStringOptions
{
    public TokenStringOptions(bool excludeLineContinuations = false, bool excludeQuotes = false, bool excludeComments = false)
    {
        this.ExcludeLineContinuations = excludeLineContinuations;
        this.ExcludeQuotes = excludeQuotes;
        this.ExcludeComments = excludeComments;
    }

    public bool ExcludeLineContinuations { get; }
    public bool ExcludeQuotes { get; }
    public bool ExcludeComments { get; }

    public static TokenStringOptions CreateOptionsForValueString() =>
        new(true, true, true);
}
