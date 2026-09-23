using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

public class UserInstruction : Instruction
{
    public UserInstruction(string user, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(user, escapeChar), escapeChar)
    {
    }

    private UserInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    public string User
    {
        get => UserToken.Value;
        set
        {
            Guard.NotNullOrEmpty(value, nameof(value));
            UserToken.Value = value;
        }
    }

    public LiteralToken UserToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Guard.NotNull(value, nameof(value));
            SetToken(UserToken, value);
        }
    }

    public static UserInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    internal static TextParser<UserInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new UserInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string user, char escapeChar)
    {
        Guard.NotNullOrEmpty(user, nameof(user));
        return GetTokens($"USER {user}", GetInnerParser(escapeChar));
    }

    private static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("USER", escapeChar, GetArgsParser(escapeChar));

    private static TextParser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar);
}
