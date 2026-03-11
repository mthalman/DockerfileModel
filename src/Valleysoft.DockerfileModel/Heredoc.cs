using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Semantic wrapper that pairs a <see cref="HeredocMarkerToken"/> with its corresponding
/// <see cref="HeredocBodyToken"/>. Association is positional: the first marker pairs with
/// the first body, the second marker with the second body, etc.
/// </summary>
public class Heredoc
{
    internal Heredoc(HeredocMarkerToken marker, HeredocBodyToken body)
    {
        Marker = marker;
        Body = body;
    }

    /// <summary>
    /// Gets the delimiter name (e.g. "EOF").
    /// </summary>
    public string Name => Marker.DelimiterName;

    /// <summary>
    /// Gets the body content of the heredoc (text between the command line and closing delimiter).
    /// </summary>
    public string Content => Body.Content;

    /// <summary>
    /// True if the heredoc uses <<- (tab-stripping chomp mode).
    /// </summary>
    public bool Chomp => Marker.Chomp;

    /// <summary>
    /// True if the heredoc body should expand variables (i.e. delimiter is NOT quoted).
    /// </summary>
    public bool Expand => !Marker.IsQuoted;

    /// <summary>
    /// Gets the marker token.
    /// </summary>
    internal HeredocMarkerToken Marker { get; }

    /// <summary>
    /// Gets the body token.
    /// </summary>
    internal HeredocBodyToken Body { get; }
}
