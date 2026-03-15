using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class OnBuildInstruction : Instruction
{
    // Dockerfile only allows ONBUILD to wrap instructions that execute in the child build.
    // FROM starts a new stage, MAINTAINER is deprecated, and ONBUILD would recurse into itself.
    private static readonly HashSet<string> ExcludedTriggerInstructions = new(StringComparer.OrdinalIgnoreCase)
    {
        "FROM",
        "MAINTAINER",
        "ONBUILD",
    };

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
    /// Parses the ONBUILD trigger by validating the nested instruction keyword against
    /// the Dockerfile exclusions and then deferring to <see cref="Instruction.CreateInstruction"/>.
    /// Scanning for the longest valid prefix preserves the existing split between the
    /// inner instruction tokens and any trailing ONBUILD-level continuation comments.
    /// </summary>
    private static Parser<Instruction> TriggerInstructionParser(char escapeChar) =>
        input =>
        {
            IResult<string> instructionNameResult = InstructionNameParser(escapeChar)(input);
            if (!instructionNameResult.WasSuccessful)
            {
                return Result.Failure<Instruction>(input, instructionNameResult.Message, instructionNameResult.Expectations);
            }

            if (ExcludedTriggerInstructions.Contains(instructionNameResult.Value))
            {
                return Result.Failure<Instruction>(
                    input,
                    $"{instructionNameResult.Value} is not a valid ONBUILD trigger instruction.",
                    new[] { "valid ONBUILD trigger instruction" });
            }

            string remainingText = input.Source.Substring(input.Position);
            for (int consumedLength = remainingText.Length; consumedLength > 0; consumedLength--)
            {
                string instructionText = remainingText.Substring(0, consumedLength);
                if (!TryCreateTriggerInstruction(instructionText, escapeChar, out Instruction? instruction) ||
                    !ArgTrailingWhitespace(escapeChar).TryParse(remainingText.Substring(consumedLength)).WasSuccessful)
                {
                    continue;
                }

                return Result.Success(instruction!, AdvanceInput(input, consumedLength));
            }

            return Result.Failure<Instruction>(
                input,
                "Expected a valid ONBUILD trigger instruction.",
                new[] { "valid ONBUILD trigger instruction" });
        };

    private static bool TryCreateTriggerInstruction(string text, char escapeChar, out Instruction? instruction)
    {
        try
        {
            instruction = Instruction.CreateInstruction(text, escapeChar);
            return true;
        }
        catch (ParseException)
        {
            instruction = null;
            return false;
        }
    }

    private static IInput AdvanceInput(IInput input, int count)
    {
        IInput advancedInput = input;
        for (int i = 0; i < count; i++)
        {
            advancedInput = advancedInput.Advance();
        }

        return advancedInput;
    }
}
