using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class WorkdirInstruction : Instruction
{
    public WorkdirInstruction(string path, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(path, escapeChar))
    {
    }

    private WorkdirInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public string Path
    {
        get => PathToken.Value;
        set
        {
            Requires.NotNullOrEmpty(value, nameof(value));
            PathToken.Value = value;
        }
    }

    public LiteralToken PathToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(PathToken, value);
        }
    }

    public static WorkdirInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<WorkdirInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new WorkdirInstruction(tokens);

    internal static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("WORKDIR", escapeChar, GetArgsParser(escapeChar));

    private static IEnumerable<Token> GetTokens(string path, char escapeChar)
    {
        Requires.NotNullOrEmpty(path, nameof(path));
        return GetTokens($"WORKDIR {path}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.Allowed).AsEnumerable(), escapeChar);
}
