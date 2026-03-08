using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class LabelInstruction : Instruction
{
    public LabelInstruction(IDictionary<string, string> labels, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(labels, escapeChar))
    {
    }

    private LabelInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
        LabelTokens = new TokenList<KeyValueToken<LiteralToken, LiteralToken>>(TokenList);
        Labels = new ProjectedItemList<KeyValueToken<LiteralToken, LiteralToken>, IKeyValuePair>(
            LabelTokens,
            token => token,
            (token, keyValuePair) =>
            {
                Requires.NotNull(keyValuePair, "value");
                token.Key = keyValuePair.Key;
                token.Value = keyValuePair.Value!;
            });
    }

    public IList<IKeyValuePair> Labels { get; }

    public IList<KeyValueToken<LiteralToken, LiteralToken>> LabelTokens { get; }
   
    public static LabelInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<LabelInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new LabelInstruction(tokens);

    private static IEnumerable<Token> GetTokens(IDictionary<string, string> variables, char escapeChar)
    {
        Requires.NotNullOrEmpty(variables, nameof(variables));

        string[] keyValueAssignments = variables
            .Select(kvp => StringHelper.FormatKeyValueAssignment(kvp.Key, kvp.Value))
            .ToArray();

        return GetTokens($"LABEL {string.Join(" ", keyValueAssignments)}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Instruction("LABEL", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(
            from whitespace in Whitespace().Optional()
            from variable in KeyValueToken<LiteralToken, LiteralToken>.GetParser(
                LiteralWithVariables(escapeChar, excludedChars: new char[] { '=' }, whitespaceMode: WhitespaceMode.AllowedInQuotes),
                LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.AllowedInQuotes),
                escapeChar: escapeChar,
                optionalValue: true).AsEnumerable()
            select ConcatTokens(whitespace.GetOrDefault(), variable), escapeChar
        ).AtLeastOnce().Flatten();
}
