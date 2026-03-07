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
        get
        {
            var kvp = PortProtocolToken;
            return kvp?.KeyToken ?? Tokens.OfType<LiteralToken>().First();
        }
        set
        {
            Requires.NotNull(value, nameof(value));
            var kvp = PortProtocolToken;
            if (kvp is not null)
            {
                kvp.KeyToken = value;
            }
            else
            {
                SetToken(Tokens.OfType<LiteralToken>().First(), value);
            }
        }
    }

    public string? Protocol
    {
        get => ProtocolToken?.Value;
        set => SetOptionalLiteralTokenValue(ProtocolToken, value, token => ProtocolToken = token, canContainVariables: true, escapeChar);
    }

    public LiteralToken? ProtocolToken
    {
        get => PortProtocolToken?.ValueToken;
        set
        {
            var kvp = PortProtocolToken;
            if (value is not null)
            {
                if (kvp is not null)
                {
                    // KeyValueToken exists, replace the value token
                    kvp.ValueToken = value;
                }
                else
                {
                    // No KeyValueToken yet - wrap existing port literal with separator and protocol
                    LiteralToken portToken = Tokens.OfType<LiteralToken>().First();
                    var newKvp = new KeyValueToken<LiteralToken, LiteralToken>(
                        ConcatTokens(portToken, new SymbolToken('/'), value));
                    int portIndex = TokenList.IndexOf(portToken);
                    TokenList[portIndex] = newKvp;
                }
            }
            else
            {
                if (kvp is not null)
                {
                    // KeyValueToken exists - unwrap back to a flat port literal
                    LiteralToken portToken = kvp.KeyToken;
                    int kvpIndex = TokenList.IndexOf(kvp);
                    TokenList[kvpIndex] = portToken;
                }
                // else: no KeyValueToken and setting to null - nothing to do
            }
        }
    }

    public static ExposeInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<ExposeInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new ExposeInstruction(tokens, escapeChar);

    private KeyValueToken<LiteralToken, LiteralToken>? PortProtocolToken =>
        Tokens.OfType<KeyValueToken<LiteralToken, LiteralToken>>().FirstOrDefault();

    private static IEnumerable<Token> GetTokens(string port, string? protocol, char escapeChar)
    {
        string protocolSegment = protocol is null ? string.Empty : $"/{protocol}";
        return GetTokens($"EXPOSE {port}{protocolSegment}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("EXPOSE", escapeChar,
            GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        (from port in ArgTokens(LiteralWithVariables(escapeChar, new char[] { '/' }).AsEnumerable(), escapeChar)
        from separator in ArgTokens(Symbol('/').AsEnumerable(), escapeChar)
        from protocol in ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar)
        select ConcatTokens(new KeyValueToken<LiteralToken, LiteralToken>(ConcatTokens(port, separator, protocol))))
        .Or(
            ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar));
}
