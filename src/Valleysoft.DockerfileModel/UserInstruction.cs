using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class UserInstruction : Instruction
{
    public UserInstruction(string user, string? group = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(user, group, escapeChar))
    {
    }

    private UserInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public string UserAccount
    {
        get => UserAccountToken.Value;
        set
        {
            Requires.NotNullOrEmpty(value, nameof(value));
            UserAccountToken.Value = value;
        }
    }

    public LiteralToken UserAccountToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(UserAccountToken, value);
        }
    }

    public static UserInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<UserInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new UserInstruction(tokens);

    private static IEnumerable<Token> GetTokens(string user, string? group, char escapeChar)
    {
        Requires.NotNullOrEmpty(user, nameof(user));
        string value = String.IsNullOrEmpty(group) ? user : $"{user}:{group}";
        return GetTokens($"USER {value}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("USER", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar);
}
