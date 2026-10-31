namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class LiteralResolutionScope : IDisposable
{
    // Synchronous virtual calls inherit lexical context without cloning or modifying the caller's options.
    [ThreadStatic]
    private static LiteralResolutionScope? current;

    private readonly LiteralResolutionScope? previous;
    private readonly LiteralToken literal;
    private readonly ShellQuoteContext context;

    public LiteralResolutionScope(LiteralToken literal, ShellQuoteContext context)
    {
        this.literal = literal;
        this.context = context;
        previous = current;
        current = this;
    }

    public static ShellQuoteContext GetContext(LiteralToken literal) =>
        current?.literal == literal ? current.context : new ShellQuoteContext();

    public void Dispose() => current = previous;
}
