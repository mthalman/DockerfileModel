using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.BasicParsers;
using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;
using static Valleysoft.DockerfileModel.Parsing.TokenSequences;
using static Valleysoft.DockerfileModel.Parsing.VariableParsers;

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
        LabelTokens = new TokenList<KeyValueToken<LabelKeyToken, LiteralToken>>(this);
        Labels = InstructionCollectionEditing.Pairs(LabelTokens, this);
    }

    /// <summary>Gets the live syntax-aware assignment view, not a detached dictionary.</summary>
    /// <remarks>Pair objects remain connected to their tokens. Structural edits preserve trivia by default and must leave required operands.</remarks>
    public EditableList<IKeyValuePair> Labels { get; }

    /// <summary>Gets the corresponding live assignment token view for syntax-level replacement.</summary>
    public EditableList<KeyValueToken<LabelKeyToken, LiteralToken>> LabelTokens { get; }
   
    public static LabelInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<LabelInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
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

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Instruction("LABEL", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        LegacyKeyValueFormat(escapeChar)
            .Or(StandardKeyValueFormat(escapeChar));

    private static Parser<IEnumerable<Token>> StandardKeyValueFormat(char escapeChar) =>
        ArgTokens(
            from whitespace in Whitespace().Optional()
            from variable in KeyValueToken<LabelKeyToken, LiteralToken>.GetParser(
                LabelKeyToken.GetParser(escapeChar),
                LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes),
                escapeChar: escapeChar,
                optionalValue: true).AsEnumerable()
            select ConcatTokens(whitespace.GetOrDefault(), variable), escapeChar
        ).AtLeastOnce().Flatten();

    private static Parser<IEnumerable<Token>> LegacyKeyValueFormat(char escapeChar) =>
        ArgTokens(
            from whitespace in Whitespace().Optional()
            from variable in (
                from key in LabelKeyToken.GetParser(escapeChar)
                from valueWhitespace in Whitespace().Where(ws => ws.Any())
                from value in LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.Allowed)
                select new Token[] { new KeyValueToken<LabelKeyToken, LiteralToken>(ConcatTokens(new Token[] { key }, valueWhitespace, new Token[] { value }), escapeChar) })
            select ConcatTokens(whitespace.GetOrDefault(), variable), escapeChar
        ).AtLeastOnce().Flatten();
}
