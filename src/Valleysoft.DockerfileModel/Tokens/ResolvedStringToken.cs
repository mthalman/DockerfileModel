namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class ResolvedStringToken : StringToken
{
    // Substitution output may contain syntax quotes that must not become binding contents on a later pass.
    private readonly string syntax;
    private readonly char? quoteChar;
    private readonly bool escaped;

    public ResolvedStringToken(string syntax, string semanticValue, char? quoteChar, bool escaped) : base(syntax)
    {
        this.syntax = syntax;
        this.quoteChar = quoteChar;
        this.escaped = escaped;
        SemanticValue = semanticValue;
    }

    public string SemanticValue { get; }

    public bool MatchesContext(char? quote, bool isEscaped) =>
        Value == syntax && quoteChar == quote && escaped == isEscaped;
}
