using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class UserAccount : AggregateToken
{
    private readonly char escapeChar;

    public UserAccount(string user, string? group = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(user, group, escapeChar), escapeChar)
    {
    }

    internal UserAccount(IEnumerable<Token> tokens, char escapeChar)
        : base(tokens)
    {
        this.escapeChar = escapeChar;
    }

    public string User
    {
        get => UserToken.Value;
        set
        {
            Requires.NotNullOrEmpty(value, nameof(value));
            UserToken.Value = value;
        }
    }

    public LiteralToken UserToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(UserToken, value);
        }
    }

    public string? Group
    {
        get => GroupToken?.Value;
        set => SetOptionalLiteralTokenValue(GroupToken, value, token => GroupToken = token, canContainVariables: true, escapeChar);
    }

    public LiteralToken? GroupToken
    {
        get => Tokens.OfType<LiteralToken>().Skip(1).FirstOrDefault();
        set
        {
            SetToken(GroupToken, value,
                addToken: token =>
                {
                    TokenList.Add(new SymbolToken(':'));
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

    public static UserAccount Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<UserAccount> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new UserAccount(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string user, string? group, char escapeChar)
    {
        Requires.NotNullOrEmpty(user, nameof(user));
        return GetTokens($"{user}{(String.IsNullOrEmpty(group) ? "" : $":{group}")}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        UserAndGroup(escapeChar).Or(ArgTokens(UserParser(escapeChar), escapeChar, excludeTrailingWhitespace: true));

    private static Parser<IEnumerable<Token>> UserAndGroup(char escapeChar) =>
        from user in ArgTokens(UserParser(escapeChar), escapeChar)
        from groupSegment in GroupSegment(escapeChar)
        select ConcatTokens(user, groupSegment);

    private static Parser<IEnumerable<Token>> UserParser(char escapeChar) =>
        LiteralWithVariables(escapeChar, new char[] { ':' }).AsEnumerable();

    private static Parser<IEnumerable<Token>> GroupSegment(char escapeChar) =>
        from colon in ArgTokens(Symbol(':').AsEnumerable(), escapeChar)
        from @group in ArgTokens(
            LiteralWithVariables(escapeChar, Enumerable.Empty<char>()).AsEnumerable(), escapeChar, excludeTrailingWhitespace: true)
        select ConcatTokens(colon, @group);
}
