using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;
using static Valleysoft.DockerfileModel.Parsing.VariableParsers;

namespace Valleysoft.DockerfileModel;

public class ExposeInstruction : Instruction
{
    public ExposeInstruction(string portSpecs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(portSpecs, escapeChar), escapeChar)
    {
    }

    public ExposeInstruction(IEnumerable<string> portSpecs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(portSpecs, escapeChar), escapeChar)
    {
    }

    private ExposeInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
        PortTokens = new TokenList<LiteralToken>(this);
        Ports = InstructionCollectionEditing.Strings(PortTokens, this);
    }

    public EditableList<string> Ports { get; }

    public EditableList<LiteralToken> PortTokens { get; }

    public static ExposeInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    internal static Parser<ExposeInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ExposeInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string portSpecs, char escapeChar)
    {
        Guard.NotNullOrEmpty(portSpecs, nameof(portSpecs));
        return GetTokens($"EXPOSE {portSpecs}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(IEnumerable<string> portSpecs, char escapeChar)
    {
        Guard.NotNullEmptyOrNullElements(portSpecs, nameof(portSpecs));
        return GetTokens(string.Join(" ", portSpecs), escapeChar);
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("EXPOSE", escapeChar,
            GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        // LiteralWithVariables already keeps '/' inside the LiteralToken because '/' is not
        // in the set of excluded characters (whitespace, escape char, variable ref chars).
        // This matches BuildKit, which treats port/protocol specs like "80/tcp" as single
        // opaque literals rather than splitting on '/'.
        ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar).AtLeastOnce().Flatten();
}
