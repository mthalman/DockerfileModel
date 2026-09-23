using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

/// <summary>A LABEL instruction with ordered, mutable key/value assignments.</summary>
public class LabelInstruction : Instruction
{
    public LabelInstruction(IDictionary<string, string> labels, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(labels, escapeChar), escapeChar)
    {
    }

    private LabelInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        LabelTokens = new Valleysoft.DockerfileModel.Tokens.TokenList<KeyValueToken<LabelKeyToken, LiteralToken>>(this);
        Labels = InstructionCollectionEditing.Pairs(LabelTokens, this);
    }

    /// <summary>Gets the live syntax-aware assignment view, not a detached dictionary.</summary>
    /// <remarks>Pair objects remain connected to their tokens. Structural edits preserve trivia by default and must leave required operands.</remarks>
    public EditableList<IKeyValuePair> Labels { get; }

    /// <summary>Gets the corresponding live assignment token view for syntax-level replacement.</summary>
    public EditableList<KeyValueToken<LabelKeyToken, LiteralToken>> LabelTokens { get; }
   
    public static LabelInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    internal static TextParser<LabelInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new LabelInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(IDictionary<string, string> variables, char escapeChar)
    {
        Guard.NotNullOrEmpty(variables, nameof(variables));

        string[] keyValueAssignments = variables
            .Select(kvp => StringHelper.FormatKeyValueAssignment(kvp.Key, kvp.Value))
            .ToArray();

        return GetTokens($"LABEL {string.Join(" ", keyValueAssignments)}", GetInnerParser(escapeChar));
    }

    private static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Instruction("LABEL", escapeChar, GetArgsParser(escapeChar));

    private static TextParser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        LegacyKeyValueFormat(escapeChar)
            .Try().Or(StandardKeyValueFormat(escapeChar));

    private static TextParser<IEnumerable<Token>> StandardKeyValueFormat(char escapeChar) =>
        ArgTokens(
            from whitespace in Whitespace().Try().OptionalOrDefault(Enumerable.Empty<Token>())
            from variable in KeyValueToken<LabelKeyToken, LiteralToken>.GetParser(
                LabelKeyToken.GetParser(escapeChar),
                LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes),
                escapeChar: escapeChar,
                optionalValue: true).AsEnumerable()
            select ConcatTokens(whitespace, variable), escapeChar
        ).Try().AtLeastOnce().Flatten();

    private static TextParser<IEnumerable<Token>> LegacyKeyValueFormat(char escapeChar) =>
        ArgTokens(
            from whitespace in Whitespace().Try().OptionalOrDefault(Enumerable.Empty<Token>())
            from variable in (
                from key in LabelKeyToken.GetParser(escapeChar)
                from valueWhitespace in Whitespace().Where(ws => ws.Any())
                from value in UnquotedLiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.Allowed)
                select new Token[] { new KeyValueToken<LabelKeyToken, LiteralToken>(ConcatTokens(new Token[] { key }, valueWhitespace, new Token[] { value }), escapeChar) })
            select ConcatTokens(whitespace, variable), escapeChar
        ).Try().AtLeastOnce().Flatten();
}
