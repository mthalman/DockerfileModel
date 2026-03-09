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
            (token, value) =>
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                token.Value = value;
            });
    }

    public IList<string> Ports { get; }

    public IList<LiteralToken> PortTokens { get; }

    /// <summary>
    /// Returns the protocol token for a given port token, or <c>null</c> if the port has no protocol.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="portToken"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="portToken"/> is not a port token in this instruction.</exception>
    public LiteralToken? GetProtocolTokenForPort(LiteralToken portToken)
    {
        if (portToken is null)
        {
            throw new ArgumentNullException(nameof(portToken));
        }
        if (!PortTokens.Contains(portToken))
        {
            throw new ArgumentException("The specified token is not a port token in this instruction.", nameof(portToken));
        }
        return GetProtocolTokenForPortInternal(portToken);
    }

    /// <summary>
    /// Sets or removes the protocol for a given port token.
    /// When <paramref name="protocol"/> is non-null, adds or replaces the <c>/protocol</c> tokens immediately after the port token.
    /// When <paramref name="protocol"/> is null, removes any existing <c>/protocol</c> tokens for the port.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="portToken"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="portToken"/> is not a port token in this instruction.</exception>
    public void SetProtocolForPort(LiteralToken portToken, string? protocol)
    {
        if (portToken is null)
        {
            throw new ArgumentNullException(nameof(portToken));
        }
        if (!PortTokens.Contains(portToken))
        {
            throw new ArgumentException("The specified token is not a port token in this instruction.", nameof(portToken));
        }

        // Treat empty string the same as null: both trigger the remove path.
        if (string.IsNullOrEmpty(protocol))
        {
            protocol = null;
        }

        LiteralToken? existingProtocol = GetProtocolTokenForPortInternal(portToken);

        if (existingProtocol is not null)
        {
            if (protocol is null)
            {
                // Remove the '/' SymbolToken and the protocol LiteralToken
                SymbolToken slashToken = (SymbolToken)TokenList.FirstPreviousOfType<Token, SymbolToken>(existingProtocol);
                TokenList.RemoveRange(slashToken, existingProtocol);
            }
            else
            {
                // Replace the existing protocol value
                existingProtocol.Value = protocol;
            }
        }
        else if (protocol is not null)
        {
            // Insert '/' + protocol immediately after the port token
            int portIndex = TokenList.IndexOf(portToken);
            TokenList.Insert(portIndex + 1, new LiteralToken(protocol, canContainVariables: true, escapeChar));
            TokenList.Insert(portIndex + 1, new SymbolToken('/'));
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
        // A port token is a LiteralToken that is NOT preceded by a SymbolToken('/') as the previous significant token.
        // Non-significant tokens (whitespace, line continuations, and comments) are skipped when searching backwards.
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
        // Walk backwards past any non-significant tokens (line continuations, whitespace, comments) to find the previous significant token
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

    private LiteralToken? GetProtocolTokenForPortInternal(LiteralToken portToken)
    {
        int portIndex = TokenList.IndexOf(portToken);
        // Look forward past any non-significant tokens (line continuations, whitespace, comments) to find if the next significant token is a '/'
        for (int i = portIndex + 1; i < TokenList.Count; i++)
        {
            Token next = TokenList[i];
            if (next is LineContinuationToken || next is WhitespaceToken || next is CommentToken)
            {
                continue;
            }
            if (next is SymbolToken symbolToken && symbolToken.Value == "/")
            {
                // The token after the '/' (skipping non-significant tokens: line continuations, whitespace, comments) is the protocol
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
