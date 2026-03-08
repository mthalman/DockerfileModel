using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Backward-compatible wrapper for secret mount specifications.
/// Use <see cref="Mount"/> directly for new code.
/// </summary>
[Obsolete("Use Mount instead.")]
public class SecretMount : Mount
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SecretMount"/> class.
    /// </summary>
    /// <param name="tokens">The tokens that make up the mount specification.</param>
    protected internal SecretMount(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    /// <summary>
    /// Parses a secret mount specification from text.
    /// </summary>
    /// <param name="text">The mount specification text to parse.</param>
    /// <param name="escapeChar">The escape character.</param>
    /// <returns>A <see cref="SecretMount"/> instance.</returns>
    public static new SecretMount Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar)
    {
        Mount mount = Mount.Parse(text, escapeChar);
        return new SecretMount(mount.Tokens);
    }
}
