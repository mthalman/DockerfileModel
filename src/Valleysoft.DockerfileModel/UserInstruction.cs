using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class UserInstruction : Instruction
{
    public UserInstruction(string user, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(user, escapeChar))
    {
    }

    private UserInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
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

    public static UserInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<UserInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new UserInstruction(tokens);

    private static IEnumerable<Token> GetTokens(string user, char escapeChar)
    {
        Requires.NotNullOrEmpty(user, nameof(user));
        return GetTokens($"USER {user}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("USER", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar);
}
