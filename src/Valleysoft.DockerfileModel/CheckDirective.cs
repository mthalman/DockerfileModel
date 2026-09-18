using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public sealed class CheckDirective : ParserDirective
{
    public CheckDirective(string value) : base(ParserDirective.CheckDirective, value)
    {
    }

    public CheckDirective(CheckDirectiveOptions options) : this(Format(options))
    {
    }

    internal CheckDirective(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    /// <summary>
    /// Interprets the current value without editing it or executing checks.
    /// Invalid values remain in the model; failure returns a reason and no options.
    /// </summary>
    public bool TryGetOptions(out CheckDirectiveOptions? options, out string? error)
    {
        options = null;
        if (!TryGetCurrentValue(ParserDirective.CheckDirective, out string? value, out error))
        {
            return false;
        }
        return CheckDirectiveOptions.TryParse(value!, out options, out error);
    }

    public new static CheckDirective Parse(string text) =>
        new(GetTokens(text, NamedParser(ParserDirective.CheckDirective).End()));

    public new static Parser<CheckDirective> GetParser() =>
        from tokens in NamedParser(ParserDirective.CheckDirective)
        select new CheckDirective(tokens);

    private static string Format(CheckDirectiveOptions options)
    {
        Guard.NotNull(options, nameof(options));
        // BuildKit can retain semicolons in its third option; canonical reordering would change their meaning.
        if (options.SkippedChecks.Concat(options.ExperimentalChecks).Any(name => name.Contains(';')))
        {
            throw new ArgumentException(
                "Check names containing semicolons cannot be formatted safely. Preserve the original directive value instead.",
                nameof(options));
        }
        return options.Format();
    }
}
