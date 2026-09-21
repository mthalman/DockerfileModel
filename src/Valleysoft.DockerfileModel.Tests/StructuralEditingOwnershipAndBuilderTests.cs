using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Makes retained trivia aliases and the boundary between construction and editing explicit.
/// </summary>
public class StructuralEditingOwnershipAndBuilderTests
{
    /// <summary>
    /// Preserved comments retain their identity even when an outgoing instruction still references them.
    /// </summary>
    /// <param name="replace">Whether the outgoing instruction is replaced rather than removed.</param>
    /// <param name="newline">The original document's physical line ending.</param>
    [Theory]
    [InlineData(false, "\n")]
    [InlineData(false, "\r\n")]
    [InlineData(true, "\n")]
    [InlineData(true, "\r\n")]
    public void OutgoingInstructionStillSharesItsPromotedComment(bool replace, string newline)
    {
        string outgoingText = $"RUN echo \\{newline}# embedded{newline}hi{newline}";
        var document = Dockerfile.Parse($"FROM alpine{newline}" + outgoingText);
        RunInstruction outgoing = Assert.Single(document.Items.OfType<RunInstruction>());
        CommentToken embedded = Assert.Single(outgoing.CommentTokens);

        if (replace)
        {
            document.Items.ReplaceItem(outgoing, new RunInstruction("echo replacement"));
        }
        else
        {
            Assert.True(document.Items.Remove(outgoing));
        }

        Comment promoted = Assert.Single(document.Items.OfType<Comment>());
        Assert.Same(embedded, promoted.ValueToken);
        Assert.Same(embedded, Assert.Single(outgoing.CommentTokens));
        Assert.Equal(outgoingText, outgoing.ToString());

        Assert.Single(outgoing.CommentTokens).Text = "changed";

        string replacementText = replace ? $"RUN echo replacement{newline}" : "";
        string expected = $"FROM alpine{newline}{replacementText}# changed{newline}";
        Assert.Equal(expected, document.ToString());
        Assert.Equal($"RUN echo \\{newline}# changed{newline}hi{newline}", outgoing.ToString());
        Assert.Same(embedded, promoted.ValueToken);
        Assert.Equal(expected, Dockerfile.Parse(expected).ToString());
    }

    /// <summary>
    /// Preserving a closing terminator shares the original token rather than isolating a retained outgoing pair.
    /// </summary>
    /// <param name="oldNewline">The original closing line ending.</param>
    /// <param name="changedNewline">The line ending written through the outgoing pair's retained token.</param>
    [Theory]
    [InlineData("\n", "\r\n")]
    [InlineData("\r\n", "\n")]
    public void OutgoingHeredocStillSharesItsPreservedClosingTerminator(string oldNewline, string changedNewline)
    {
        DockerfileParseResult parsed = Dockerfile.TryParse("RUN cat <<OLD\nbody\nOLD" + oldNewline);
        Assert.True(parsed.Success);
        RunInstruction run = Assert.Single(parsed.Dockerfile!.Items.OfType<RunInstruction>());
        Heredoc outgoing = Assert.Single(run.Heredocs);
        NewLineToken terminator = Assert.Single(outgoing.Body.Tokens.OfType<NewLineToken>());
        var replacement = new Heredoc("NEXT", "next\n");

        run.Heredocs[0] = replacement;

        Assert.Equal("body\nOLD" + oldNewline, outgoing.Body.ToString());
        Assert.Same(terminator, Assert.Single(replacement.Body.Tokens.OfType<NewLineToken>()));
        terminator.Value = changedNewline;

        Assert.Equal("body\nOLD" + changedNewline, outgoing.Body.ToString());
        Assert.Equal("next\nNEXT" + changedNewline, replacement.Body.ToString());
        Assert.Equal("RUN cat <<NEXT\nnext\nNEXT" + changedNewline, run.ToString());
        Assert.Equal("body\n", outgoing.RawContent);
        Assert.Equal("next\n", replacement.RawContent);
        Assert.Same(replacement, Assert.Single(run.Heredocs));
        DockerfileParseResult reparsed = Dockerfile.TryParse(run.ToString());
        Assert.True(reparsed.Success);
        Assert.Equal(run.ToString(), reparsed.Dockerfile!.ToString());
    }

    /// <summary>
    /// An explicit preceding newline permits builder appends after an edit that preserves unterminated EOF.
    /// </summary>
    /// <param name="newline">The builder's explicitly selected line ending.</param>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void BuilderCanResumeAfterAnExplicitBoundaryFollowingAnEdit(string newline)
    {
        var builder = new DockerfileBuilder { DefaultNewLine = newline };
        builder.FromInstruction("alpine");
        var inserted = new RunInstruction("echo one");

        builder.Dockerfile.Items.Add(inserted);

        Assert.Equal($"FROM alpine{newline}RUN echo one", builder.ToString());
        builder.NewLine().RunInstruction("echo two");

        string expected = $"FROM alpine{newline}RUN echo one{newline}RUN echo two{newline}";
        Assert.Equal(expected, builder.ToString());
        RunInstruction[] live = builder.Dockerfile.Items.OfType<RunInstruction>().ToArray();
        Assert.Equal(2, live.Length);
        Assert.Same(inserted, live[0]);
        DockerfileParseResult reparsed = Dockerfile.TryParse(expected);
        Assert.True(reparsed.Success);
        Assert.Equal(expected, reparsed.Dockerfile!.ToString());
        RunInstruction[] runs = reparsed.Dockerfile.Items.OfType<RunInstruction>().ToArray();
        Assert.Equal(2, runs.Length);
        Assert.Equal("echo one", Assert.IsType<ShellFormCommand>(runs[0].Command).Value);
        Assert.Equal("echo two", Assert.IsType<ShellFormCommand>(runs[1].Command).Value);
    }
}
