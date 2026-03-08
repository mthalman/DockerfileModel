using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel;

public class OnBuildInstruction : Instruction
{
    public OnBuildInstruction(string triggerInstruction, char escapeChar = Dockerfile.DefaultEscapeChar)
        : this(GetTokens(triggerInstruction, escapeChar))
    {
    }

    private OnBuildInstruction(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public string TriggerInstruction
    {
        get => TriggerInstructionToken.Value;
        set
        {
            Requires.NotNullOrWhiteSpace(value, nameof(value));
            TriggerInstructionToken.Value = value;
        }
    }

    public LiteralToken TriggerInstructionToken
    {
        get => Tokens.OfType<LiteralToken>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(TriggerInstructionToken, value);
        }
    }

    public static OnBuildInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
        new(GetTokens(text, GetInnerParser(escapeChar)));

    public static Parser<OnBuildInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        from tokens in GetInnerParser(escapeChar)
        select new OnBuildInstruction(tokens);

    private static IEnumerable<Token> GetTokens(string triggerInstruction, char escapeChar)
    {
        Requires.NotNullOrWhiteSpace(triggerInstruction, nameof(triggerInstruction));
        return GetTokens($"ONBUILD {triggerInstruction}", GetInnerParser(escapeChar));
    }

    private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
        Instruction("ONBUILD", escapeChar, GetArgsParser(escapeChar));

    private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
        ArgTokens(
            LiteralTokenWithSpaces(escapeChar).AsEnumerable(), escapeChar);

    // ONBUILD trigger text is opaque — BuildKit does not expand variables in it.
    // The $ character is treated as a regular character, not a variable reference.
    // After a line continuation, any comment lines (# ...) are parsed as CommentTokens
    // so they don't leak into the trigger string value (LiteralToken.Value excludes comments).
    // Leading whitespace before comments is absorbed into the CommentToken so it doesn't
    // leak into Value while preserving round-trip fidelity.
    private static Parser<LiteralToken> LiteralTokenWithSpaces(char escapeChar) =>
        from literal in LiteralString(escapeChar, Enumerable.Empty<char>(), excludeVariableRefChars: false)
            .Or(Spaces())
            .Or(from lc in LineContinuations(escapeChar)
                from comments in CommentText().Many()
                // Absorb leading WhitespaceTokens from each comment line into the
                // CommentToken that follows them.  CommentText() returns leading
                // whitespace as separate WhitespaceToken siblings outside the
                // CommentToken, and LiteralToken.Value only excludes
                // CommentToken/LineContinuationToken — so without this, indentation
                // before '#' on comment-only continuation lines would leak into
                // TriggerInstruction.
                select ConcatTokens(lc, AbsorbLeadingWhitespaceIntoComments(comments.Flatten())))
            .Many().Flatten()
        where literal.Any()
        select new LiteralToken(TokenHelper.CollapseStringTokens(literal), canContainVariables: false, escapeChar);

    /// <summary>
    /// Absorbs leading <see cref="WhitespaceToken"/>s into the <see cref="CommentToken"/>
    /// that follows them.  This keeps the whitespace in the token tree (preserving
    /// round-trip fidelity) while ensuring it is excluded from
    /// <see cref="LiteralToken.Value"/> via the normal <c>ExcludeComments</c> filter.
    /// </summary>
    private static IEnumerable<Token> AbsorbLeadingWhitespaceIntoComments(IEnumerable<Token> tokens)
    {
        List<Token> pending = new();
        foreach (Token token in tokens)
        {
            if (token is CommentToken comment && pending.Count > 0)
            {
                // Wrap pending whitespace + original comment children into a new CommentToken.
                pending.AddRange(comment.Tokens);
                yield return new CommentToken(pending);
                pending = new();
            }
            else if (token is WhitespaceToken)
            {
                pending.Add(token);
            }
            else
            {
                // Flush any pending tokens that weren't followed by a CommentToken.
                foreach (Token p in pending)
                    yield return p;
                pending.Clear();
                yield return token;
            }
        }
        // Flush remaining tokens.
        foreach (Token p in pending)
            yield return p;
    }
}
