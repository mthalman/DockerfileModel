using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class ExposeInstruction : Instruction
{
    private readonly char escapeChar;

    public ExposeInstruction(string port, string? protocol = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(port, protocol, escapeChar), escapeChar)
    {
    }

    private ExposeInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
    {
        this.escapeChar = escapeChar;
    }

    public string Port
    {
        get => PortToken.Value;
        set
        {
            Requires.NotNullOrEmpty(value, nameof(value));
            PortToken.Value = value;
        }
    }

    public LiteralToken PortToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(PortToken, value);
        }
    }

    public string? Protocol
    {
        get => ProtocolToken?.Value;
        set => SetOptionalLiteralTokenValue(ProtocolToken, value, token => ProtocolToken = token, canContainVariables: true, escapeChar);
    }

    public LiteralToken? ProtocolToken
    {
        get => Tokens.OfType<LiteralToken>().Skip(1).FirstOrDefault();
        set
        {
            SetToken(ProtocolToken, value,
                addToken: token =>
                {
                    TokenList.Add(new SymbolToken('/'));
                    TokenList.Add(token);
                },
                removeToken: token =>
                {
                    TokenList.RemoveRange(
                        TokenList.FirstPreviousOfType<Token, SymbolToken>(token),
                        token);
                });
        }
    }

    public static ExposeInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<ExposeInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ExposeInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string port, string? protocol, char escapeChar)
    {
        string protocolSegment = protocol is null ? string.Empty : $"/{protocol}";
        return GetTokens($"EXPOSE {port}{protocolSegment}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("EXPOSE", escapeChar,
            GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        from port in ArgTokens(LiteralWithVariables(escapeChar, new char[] { '/' }).AsEnumerable(), escapeChar)
        from protocolTokens in 
            (from separator in ArgTokens(Symbol('/').AsEnumerable(), escapeChar)
            from protocol in ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar)
            select ConcatTokens(separator, protocol)).Optional()
        select ConcatTokens(port, protocolTokens.GetOrDefault());
}
