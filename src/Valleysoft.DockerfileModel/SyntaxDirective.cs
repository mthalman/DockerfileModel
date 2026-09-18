using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public sealed class SyntaxDirective : ParserDirective
{
    public SyntaxDirective(string value) : base(ParserDirective.SyntaxDirective, value)
    {
    }

    internal SyntaxDirective(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    /// <summary>
    /// Reads the current declaration without resolving versions or inferring feature support.
    /// Throws if token edits no longer form a single syntax directive.
    /// </summary>
    public DockerfileFrontendMetadata Frontend =>
        DockerfileFrontendMetadata.FromValue(GetCurrentValue(ParserDirective.SyntaxDirective));

    public new static SyntaxDirective Parse(string text) =>
        new(GetTokens(text, NamedParser(ParserDirective.SyntaxDirective).End()));

    public new static Parser<SyntaxDirective> GetParser() =>
        from tokens in NamedParser(ParserDirective.SyntaxDirective)
        select new SyntaxDirective(tokens);
}
