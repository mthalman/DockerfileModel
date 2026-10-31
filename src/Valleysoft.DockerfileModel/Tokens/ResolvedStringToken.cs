namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class ResolvedStringToken : StringToken
{
    // Substitution output may contain syntax quotes that must not become binding contents on a later pass.
    private readonly string syntax;
    private bool semanticValueIsValid = true;

    public ResolvedStringToken(string syntax, string semanticValue) : base(syntax)
    {
        this.syntax = syntax;
        SemanticValue = semanticValue;
    }

    public string SemanticValue { get; }

    public bool HasValidSemanticValue => semanticValueIsValid && Value == syntax;

    public void InvalidateSemanticValue() => semanticValueIsValid = false;
}
