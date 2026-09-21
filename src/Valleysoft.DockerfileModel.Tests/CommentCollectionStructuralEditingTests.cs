using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Protects comment editing's continuation boundaries, live views, and operand identity.
/// </summary>
public class CommentCollectionStructuralEditingTests
{
    /// <summary>
    /// Verifies that concrete and compatibility views share edits without replacing instruction operands.
    /// </summary>
    /// <param name="newline">The owner's physical line separator.</param>
    /// <param name="escape">The escape character required for inserted continuations.</param>
    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '`')]
    public void CommentsCreateHeaderBoundaryAndStayLive(string newline, char escape)
    {
        FromInstruction instruction = FromInstruction.Parse($"FROM alpine AS build{newline}", escape);
        ICommentable compatibility = instruction;
        EditableList<string?> comments = instruction.Comments;
        Token image = instruction.ImageNameToken;
        comments.Add("first");
        compatibility.Comments.Add("second");
        comments.Insert(1, "middle");
        Assert.Equal(new[] { "first", "middle", "second" }, instruction.CommentTokens.Select(comment => comment.Text));
        Assert.Contains($"{escape}{newline}#first{newline}", instruction.ToString());
        comments.Move(2, 0);
        Assert.Equal(new[] { "second", "first", "middle" }, comments);
        comments.Remove("first");
        Assert.Same(image, instruction.ImageNameToken);
        Assert.Equal("alpine", instruction.ImageName);
        Assert.Equal("build", instruction.StageName);
        Assert.Equal(instruction.ToString(), FromInstruction.Parse(instruction.ToString(), escape).ToString());
        comments.Clear();
        Assert.Empty(compatibility.CommentTokens);
        Assert.Same(image, instruction.ImageNameToken);
    }

    /// <summary>
    /// Verifies typed-token adoption and payload replacement retain the newline separating a comment from its operand.
    /// </summary>
    [Fact]
    public void TypedCommentsAdoptTokensAndReplacePayloadWithoutLosingNewline()
    {
        ExposeInstruction instruction = ExposeInstruction.Parse("EXPOSE \\\n#old\n80");
        CommentToken inserted = new("new");
        instruction.CommentTokens.Replace(0, inserted);
        Assert.Same(inserted, instruction.CommentTokens.Single());
        Assert.Equal("EXPOSE \\\n#new\n80", instruction.ToString());
        instruction.Comments[0] = "changed";
        Assert.Equal("EXPOSE \\\n#changed\n80", instruction.ToString());
        Assert.Throws<InvalidOperationException>(() => instruction.CommentTokens.Add(instruction.CommentTokens[0]));
    }

    /// <summary>
    /// Covers empty-comment normalization and rejection of multiline payloads without partial edits.
    /// </summary>
    [Fact]
    public void EmptyCommentsAndInvalidValuesAreHandledAtomically()
    {
        ExposeInstruction instruction = ExposeInstruction.Parse("EXPOSE 80");
        instruction.Comments.Add(null);
        instruction.Comments.Add("");
        Assert.Equal(2, instruction.Comments.Count);
        Assert.All(instruction.Comments, comment => Assert.Null(comment));
        string before = instruction.ToString();
        Assert.Throws<ArgumentException>(() => instruction.Comments.Add("bad\nline"));
        Assert.Equal(before, instruction.ToString());
    }

    /// <summary>
    /// Verifies comment anchors preserve operand identity and reject tokens outside the instruction.
    /// </summary>
    [Fact]
    public void AnchoredCommentsPreserveOperands()
    {
        ExposeInstruction instruction = ExposeInstruction.Parse("EXPOSE 80 443");
        LiteralToken port = instruction.PortTokens[1];
        instruction.InsertCommentBefore(port, "before");
        instruction.InsertCommentAfter(instruction.InstructionNameToken, "header");
        Assert.Equal(new[] { "header", "before" }, instruction.Comments);
        Assert.Same(port, instruction.PortTokens[1]);
        Assert.Equal(new[] { "80", "443" }, ExposeInstruction.Parse(instruction.ToString()).Ports);
        string before = instruction.ToString();
        Assert.Throws<ArgumentException>(() => instruction.InsertCommentBefore(new LiteralToken("90"), "foreign"));
        Assert.Equal(before, instruction.ToString());
    }

    /// <summary>
    /// Guards against applying the first newline style to edits in a later, differently formatted continuation.
    /// </summary>
    [Fact]
    public void NewCommentsUseTheNearestContinuationNewline()
    {
        ExposeInstruction instruction = ExposeInstruction.Parse(
            "EXPOSE \\\n#first\n80 \\\r\n#last\r\n443");
        LineContinuationToken[] continuations = instruction.Tokens.OfType<LineContinuationToken>().ToArray();

        instruction.Comments.Add("added");
        Assert.Contains("#last\r\n#added\r\n443", instruction.ToString());
        instruction.InsertCommentBefore(instruction.PortTokens[1], "near");

        Assert.Contains("#added\r\n#near\r\n443", instruction.ToString());
        Assert.Contains("#first\n", instruction.ToString());
        Assert.Equal(continuations, instruction.Tokens.OfType<LineContinuationToken>());
        Assert.Equal(new[] { "80", "443" }, ExposeInstruction.Parse(instruction.ToString()).Ports);
    }
}
