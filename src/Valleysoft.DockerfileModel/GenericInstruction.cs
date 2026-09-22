using Valleysoft.DockerfileModel.Tokens;


namespace Valleysoft.DockerfileModel;

public class GenericInstruction : Instruction
{
    public GenericInstruction(string instruction, string args, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(instruction, args, escapeChar), escapeChar)
    {
            
    }

    protected GenericInstruction(IEnumerable<Token> tokens)
        : this(tokens, Dockerfile.DefaultEscapeChar)
    {
    }

    protected GenericInstruction(IEnumerable<Token> tokens, char escapeChar)
        : base(tokens, escapeChar)
    {
        ArgLines = InstructionCollectionEditing.Strings(new Valleysoft.DockerfileModel.Tokens.TokenList<LiteralToken>(this), this);
    }

    public EditableList<string> ArgLines { get; }

    public static GenericInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)), escapeChar);

    private static IEnumerable<Token> GetTokens(string instruction, string args, char escapeChar)
    {
        Guard.NotNullOrEmpty(instruction, nameof(instruction));
        Guard.NotNullOrEmpty(args, nameof(args));
        return GetTokens($"{instruction} {args}", GetInnerParser(escapeChar));
    }

    private static TextParser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        from leading in Whitespace()
        from instruction in TokenWithTrailingWhitespace(InstructionIdentifier(escapeChar))
        from lineContinuation in LineContinuations(escapeChar).Try().OptionalOrDefault(Enumerable.Empty<Token>())
        from instructionArgs in InstructionArgs(escapeChar)
        select ConcatTokens(leading, instruction, lineContinuation, instructionArgs);

    protected static TextParser<IEnumerable<Token>> InstructionArgs(char escapeChar) =>
        from lineSets in (CommentText().Try().Or(InstructionArgLine(escapeChar))).Try().Many()
        select lineSets.SelectMany(lineSet => lineSet);

    private static TextParser<IEnumerable<Token>> InstructionArgLine(char escapeChar) =>
        (
        from text in Superpower.Parse.Not(LineContinuationToken.GetParser(escapeChar))
            .IgnoreThen(Superpower.Parse.Not(NativeParsers.LineEnd))
            .IgnoreThen(Character.AnyChar)
            .Try().Many()
            .Text()
        from lineContinuation in LineContinuations(escapeChar).Try().OptionalOrDefault(Enumerable.Empty<Token>())
        from lineEnd in OptionalNewLine().AsEnumerable()
        select ConcatTokens(
            GetInstructionArgLineContent(text, escapeChar),
            lineContinuation,
            lineEnd)
        ).Where(tokens => tokens.Any());

    private static IEnumerable<Token?> GetInstructionArgLineContent(string text, char escapeChar)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        if (text.Trim().Length == 0)
        {
            yield return new WhitespaceToken(text);
            yield break;
        }

        yield return GetLeadingWhitespaceToken(text);
        yield return new LiteralToken(text.Trim(), canContainVariables: false, escapeChar);
        yield return GetTrailingWhitespaceToken(text);
    }
}
