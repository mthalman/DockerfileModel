namespace Valleysoft.DockerfileModel.Tokens;

/// <summary>A syntax element whose serialized text can include formatting absent from its semantic value.</summary>
public abstract class Token
{
    /// <summary>Serializes all of the token's current syntax, including quotes, comments, and continuations.</summary>
    public sealed override string ToString()
    {
        return ToString(new TokenStringOptions());
    }

    /// <summary>Serializes the token with selected syntax omitted.</summary>
    /// <param name="options">Filtering options; the token itself is not changed.</param>
    /// <returns>Filtered text, which need not be valid Dockerfile syntax or an exact round trip.</returns>
    public string ToString(TokenStringOptions options)
    {
        Guard.NotNull(options, nameof(options));

        string value = GetUnderlyingValue(options);

        if (!options.ExcludeQuotes && this is IQuotableToken quotableToken)
        {
            return $"{quotableToken.QuoteChar}{value}{quotableToken.QuoteChar}";
        }

        return value;
    }

    protected abstract string GetUnderlyingValue(TokenStringOptions options);
}
