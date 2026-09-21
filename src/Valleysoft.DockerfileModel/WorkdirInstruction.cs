using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;
using static Valleysoft.DockerfileModel.Parsing.VariableParsers;

namespace Valleysoft.DockerfileModel;

public class WorkdirInstruction : Instruction
{
    public WorkdirInstruction(string path, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(path, escapeChar), escapeChar)
    {
    }

    private WorkdirInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    public string Path
    {
        get => PathToken.Value;
        set
        {
            Guard.NotNullOrEmpty(value, nameof(value));
            PathToken.Value = value;
        }
    }

    public LiteralToken PathToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Guard.NotNull(value, nameof(value));
            SetToken(PathToken, value);
        }
    }

    public static WorkdirInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<WorkdirInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new WorkdirInstruction(tokens, escapeChar);

    internal static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("WORKDIR", escapeChar, GetArgsParser(escapeChar));

    private static IEnumerable<Token> GetTokens(string path, char escapeChar)
    {
        Guard.NotNullOrEmpty(path, nameof(path));
        return GetTokens($"WORKDIR {path}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(LiteralWithVariables(escapeChar, whitespaceMode: WhitespaceMode.Allowed).AsEnumerable(), escapeChar);
}
