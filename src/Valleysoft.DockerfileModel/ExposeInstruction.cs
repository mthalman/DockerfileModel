using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class ExposeInstruction : Instruction
{
    public ExposeInstruction(string portSpecs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(portSpecs, escapeChar))
    {
    }

    public ExposeInstruction(IEnumerable<string> portSpecs, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(portSpecs, escapeChar))
    {
    }

    private ExposeInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
        PortTokens = new TokenList<LiteralToken>(TokenList);
        Ports = new ProjectedItemList<LiteralToken, string>(
            PortTokens,
            token => token.Value,
            (token, value) =>
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                token.Value = value;
            });
    }

    public IList<string> Ports { get; }

    public IList<LiteralToken> PortTokens { get; }

    public static ExposeInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<ExposeInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ExposeInstruction(tokens);

    private static IEnumerable<Token> GetTokens(string portSpecs, char escapeChar)
    {
        Requires.NotNullOrEmpty(portSpecs, nameof(portSpecs));
        return GetTokens($"EXPOSE {portSpecs}", GetInnerParser(escapeChar));
    }

    private static IEnumerable<Token> GetTokens(IEnumerable<string> portSpecs, char escapeChar)
    {
        Requires.NotNullEmptyOrNullElements(portSpecs, nameof(portSpecs));
        return GetTokens(string.Join(" ", portSpecs), escapeChar);
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("EXPOSE", escapeChar,
            GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(GetPortSpecParser(escapeChar).AsEnumerable(), escapeChar).AtLeastOnce().Flatten();

    private static Parser<LiteralToken> GetPortSpecParser(char escapeChar) =>
        // BuildKit treats EXPOSE port/protocol values as opaque literals, so '/'
        // must stay inside the LiteralToken rather than being parsed separately.
        LiteralWithVariables(escapeChar);
}
