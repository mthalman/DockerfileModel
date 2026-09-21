using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;
using static Valleysoft.DockerfileModel.Parsing.VariableParsers;

namespace Valleysoft.DockerfileModel;

public class MaintainerInstruction : Instruction
{
    public MaintainerInstruction(string maintainer, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(maintainer, escapeChar), escapeChar)
    {
    }

    private MaintainerInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    public string Maintainer
    {
        get => MaintainerToken.Value;
        set
        {
            Guard.NotNull(value, nameof(value));
            MaintainerToken.Value = value;
        }
    }

    public LiteralToken MaintainerToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Guard.NotNull(value, nameof(value));
            SetToken(MaintainerToken, value);
        }
    }
   
    public static MaintainerInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    public static Parser<MaintainerInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new MaintainerInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string maintainer, char escapeChar)
    {
        Guard.NotNull(maintainer, nameof(maintainer));
        return GetTokens($"MAINTAINER {(String.IsNullOrEmpty(maintainer) ? "\"\"" : maintainer)}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("MAINTAINER", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(
            LiteralWithVariables(
                escapeChar, whitespaceMode: WhitespaceMode.Allowed).AsEnumerable(), escapeChar);
}
