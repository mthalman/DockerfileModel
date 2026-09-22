using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

/// <summary>A JSON-array command whose elements are not split into shell words.</summary>
public class ExecFormCommand : Command
{
    /// <summary>Wraps supplied elements in JSON string syntax and parses the resulting command.</summary>
    /// <remarks>This constructor does not JSON-escape embedded quotes, backslashes, or control characters; supply valid element syntax.</remarks>
    public ExecFormCommand(IEnumerable<string> values, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(values, escapeChar), escapeChar)
    {
    }

    internal ExecFormCommand(IEnumerable<Token> tokens) : this(tokens, Dockerfile.DefaultEscapeChar)
    {
    }

    internal ExecFormCommand(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        ValueTokens = new Valleysoft.DockerfileModel.Tokens.TokenList<LiteralToken>(this);
        Values = InstructionCollectionEditing.Strings(ValueTokens, this);
    }

    private static IEnumerable<Token> GetTokens(IEnumerable<string> values, char escapeChar)
    {
        Guard.NotNull(values, nameof(values));

        return GetTokens(StringHelper.FormatAsJson(values), GetInnerParser(escapeChar));
    }

    /// <summary>Parses JSON command syntax without an instruction keyword, preserving its formatting.</summary>
    public static ExecFormCommand Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    internal static TextParser<ExecFormCommand> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ExecFormCommand(tokens, escapeChar);

    /// <summary>Gets the live editable argument value view.</summary>
    /// <remarks>
    /// New strings supply semantic values, not JSON syntax. Values containing double quotes,
    /// backslashes, or U+0000–U+001F are rejected because JSON escaping is not implemented here.
    /// Use <see cref="ValueTokens"/> to supply valid encoded syntax within the existing operand grammar.
    /// </remarks>
    public EditableList<string> Values { get; }

    /// <summary>Gets the live editable argument token view.</summary>
    /// <remarks>
    /// Inserted and non-self replacement tokens must form one JSON string when double-quoted
    /// and normalized for Dockerfile continuations and physical comments. Valid encoded syntax
    /// is preserved; rejection leaves incoming tokens unchanged. Existing operand grammar checks still apply.
    /// </remarks>
    public EditableList<LiteralToken> ValueTokens { get; }

    public override CommandType CommandType => CommandType.ExecForm;

    private static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        ArgTokens(JsonArray(escapeChar, canContainVariables: false, allowEmpty: true), escapeChar);
}
