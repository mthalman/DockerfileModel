using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class InstructionParsers
{
    /// <summary>
    /// Tokenizes an argument of an instruction. This handles the parsing of whitespace and line continuations.
    /// </summary>
    /// <param name="tokenParser">Parser for the argument.</param>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="excludeTrailingWhitespace">A value indicating whether trailing whitespace should not be parsed.</param>
    /// <param name="excludeLeadingWhitespace">A value indicating whether leading whitespace should not be parsed.</param>
    /// <returns>Set of tokens.</returns>
    internal static Parser<IEnumerable<Token>> ArgTokens(Parser<IEnumerable<Token>> tokenParser, char escapeChar,
        bool excludeTrailingWhitespace = false, bool excludeLeadingWhitespace = false)
    {
        if (excludeTrailingWhitespace)
        {
            if (excludeLeadingWhitespace)
            {
                return tokenParser;
            }
            else
            {
                return
                    from leadingWhitespace in BasicParsers.Whitespace()
                    from token in tokenParser
                    select TokenSequences.ConcatTokens(leadingWhitespace, token);
            }
        }
        else
        {
            Parser<IEnumerable<Token>> primaryParser;
            if (excludeLeadingWhitespace)
            {
                primaryParser = tokenParser;
            }
            else
            {
                primaryParser =
                    from leadingWhitespace in BasicParsers.Whitespace()
                    from token in tokenParser
                    select TokenSequences.ConcatTokens(leadingWhitespace, token);
            }

            return
                from tokens in primaryParser
                from trailingWhitespace in ArgTrailingWhitespace(escapeChar)
                select TokenSequences.ConcatTokens(tokens, trailingWhitespace);
        }
    }

    internal static Parser<IEnumerable<Token>> ArgTrailingWhitespace(char escapeChar) =>
        // After at least one line continuation, comments (# ...) at the start of the
        // next line are recognized as Dockerfile comments. This matches BuildKit, which
        // only treats # as a comment delimiter at the beginning of a line — including
        // continuation lines within a multi-line instruction. Inline # (not preceded
        // by a newline) is NOT a comment and is treated as regular argument text.
        (from whitespaceBeforeContinuation in BasicParsers.Whitespace()
            from firstContinuation in LineContinuationToken.GetParser(escapeChar)
            from moreContinuations in BasicParsers.LineContinuations(escapeChar)
            from trailingComments in BasicParsers.CommentText().Many()
            select TokenSequences.ConcatTokens(
                whitespaceBeforeContinuation,
                new Token[] { firstContinuation },
                moreContinuations,
                trailingComments.SelectMany(c => c))).Or(
        // Fallback: whitespace and zero-or-more line continuations with no comments.
        // LineContinuations uses .Many() so it succeeds with zero matches,
        // making this branch always succeed and subsume any plain-newline case.
            from trailingWhitespaceOnly in BasicParsers.Whitespace()
            from lineContinuations in BasicParsers.LineContinuations(escapeChar)
            select TokenSequences.ConcatTokens(trailingWhitespaceOnly, lineContinuations));

    /// <summary>
    /// Parses an instruction.
    /// </summary>
    /// <param name="instructionName">Name of the instruction.</param>
    /// <param name="escapeChar">Escape character.</param>
    /// <param name="instructionArgsParser">Parser for the instruction's arguments.</param>
    /// <returns>Set of tokens.</returns>
    internal static Parser<IEnumerable<Token>> Instruction(string instructionName, char escapeChar, Parser<IEnumerable<Token>> instructionArgsParser) =>
        from instructionNameTokens in InstructionNameWithTrailingContent(instructionName, escapeChar)
        from instructionArgs in instructionArgsParser
        select TokenSequences.ConcatTokens(instructionNameTokens, instructionArgs);

    /// <summary>
    /// Parses an instruction with any trailing content.
    /// </summary>
    /// <param name="instructionName">Name of the instruction.</param>
    /// <param name="escapeChar">Escape character.</param>
    private static Parser<IEnumerable<Token>> InstructionNameWithTrailingContent(string instructionName, char escapeChar) =>
        // Comments (# ...) are only recognized after a mandatory line continuation,
        // never directly after the instruction keyword. This prevents "RUN #arg" from
        // incorrectly treating "#arg" as a comment.
        from leading in BasicParsers.Whitespace()
        from instruction in BasicParsers.TokenWithTrailingWhitespace(KeywordToken.GetParser(instructionName, escapeChar))
        from lineContinuationAndComments in (
            from firstContinuation in LineContinuationToken.GetParser(escapeChar)
            from moreContinuations in BasicParsers.LineContinuations(escapeChar)
            from trailingComments in BasicParsers.CommentText().Many()
            select TokenSequences.ConcatTokens(
                new Token[] { firstContinuation },
                moreContinuations,
                trailingComments.SelectMany(c => c))
        ).Optional()
        select TokenSequences.ConcatTokens(leading, instruction, lineContinuationAndComments.GetOrDefault());
}
