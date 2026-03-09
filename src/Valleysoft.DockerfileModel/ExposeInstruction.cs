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

        PortTokens = new TokenList<LiteralToken>(TokenList, FilterPortTokens);
        Ports = new ProjectedItemList<LiteralToken, string>(
            PortTokens,
            token => token.Value,
            (token, value) => token.Value = value);
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
        get => PortTokens.First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(PortTokens.First(), value);
        }
    }

    public IList<string> Ports { get; }

    public IList<LiteralToken> PortTokens { get; }

    public string? Protocol
    {
        get => ProtocolToken?.Value;
        set => SetOptionalLiteralTokenValue(ProtocolToken, value, token => ProtocolToken = token, canContainVariables: true, escapeChar);
    }

    public LiteralToken? ProtocolToken
    {
        get => GetProtocolTokenForPort(PortTokens.First());
        set
        {
            SetToken(ProtocolToken, value,
                addToken: token =>
                {
                    int portIndex = TokenList.IndexOf(PortTokens.First());
                    TokenList.Insert(portIndex + 1, new SymbolToken('/'));
                    TokenList.Insert(portIndex + 2, token);
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
        GetPortSpecParser(escapeChar).AtLeastOnce().Flatten();

    private static Parser<IEnumerable<Token>> GetPortSpecParser(char escapeChar) =>
        from port in ArgTokens(LiteralWithVariables(escapeChar, new char[] { '/' }).AsEnumerable(), escapeChar)
        from protocolTokens in
            (from separator in ArgTokens(Symbol('/').AsEnumerable(), escapeChar)
            from protocol in ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar)
            select ConcatTokens(separator, protocol)).Optional()
        select ConcatTokens(port, protocolTokens.GetOrDefault());

    private IEnumerable<LiteralToken> FilterPortTokens(IEnumerable<LiteralToken> literals)
    {
        // A port token is a LiteralToken that is NOT immediately preceded by a SymbolToken('/')
        foreach (LiteralToken literal in literals)
        {
            int index = TokenList.IndexOf(literal);
            if (index == 0 || !IsSlashSymbol(TokenList, index))
            {
                yield return literal;
            }
        }
    }

    private static bool IsSlashSymbol(List<Token> tokenList, int literalIndex)
    {
        // Walk backwards past any LineContinuationTokens/WhitespaceTokens to find the previous significant token
        for (int i = literalIndex - 1; i >= 0; i--)
        {
            Token prev = tokenList[i];
            if (prev is LineContinuationToken || prev is WhitespaceToken || prev is CommentToken)
            {
                continue;
            }
            return prev is SymbolToken symbolToken && symbolToken.Value == "/";
        }
        return false;
    }

    private LiteralToken? GetProtocolTokenForPort(LiteralToken portToken)
    {
        int portIndex = TokenList.IndexOf(portToken);
        // Look forward past any LineContinuationTokens to find if the next significant token is a '/'
        for (int i = portIndex + 1; i < TokenList.Count; i++)
        {
            Token next = TokenList[i];
            if (next is LineContinuationToken || next is WhitespaceToken || next is CommentToken)
            {
                continue;
            }
            if (next is SymbolToken symbolToken && symbolToken.Value == "/")
            {
                // The token after the '/' (skipping line continuations) is the protocol
                for (int j = i + 1; j < TokenList.Count; j++)
                {
                    Token afterSlash = TokenList[j];
                    if (afterSlash is LineContinuationToken || afterSlash is WhitespaceToken || afterSlash is CommentToken)
                    {
                        continue;
                    }
                    if (afterSlash is LiteralToken literalToken)
                    {
                        return literalToken;
                    }
                    break;
                }
            }
            break;
        }
        return null;
    }
}
