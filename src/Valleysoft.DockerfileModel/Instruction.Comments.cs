using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Represents a Dockerfile instruction and its token-backed syntax.</summary>
public abstract partial class Instruction
{
    /// <summary>Inserts a comment before an existing token at a valid instruction continuation boundary.</summary>
    /// <param name="anchor">A direct semantic child token, or an existing comment belonging to this instruction.</param>
    /// <param name="text">The comment payload without the leading <c>#</c>; an empty string creates an empty comment.</param>
    /// <remarks>
    /// The anchor is selected by identity. Whitespace and continuation tokens cannot be anchors.
    /// Required continuation syntax uses the instruction's escape character and local newline style.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The anchor or text is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The anchor is unsuitable or absent, or the text cannot represent one comment payload.</exception>
    /// <exception cref="InvalidOperationException">The comment cannot be inserted without changing valid instruction boundaries.</exception>
    public void InsertCommentBefore(Token anchor, string text) =>
        InstructionCommentEditing.InsertAnchored(this, anchor, text, after: false);

    /// <summary>Inserts a comment after an existing token at a valid instruction continuation boundary.</summary>
    /// <param name="anchor">A direct semantic child token, or an existing comment belonging to this instruction.</param>
    /// <param name="text">The comment payload without the leading <c>#</c>; an empty string creates an empty comment.</param>
    /// <remarks>
    /// The anchor is selected by identity. The edit must leave a valid following boundary; it cannot
    /// simply append a comment after the instruction's final operand. Existing continuations are reused
    /// where possible.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The anchor or text is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The anchor is unsuitable or absent, or the text cannot represent one comment payload.</exception>
    /// <exception cref="InvalidOperationException">The comment cannot be inserted without changing valid instruction boundaries.</exception>
    public void InsertCommentAfter(Token anchor, string text) =>
        InstructionCommentEditing.InsertAnchored(this, anchor, text, after: true);
}
