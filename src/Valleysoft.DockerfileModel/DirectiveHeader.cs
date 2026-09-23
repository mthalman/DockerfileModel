using System.Text;

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

        Result<ParserDirective> result = ParserDirective.GetDiagnosticParser().TryParse(line);
        if (!result.HasValue || !ParserDirective.IsSupportedName(result.Value.DirectiveName) ||
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

    public static DirectiveHeader FromItems(IEnumerable<DockerfileConstruct> items)
    {
        DirectiveHeader header = new();
        StringBuilder line = new();
        bool atStart = true;
        foreach (DockerfileConstruct item in items)
        {
            string text = item.ToString();
            if (text.Length == 0)
            {
                continue;
            }
            int start = atStart && text[0] == '\uFEFF' ? 1 : 0;
            atStart = false;
            while (start < text.Length)
            {
                int end = LineEnd(text, start);
                line.Append(text, start, end - start);
                if (text[end - 1] == '\n')
                {
                    ReadLine();
                    if (header.Complete)
                    {
                        return header;
                    }
                }
                start = end;
            }
        }
        if (line.Length > 0)
        {
            ReadLine();
        }
        return header;

        void ReadLine()
        {
            // Model queries retain the last valid state; full-file parsing owns the error diagnostics.
            header.Read(line.ToString(), out _);
            line.Clear();
        }
    }

    internal static int LineEnd(string text, int start)
    {
        int newline = text.IndexOf('\n', start);
        return newline < 0 ? text.Length : newline + 1;
    }
}
