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

    /// <summary>
    /// Parses the trigger instruction for ONBUILD by dispatching to the appropriate
    /// instruction-specific parser. This mirrors BuildKit's parseSubCommand approach
    /// and the Lean formal spec's triggerInstructionParser.
    ///
    /// Using direct parser dispatch instead of AnyChar.Many().Text() + CreateInstruction
    /// ensures that ArgTokens properly handles trailing line continuations and comments
    /// at the ONBUILD level, rather than having them swallowed as raw literal text
    /// inside the inner instruction.
    /// </summary>
    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(
            from instruction in TriggerInstructionParser(escapeChar)
            select new Token[] { instruction },
            escapeChar);

    /// <summary>
    /// Parser that dispatches to the correct instruction-specific parser based on the
    /// instruction keyword. This is the ONBUILD trigger instruction parser -- it
    /// supports all instruction types that are valid as ONBUILD triggers per BuildKit.
    /// Each instruction's own parser handles its internal comment/line-continuation
    /// semantics, while ArgTokens handles trailing comments at the ONBUILD level.
    ///
    /// Excludes FROM, ONBUILD, and MAINTAINER per BuildKit rules (ONBUILD exclusion
    /// also avoids infinite parser recursion).
    /// </summary>
    private static Parser<Instruction> TriggerInstructionParser(char escapeChar) =>
        AddInstruction.GetParser(escapeChar).Cast<AddInstruction, Instruction>()
        .Or(ArgInstruction.GetParser(escapeChar).Cast<ArgInstruction, Instruction>())
        .Or(CmdInstruction.GetParser(escapeChar).Cast<CmdInstruction, Instruction>())
        .Or(CopyInstruction.GetParser(escapeChar).Cast<CopyInstruction, Instruction>())
        .Or(EntrypointInstruction.GetParser(escapeChar).Cast<EntrypointInstruction, Instruction>())
        .Or(ExposeInstruction.GetParser(escapeChar).Cast<ExposeInstruction, Instruction>())
        .Or(EnvInstruction.GetParser(escapeChar).Cast<EnvInstruction, Instruction>())
        .Or(HealthCheckInstruction.GetParser(escapeChar).Cast<HealthCheckInstruction, Instruction>())
        .Or(LabelInstruction.GetParser(escapeChar).Cast<LabelInstruction, Instruction>())
        .Or(RunInstruction.GetParser(escapeChar).Cast<RunInstruction, Instruction>())
        .Or(ShellInstruction.GetParser(escapeChar).Cast<ShellInstruction, Instruction>())
        .Or(StopSignalInstruction.GetParser(escapeChar).Cast<StopSignalInstruction, Instruction>())
        .Or(UserInstruction.GetParser(escapeChar).Cast<UserInstruction, Instruction>())
        .Or(VolumeInstruction.GetParser(escapeChar).Cast<VolumeInstruction, Instruction>())
        .Or(WorkdirInstruction.GetParser(escapeChar).Cast<WorkdirInstruction, Instruction>());
}
