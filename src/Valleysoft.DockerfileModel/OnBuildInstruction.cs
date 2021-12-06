using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class OnBuildInstruction : Instruction
{
    public OnBuildInstruction(Instruction instruction, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(instruction, escapeChar))
    {
    }

    private OnBuildInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public Instruction Instruction
    {
        get => Tokens.OfType<Instruction>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(Instruction, value);
        }
    }
   
    public static OnBuildInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<OnBuildInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new OnBuildInstruction(tokens);

    private static IEnumerable<Token> GetTokens(Instruction instruction, char escapeChar)
    {
        Requires.NotNull(instruction, nameof(instruction));
        return GetTokens($"ONBUILD {instruction}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Instruction("ONBUILD", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(
            from text in Sprache.Parse.AnyChar.Many().Text()
            select new Token[] { CreateInstruction(text, escapeChar) },
            escapeChar);
}
