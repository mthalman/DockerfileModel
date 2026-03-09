using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Represents a heredoc block in a Dockerfile instruction.
/// A heredoc is opened by &lt;&lt;DELIM (or &lt;&lt;-DELIM, &lt;&lt;"DELIM", &lt;&lt;'DELIM') and closed by DELIM on its own line.
/// Child tokens preserve exact character fidelity for round-trip. The tokenization structure is:
/// <list type="bullet">
///   <item><description>
///     The opening marker (e.g. <c>&lt;&lt;EOF</c>) is a <see cref="Tokens.StringToken"/>, optionally
///     followed by a second <see cref="Tokens.StringToken"/> for any text that appears after the marker
///     on the same line, and then a <see cref="Tokens.NewLineToken"/>.
///   </description></item>
///   <item><description>
///     Each body line is stored as a single <see cref="Tokens.StringToken"/> whose value
///     already includes the trailing newline character(s); no separate <see cref="Tokens.NewLineToken"/>
///     is emitted for body lines.
///   </description></item>
///   <item><description>
///     The closing delimiter line is a <see cref="Tokens.StringToken"/>, optionally followed
///     by a <see cref="Tokens.NewLineToken"/> when a newline is present after the delimiter.
///   </description></item>
/// </list>
/// </summary>
public class HeredocToken : AggregateToken
{
    internal HeredocToken(IEnumerable<Token> tokens) : base(tokens)
    {
    }
}
