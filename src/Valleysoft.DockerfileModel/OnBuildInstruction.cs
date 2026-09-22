using Valleysoft.DockerfileModel.Tokens;


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
        : this(GetTokens(instruction, escapeChar), escapeChar)
    {
    }

    private OnBuildInstruction(IEnumerable<Token> tokens, char escapeChar) : base(tokens, escapeChar)
    {
    }

    public Instruction Instruction
    {
        get => Tokens.OfType<Instruction>().First();
        set
        {
            Guard.NotNull(value, nameof(value));
            SetToken(Instruction, value);
        }
    }

    public static OnBuildInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    internal static TextParser<OnBuildInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new OnBuildInstruction(tokens, escapeChar);

    private static IEnumerable<Token> GetTokens(Instruction instruction, char escapeChar)
    {
        Guard.NotNull(instruction, nameof(instruction));
        return GetTokens($"ONBUILD {instruction}", GetInnerParser(escapeChar));
    }

    internal static OnBuildInstruction ParseDiagnostic(string text, char escapeChar, InstructionParseContext context) =>
        new(GetTokens(text, GetInnerParser(escapeChar, context)), escapeChar);

    private static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar,
        InstructionParseContext? context = null) =>
        Instruction("ONBUILD", escapeChar, GetArgsParser(escapeChar, context));

    /// <summary>
    /// Parses the trigger instruction for ONBUILD by dispatching to the appropriate
    /// instruction-specific parser. This mirrors BuildKit's parseSubCommand approach
    /// and the Lean formal spec's triggerInstructionParser.
    ///
    /// Using direct parser dispatch instead of AnyChar.Try().Many().Text() + CreateInstruction
    /// ensures that ArgTokens properly handles trailing line continuations and comments
    /// at the ONBUILD level, rather than having them swallowed as raw literal text
    /// inside the inner instruction.
    /// </summary>
    private static TextParser<IEnumerable<Token>> GetArgsParser(char escapeChar, InstructionParseContext? context) =>
        ArgTokens(
            from instruction in TriggerInstructionParser(escapeChar, context)
            select (IEnumerable<Token>)new Token[] { instruction },
            escapeChar);

    /// <summary>
    /// Parses the ONBUILD trigger by validating the nested instruction keyword against
    /// the Dockerfile exclusions and then deferring to <see cref="Instruction.CreateInstruction"/>.
    /// Scanning for the longest valid prefix preserves the existing split between the
    /// inner instruction tokens and any trailing ONBUILD-level continuation comments.
    /// </summary>
    private static TextParser<Instruction> TriggerInstructionParser(char escapeChar, InstructionParseContext? context) =>
        input =>
        {
            Result<string> instructionNameResult = InstructionNameParser(escapeChar)(input);
            if (!instructionNameResult.HasValue)
            {
                return Result.CastEmpty<string, Instruction>(instructionNameResult);
            }

            if (ExcludedTriggerInstructions.Contains(instructionNameResult.Value))
            {
                return Result.Empty<Instruction>(input, "valid ONBUILD trigger instruction");
            }

            int inputOffset = input.Position.Absolute;
            string remainingText = input.Source!.Substring(inputOffset);
            for (int consumedLength = remainingText.Length; consumedLength > 0; consumedLength--)
            {
                string instructionText = remainingText.Substring(0, consumedLength);
                if (!TryCreateTriggerInstruction(instructionText, escapeChar, context?.Slice(inputOffset), out Instruction? instruction) ||
                    !ArgTrailingWhitespace(escapeChar).TryParse(remainingText.Substring(consumedLength)).HasValue)
                {
                    continue;
                }

                return Result.Value(instruction!, input, input.Skip(consumedLength));
            }

            return Result.Empty<Instruction>(input, "valid ONBUILD trigger instruction");
        };

    private static bool TryCreateTriggerInstruction(string text, char escapeChar,
        InstructionParseContext? context, out Instruction? instruction)
    {
        try
        {
            instruction = context is null
                ? Instruction.CreateInstruction(text, escapeChar)
                : Instruction.CreateDiagnosticInstruction(InstructionNameParser(escapeChar).Parse(text), text, escapeChar, context);
            return true;
        }
        catch (ParseException)
        {
            instruction = null;
            return false;
        }
    }

}
