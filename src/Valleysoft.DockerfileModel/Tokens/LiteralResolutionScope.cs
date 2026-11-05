namespace Valleysoft.DockerfileModel.Tokens;

internal sealed class LiteralResolutionScope : IDisposable
{
    // Execution-context flow preserves delegation across threads without changing the caller's options.
    private static readonly AsyncLocal<LiteralResolutionScope?> current = new();

    private readonly LiteralResolutionScope? previous;
    private readonly AggregateToken token;
    private readonly ShellQuoteContext context;

    public LiteralResolutionScope(AggregateToken token, ShellQuoteContext context, LiteralResolutionPlan? plan = null)
    {
        this.token = token;
        this.context = context;
        Plan = plan;
        previous = current.Value;
        current.Value = this;
    }

    public LiteralResolutionPlan? Plan { get; }

    public static LiteralResolutionScope? GetScope(AggregateToken token) =>
        ReferenceEquals(current.Value?.token, token) ? current.Value : null;

    public static ShellQuoteContext GetContext(AggregateToken token) =>
        GetScope(token)?.context ?? new ShellQuoteContext();

    public void Dispose() => current.Value = previous;
}
