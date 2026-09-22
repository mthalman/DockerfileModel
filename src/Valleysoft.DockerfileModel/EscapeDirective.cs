using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>A typed escape directive whose effective value must be a backslash or backtick.</summary>
/// <remarks>Editing this declaration does not reparse existing instructions or change their retained escape contexts.</remarks>
public sealed class EscapeDirective : ParserDirective
{
    public EscapeDirective(char escapeChar) : base(ParserDirective.EscapeDirective, Format(escapeChar))
    {
    }

    internal EscapeDirective(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    /// <summary>Throws if inherited token edits no longer describe a valid escape directive.</summary>
    public char EscapeChar
    {
        get
        {
            string value = GetCurrentValue(ParserDirective.EscapeDirective);
            Guard.Operation(IsValidValue(value), "An escape directive must specify a single backslash or backtick.");
            return value[0];
        }
        set
        {
            RequireName(ParserDirective.EscapeDirective);
            DirectiveValue = Format(value);
        }
    }

    public new static EscapeDirective Parse(string text) =>
        new(GetTokens(text, NamedParser(ParserDirective.EscapeDirective).AtEnd()));

    public new static TextParser<EscapeDirective> GetParser() =>
        from tokens in NamedParser(ParserDirective.EscapeDirective)
        select new EscapeDirective(tokens);

    internal static bool IsValidValue(string value) => value is "\\" or "`";

    private static string Format(char value) => value is '\\' or '`'
        ? value.ToString()
        : throw new ArgumentOutOfRangeException(nameof(value), "An escape character must be a backslash or backtick.");
}
