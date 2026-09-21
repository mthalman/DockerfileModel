namespace Valleysoft.DockerfileModel;

/// <summary>Specifies how a newly constructed heredoc's opening delimiter is quoted.</summary>
/// <remarks>
/// <see cref="Heredoc.Expand"/> is Dockerfile parser metadata, not a guarantee of expansion
/// by the selected shell. Backslash escaping in constructed unquoted names retains true;
/// shells such as Bash can interpret escaped delimiters differently.
/// </remarks>
public enum HeredocQuoteKind
{
    /// <summary>
    /// Omits surrounding quotes and reports <see cref="Heredoc.Expand"/> as true,
    /// including when delimiter characters require backslash escapes.
    /// </summary>
    Unquoted = 0,

    /// <summary>Uses single quotes and reports <see cref="Heredoc.Expand"/> as false. The name cannot contain a single quote.</summary>
    SingleQuoted = 1,

    /// <summary>Uses double quotes and reports <see cref="Heredoc.Expand"/> as false, escaping characters as required.</summary>
    DoubleQuoted = 2
}
