using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Represents a heredoc block in a Dockerfile instruction.
/// A heredoc is opened by &lt;&lt;DELIM (or &lt;&lt;-DELIM, &lt;&lt;"DELIM", &lt;&lt;'DELIM') and closed by DELIM on its own line.
/// Child tokens contain the raw text of the heredoc as string tokens and newline tokens,
/// preserving exact character fidelity for round-trip.
/// </summary>
public class HeredocToken : AggregateToken
{
    internal HeredocToken(IEnumerable<Token> tokens) : base(tokens)
    {
    }
}
