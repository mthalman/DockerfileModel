using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.InstructionParsers;
using static Valleysoft.DockerfileModel.Parsing.VariableParsers;

namespace Valleysoft.DockerfileModel;

public class StopSignalInstruction : Instruction
{
    public StopSignalInstruction(string signal, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(signal, escapeChar), escapeChar)
    {
    }

    private StopSignalInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    public string Signal
    {
        get => SignalToken.Value;
        set
        {
            Guard.NotNullOrEmpty(value, nameof(value));
            SignalToken.Value = value;
        }
    }

    public LiteralToken SignalToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Guard.NotNull(value, nameof(value));
            SetToken(SignalToken, value);
        }
    }
   
    public static StopSignalInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    [Obsolete("Use Dockerfile.Parse or Dockerfile.TryParse instead. Parser factories will be removed in the next major version.")]
    public static Parser<StopSignalInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new StopSignalInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(string signal, char escapeChar)
    {
        Guard.NotNullOrEmpty(signal, nameof(signal));
        return GetTokens($"STOPSIGNAL {signal}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        Instruction("STOPSIGNAL", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(
            LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar);
}
