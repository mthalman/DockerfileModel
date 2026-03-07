using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class OnBuildInstruction : Instruction
{
    public OnBuildInstruction(string triggerInstruction, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(triggerInstruction, escapeChar))
    {
    }

    private OnBuildInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public string TriggerInstruction
    {
        get => TriggerInstructionToken.Value;
        set
        {
            Requires.NotNull(value, nameof(value));
            TriggerInstructionToken.Value = value;
        }
    }

    public LiteralToken TriggerInstructionToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(TriggerInstructionToken, value);
        }
    }

    public static OnBuildInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<OnBuildInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new OnBuildInstruction(tokens);

    private static IEnumerable<Token> GetTokens(string triggerInstruction, char escapeChar)
    {
        Requires.NotNull(triggerInstruction, nameof(triggerInstruction));
        return GetTokens($"ONBUILD {triggerInstruction}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Instruction("ONBUILD", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(
            LiteralTokenWithSpaces(escapeChar).AsEnumerable(), escapeChar);

    // ONBUILD trigger text is opaque — BuildKit does not expand variables in it.
    // The $ character is treated as a regular character, not a variable reference.
    private static Parser<LiteralToken> LiteralTokenWithSpaces(char escapeChar) =>
        from literal in LiteralString(escapeChar, Enumerable.Empty<char>(), excludeVariableRefChars: false)
            .Or(Whitespace()).Or(LineContinuations(escapeChar)).Many().Flatten()
        where literal.Any()
        select new LiteralToken(TokenHelper.CollapseStringTokens(literal), canContainVariables: false, escapeChar);
}
