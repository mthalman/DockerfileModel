using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class ExposeInstruction : Instruction
{
    public ExposeInstruction(string portSpec, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(portSpec, escapeChar), escapeChar)
    {
    }

    private ExposeInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
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
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<ExposeInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ExposeInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string portSpec, char escapeChar)
    {
        Requires.NotNullOrEmpty(portSpec, nameof(portSpec));
        return GetTokens($"EXPOSE {portSpec}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("EXPOSE", escapeChar,
            GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar).AtLeastOnce().Flatten();
}
