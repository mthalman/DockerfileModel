using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Delegate for creating a parser of a token.
/// </summary>
/// <param name="escapeChar">The escape character.</param>
/// <param name="excludedChars">Characters to be excluded from parsing.</param>
/// <returns>The token parser.</returns>
public delegate Parser<IEnumerable<Token>> CreateTokenParserDelegate(
    char escapeChar, IEnumerable<char> excludedChars);
