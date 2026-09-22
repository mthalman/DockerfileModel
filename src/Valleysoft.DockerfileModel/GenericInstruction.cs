using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Parsing.BasicParsers;
using static Valleysoft.DockerfileModel.Parsing.TokenSequences;

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
        ArgLines = InstructionCollectionEditing.Strings(new TokenList<LiteralToken>(this), this);
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

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
        from leading in Whitespace()
        from instruction in TokenWithTrailingWhitespace(InstructionIdentifier(escapeChar))
        from lineContinuation in LineContinuations(escapeChar).Optional()
        from instructionArgs in InstructionArgs(escapeChar)
        select ConcatTokens(leading, instruction, lineContinuation.GetOrDefault(), instructionArgs);

    protected static Parser<IEnumerable<Token>> InstructionArgs(char escapeChar) =>
        from lineSets in (CommentText().Or(InstructionArgLine(escapeChar))).Many()
        select lineSets.SelectMany(lineSet => lineSet);

    private static Parser<IEnumerable<Token>> InstructionArgLine(char escapeChar) =>
        from text in Valleysoft.DockerfileModel.Parsing.Parse.AnyChar.Except(LineContinuationToken.GetParser(escapeChar)).Except(Valleysoft.DockerfileModel.Parsing.Parse.LineEnd).Many().Text()
        from lineContinuation in LineContinuations(escapeChar).Optional()
        from lineEnd in OptionalNewLine().AsEnumerable()
        select ConcatTokens(
            GetInstructionArgLineContent(text, escapeChar),
            lineContinuation.GetOrDefault(),
            lineEnd);

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
