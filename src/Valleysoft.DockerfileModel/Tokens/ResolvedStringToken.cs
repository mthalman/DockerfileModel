namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class ResolvedStringToken : StringToken
{
    // Substitution output may contain syntax quotes that must not become binding contents on a later pass.
    private readonly string syntax;
    private readonly char? quoteChar;
    private readonly bool escaped;
    private readonly char escapeChar;

    public ResolvedStringToken(string syntax, string semanticValue, char? quoteChar, bool escaped, char escapeChar)
        : this(syntax, semanticValue, quoteChar, escaped, escapeChar, syntax)
    {
    }

    private ResolvedStringToken(string syntax, string semanticValue, char? quoteChar, bool escaped, char escapeChar,
        string value) : base(value)
    {
        this.syntax = syntax;
        this.quoteChar = quoteChar;
        this.escaped = escaped;
        this.escapeChar = escapeChar;
        SemanticValue = semanticValue;
    }

    public string SemanticValue { get; }

    internal ResolvedStringToken CreateSnapshot() =>
        new(syntax, SemanticValue, quoteChar, escaped, escapeChar, Value);

    public bool MatchesContext(char? quote, bool isEscaped, char currentEscapeChar) =>
        Value == syntax && quoteChar == quote && escaped == isEscaped && escapeChar == currentEscapeChar;
}
