using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// An unrecognized instruction retained without interpreting its arguments.
/// Its arguments are opaque, including during variable resolution.
/// </summary>
public sealed class UnknownInstruction : GenericInstruction
{
    internal UnknownInstruction(string leading, KeywordToken name, string arguments, char escapeChar)
        : base(CreateTokens(leading, name, arguments, escapeChar))
    {
    }

    public override string? ResolveVariables(
        char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null) =>
        ToString();

    private static IEnumerable<Token> CreateTokens(
        string leading, KeywordToken name, string arguments, char escapeChar)
    {
        if (leading.Length > 0)
        {
            yield return new StringToken(leading);
        }
        yield return name;
        if (arguments.Length > 0)
        {
            yield return new LiteralToken(new Token[] { new StringToken(arguments) }, false, escapeChar);
        }
    }
}
