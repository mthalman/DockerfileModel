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

    /// <summary>
    /// Gets or sets the trigger instruction text.
    /// Note: The setter re-parses the value through LiteralToken.Value, which uses
    /// WrappedInOptionalQuotesLiteralStringWithSpaces. This collapses the
    /// StringToken/WhitespaceToken child structure into merged StringTokens.
    /// The parsed token structure (with separate WhitespaceTokens) is only
    /// preserved on the initial parse path, not through mutation.
    /// </summary>
    public string TriggerInstruction
    {
        get => TriggerInstructionToken.Value;
        set
        {
            Requires.NotNullOrEmpty(value, nameof(value));
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
        Requires.NotNullOrEmpty(triggerInstruction, nameof(triggerInstruction));
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
