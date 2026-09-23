using System.Text.RegularExpressions;
using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

/// <summary>A token-preserving parser-directive declaration.</summary>
/// <remarks>
/// Standalone directive syntax does not establish that the declaration is effective in a document.
/// Header placement and duplicate directives matter. Typed wrappers interpret their current tokens;
/// low-level edits may leave declarations that no longer satisfy the typed accessor's requirements.
/// </remarks>
public class ParserDirective : DockerfileConstruct
{
    public const string EscapeDirective = "escape";
    public const string SyntaxDirective = "syntax";
    public const string CheckDirective = "check";

    private static readonly Regex ValuePattern = new(
        @"\A(?<leading>[ \t\f\r]*)(?<value>.+?)(?<trailing>[ \t\f\r]*)\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public ParserDirective(string directive, string value)
        : this(GetTokens(directive, value))
    {
    }

    private protected ParserDirective(IEnumerable<Token> tokens)
        : base(tokens)
    {
    }

    public string DirectiveName
    {
        get => DirectiveNameToken.Value;
    }

    public KeywordToken DirectiveNameToken => Tokens.OfType<KeywordToken>().First();

    /// <summary>Gets or replaces the serialized value text while retaining surrounding directive tokens.</summary>
    /// <remarks>Setting stores raw text and clears a value quote wrapper; it does not validate frontend support or execute checks.</remarks>
    public string DirectiveValue
    {
        get => DirectiveValueToken.ToString();
        set
        {
            Guard.NotNullOrEmpty(value, nameof(value));
            LiteralToken token = DirectiveValueToken;
            token.QuoteChar = null;
            token.ReplaceWithTokens(new Token[] { new StringToken(value) });
        }
    }

    public LiteralToken DirectiveValueToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Guard.NotNull(value, nameof(value));
            SetToken(DirectiveValueToken, value);
        }
    }

    public override ConstructType Type => ConstructType.ParserDirective;

    /// <summary>Parses a standalone directive, returning a typed wrapper for syntax, escape, or check names.</summary>
    /// <remarks>Typed value validity and effective document-header placement are separate from syntax parsing.</remarks>
    public static ParserDirective Parse(string text) =>
        Create(GetTokens(text, GetParser().AtEnd()));

    internal static TextParser<IEnumerable<Token>> GetParser() =>
        from bom in Character.EqualTo('\uFEFF').Try().Optional()
        from leading in HorizontalWhitespace()
        from hash in Symbol('#')
        from afterHash in HorizontalWhitespace()
        from name in NativeParsers.Identifier(
            Character.Matching(IsAsciiLetter, "directive name"),
            Character.Matching(c => IsAsciiLetter(c) || c >= '0' && c <= '9', "directive name"))
        from beforeEquals in Character.In(' ', '\t', '\f', '\r').Try().Many().Text()
        from op in Symbol('=')
        from suffix in Character.Except('\n').Try().Many().Text()
        from newline in Character.EqualTo('\n').Try().Optional()
        let content = suffix.TrimEnd('\r')
        let value = ValuePattern.Match(content)
        where value.Success
        select CreateTokens(bom.HasValue, leading, hash, afterHash, name, beforeEquals, op, value,
            suffix.Substring(content.Length) + (newline.HasValue ? "\n" : ""));

    internal static TextParser<ParserDirective> GetDiagnosticParser() =>
        from tokens in GetParser().AtEnd()
        select Create(tokens);

    internal static bool IsSupportedName(string name) =>
        name.Equals(SyntaxDirective, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(EscapeDirective, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(CheckDirective, StringComparison.OrdinalIgnoreCase);

    internal bool HasName(string name) => DirectiveName.Equals(name, StringComparison.OrdinalIgnoreCase);

    private protected void RequireName(string name) =>
        Guard.Operation(HasName(name), $"The directive name is no longer '{name}'.");

    private protected string GetCurrentValue(string name)
    {
        if (!TryGetCurrentValue(name, out string? value, out string? error))
        {
            throw new InvalidOperationException(error);
        }
        return value!;
    }

    private protected bool TryGetCurrentValue(string name, out string? value, out string? error)
    {
        value = null;
        // Token edits can move trivia into the value or name; interpret the serialized grammar without editing it.
        Result<ParserDirective> result = GetDiagnosticParser().TryParse(ToString());
        if (!result.HasValue)
        {
            error = $"The current tokens do not form a single parser directive: {result.ErrorMessage}";
            return false;
        }
        if (!result.Value.HasName(name))
        {
            error = $"The directive name is no longer '{name}'.";
            return false;
        }
        value = result.Value.DirectiveValue;
        error = null;
        return true;
    }

    private protected static TextParser<IEnumerable<Token>> NamedParser(string name) =>
        from tokens in GetParser()
        where tokens.OfType<KeywordToken>().First().Value.Equals(name, StringComparison.OrdinalIgnoreCase)
        select tokens;

    internal static ParserDirective Create(IEnumerable<Token> tokens)
    {
        Token[] items = tokens.ToArray();
        return items.OfType<KeywordToken>().First().Value.ToLowerInvariant() switch
        {
            SyntaxDirective => new DockerfileModel.SyntaxDirective(items),
            EscapeDirective => new DockerfileModel.EscapeDirective(items),
            CheckDirective => new DockerfileModel.CheckDirective(items),
            _ => new ParserDirective(items)
        };
    }

    private static IEnumerable<Token> GetTokens(string directive, string value)
    {
        Guard.NotNullOrEmpty(directive, nameof(directive));
        Guard.NotNullOrEmpty(value, nameof(value));
        return GetTokens($"#{directive}={value}", GetParser().AtEnd());
    }

    private static bool IsAsciiLetter(char c) => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z';

    private static TextParser<string> HorizontalWhitespace() =>
        Character.Matching(c => char.IsWhiteSpace(c) && c != '\n', "horizontal whitespace").Try().Many().Text();

    private static IEnumerable<Token> CreateTokens(bool bom, string leading, Token hash, string afterHash,
        string name, string beforeEquals, Token op, Match value, string newline)
    {
        List<Token> tokens = new();
        if (bom)
        {
            tokens.Add(new StringToken("\uFEFF"));
        }
        AddWhitespace(leading);
        tokens.Add(hash);
        AddWhitespace(afterHash);
        tokens.Add(new KeywordToken(name));
        AddWhitespace(beforeEquals);
        tokens.Add(op);
        AddWhitespace(value.Groups["leading"].Value);
        tokens.Add(new LiteralToken(new Token[] { new StringToken(value.Groups["value"].Value) },
            canContainVariables: false, Dockerfile.DefaultEscapeChar, preserveRawValue: true));
        AddWhitespace(value.Groups["trailing"].Value);
        if (newline.Length > 0)
        {
            tokens.Add(new NewLineToken(newline));
        }
        return tokens;

        void AddWhitespace(string text)
        {
            if (text.Length > 0)
            {
                tokens.Add(new WhitespaceToken(text));
            }
        }
    }
}
