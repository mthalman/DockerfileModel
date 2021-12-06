using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class UserInstruction : Instruction
{
    public UserInstruction(string user, string? group = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(new UserAccount(user, group, escapeChar))
    {
    }

    public UserInstruction(UserAccount userAccount, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(userAccount, escapeChar))
    {
    }

    private UserInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public UserAccount UserAccount
    {
        get => Tokens.OfType<UserAccount>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(UserAccount, value);
        }
    }
   
    public static UserInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<UserInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new UserInstruction(tokens);

    private static IEnumerable<Token> GetTokens(UserAccount userAccount, char escapeChar)
    {
        Requires.NotNull(userAccount, nameof(userAccount));
        return GetTokens($"USER {userAccount}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("USER", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(UserAccount.GetParser(escapeChar).AsEnumerable(), escapeChar);
}
