namespace Valleysoft.DockerfileModel;

internal sealed class DirectiveHeader
{
    private readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

    public bool Complete { get; private set; }
    public char EscapeChar { get; private set; } = Dockerfile.DefaultEscapeChar;
    public string? Syntax { get; private set; }

    public ParserDirective? Read(string line, out string? error)
    {
        error = null;
        if (Complete)
        {
            return null;
        }

        IResult<ParserDirective> result = ParserDirective.GetDiagnosticParser().TryParse(line);
        if (!result.WasSuccessful || !ParserDirective.IsSupportedName(result.Value.DirectiveName) ||
            line.StartsWith("\uFEFF", StringComparison.Ordinal))
        {
            Complete = true;
            return null;
        }

        ParserDirective directive = result.Value;
        if (!seen.Add(directive.DirectiveName))
        {
            error = $"Only one '{directive.DirectiveName.ToLowerInvariant()}' parser directive can be used.";
        }
        else if (directive.HasName(ParserDirective.EscapeDirective))
        {
            if (!EscapeDirective.IsValidValue(directive.DirectiveValue))
            {
                error = "An escape directive must specify a single backslash or backtick.";
            }
            else
            {
                EscapeChar = directive.DirectiveValue[0];
            }
        }

        if (error is not null)
        {
            Complete = true;
            return null;
        }
        if (directive.HasName(ParserDirective.SyntaxDirective))
        {
            Syntax = directive.DirectiveValue;
        }
        return directive;
    }

    public static DirectiveHeader FromText(string text)
    {
        DirectiveHeader header = new();
        int start = text.StartsWith("\uFEFF", StringComparison.Ordinal) ? 1 : 0;
        while (start < text.Length && !header.Complete)
        {
            int end = LineEnd(text, start);
            // Model queries retain the last valid state; full-file parsing owns the error diagnostics.
            header.Read(text.Substring(start, end - start), out _);
            start = end;
        }
        return header;
    }

    internal static int LineEnd(string text, int start)
    {
        int newline = text.IndexOf('\n', start);
        return newline < 0 ? text.Length : newline + 1;
    }
}
