using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Protects document-level collection semantics, local formatting, and atomic rejection of unsafe header edits.
/// </summary>
public class DocumentStructuralEditingTests
{
    /// <summary>
    /// Verifies insertion creates only the necessary separator while retaining neighboring construct instances.
    /// </summary>
    /// <param name="newline">The document's existing line separator.</param>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void InsertRepairsOnlyNewBoundary(string newline)
    {
        Dockerfile file = Dockerfile.Parse($"FROM alpine{newline}RUN echo old{newline}");
        var first = file.Items[0];
        var last = file.Items[1];
        var inserted = new WorkdirInstruction("/app");

        file.Items.Insert(file.Items.IndexOf(first) + 1, inserted);

        Assert.Equal($"FROM alpine{newline}WORKDIR /app{newline}RUN echo old{newline}", file.ToString());
        Assert.Same(first, file.Items[0]);
        Assert.Same(inserted, file.Items[1]);
        Assert.Same(last, file.Items[2]);
    }

    /// <summary>
    /// Ensures writes through the standard list interface receive structural boundary repair.
    /// </summary>
    [Fact]
    public void IListWritesUseSameBoundaryRules()
    {
        Dockerfile file = new();
        IList<DockerfileConstruct> items = file.Items;
        items.Add(new FromInstruction("alpine"));
        items.Add(new RunInstruction("echo hi"));

        Assert.Equal("FROM alpine\nRUN echo hi", file.ToString());
        Assert.False(items.IsReadOnly);
        Assert.False(items.Remove(new RunInstruction("echo absent")));
    }

    /// <summary>
    /// Checks that indexed moves reorder existing objects rather than reparsing replacements.
    /// </summary>
    [Fact]
    public void MoveUsesFinalIndexAndKeepsObjects()
    {
        Dockerfile file = Dockerfile.Parse("FROM alpine\nRUN first\nRUN second\n");
        DockerfileConstruct first = file.Items[1];
        file.Items.Move(1, 2);
        Assert.Equal("FROM alpine\nRUN second\nRUN first\n", file.ToString());
        Assert.Same(first, file.Items[2]);
        file.Items.Move(file.Items.IndexOf(first), 1);
        Assert.Equal("FROM alpine\nRUN first\nRUN second\n", file.ToString());
        file.Items.Move(file.Items.IndexOf(first), 2);
        Assert.Equal("FROM alpine\nRUN second\nRUN first\n", file.ToString());
    }

    /// <summary>
    /// Fixes the move destination contract as an index in the resulting list, including no-op moves.
    /// </summary>
    /// <param name="oldIndex">The selected item's original index.</param>
    /// <param name="newIndex">The selected item's final index.</param>
    /// <param name="expected">The exact document after the move.</param>
    [Theory]
    [InlineData(0, 2, "RUN b\nRUN c\nRUN a\n")]
    [InlineData(2, 0, "RUN c\nRUN a\nRUN b\n")]
    [InlineData(1, 0, "RUN b\nRUN a\nRUN c\n")]
    [InlineData(1, 2, "RUN a\nRUN c\nRUN b\n")]
    [InlineData(1, 1, "RUN a\nRUN b\nRUN c\n")]
    public void MoveIndicesDescribeFinalOrder(int oldIndex, int newIndex, string expected)
    {
        Dockerfile file = Dockerfile.Parse("RUN a\nRUN b\nRUN c\n");
        file.Items.Move(oldIndex, newIndex);
        Assert.Equal(expected, file.ToString());
    }

    /// <summary>
    /// Ensures previously obtained read-only views reflect edits and copied entries retain reference identity.
    /// </summary>
    [Fact]
    public void CollectionReadsAreLiveAndCopyToKeepsIdentity()
    {
        Dockerfile file = Dockerfile.Parse("FROM alpine\n");
        IReadOnlyList<DockerfileConstruct> readOnly = file.Items;
        var run = new RunInstruction("echo hi");
        file.Items.Add(run);
        var copy = new DockerfileConstruct[3];
        file.Items.CopyTo(copy, 1);
        Assert.Equal(2, readOnly.Count);
        Assert.Equal(1, file.Items.IndexOf(run));
        Assert.Same(run, readOnly[1]);
        Assert.Same(run, copy[2]);
        Assert.Contains(run, file.Items);
    }

    /// <summary>
    /// Verifies replacement adopts the incoming construct while keeping the selected boundary's indentation and suffix.
    /// </summary>
    [Fact]
    public void ReplacePreservesOuterFormatting()
    {
        Dockerfile file = Dockerfile.Parse("FROM alpine\r\n\tRUN echo old  \r\n");
        DockerfileConstruct replacement = new WorkdirInstruction("/app");
        file.Items.Replace(1, replacement);
        Assert.Equal("FROM alpine\r\n\tWORKDIR /app  \r\n", file.ToString());
        Assert.Same(replacement, file.Items[1]);
    }

    /// <summary>
    /// Prevents promoted comments from displacing the replacement from the index assigned by the caller.
    /// </summary>
    [Fact]
    public void IndexAssignmentKeepsReplacementAtSelectedIndexWhenPromotingComments()
    {
        Dockerfile file = Dockerfile.Parse("FROM alpine\nRUN echo \\\n# retained\nold\n");
        var replacement = new RunInstruction("echo new");
        file.Items[1] = replacement;
        Assert.Same(replacement, file.Items[1]);
        Assert.Equal("FROM alpine\nRUN echo new\n# retained\n", file.ToString());
    }

    /// <summary>
    /// Distinguishes removing the final instruction from clearing the collection when embedded comments survive.
    /// </summary>
    /// <param name="prefix">An optional byte-order mark preceding the instruction.</param>
    /// <param name="expected">The promoted trivia that must remain in the document.</param>
    [Theory]
    [InlineData("", "# retained\n")]
    [InlineData("\uFEFF", "\uFEFF# retained\n")]
    public void RemovingTheLastInstructionCanLeavePromotedTrivia(string prefix, string expected)
    {
        Dockerfile file = Dockerfile.Parse(prefix + "RUN echo \\\n# retained\nold\n");
        Assert.Single(file.Items);
        file.Items.RemoveAt(0);
        Assert.IsType<Comment>(Assert.Single(file.Items));
        Assert.Equal(expected, file.ToString());
    }

    /// <summary>
    /// Verifies the byte-order mark remains document trivia rather than being discarded with its former instruction.
    /// </summary>
    [Fact]
    public void RemovingTheLastInstructionPreservesAnIndependentBom()
    {
        Dockerfile file = Dockerfile.Parse("\uFEFFFROM alpine\n");
        file.Items.RemoveAt(0);
        Assert.IsType<Whitespace>(Assert.Single(file.Items));
        Assert.Equal("\uFEFF", file.ToString());
        Assert.Equal(file.ToString(), Dockerfile.Parse(file.ToString()).ToString());
    }

    /// <summary>
    /// Ensures inserted separators follow the edited region instead of the document's first newline.
    /// </summary>
    /// <param name="firstNewline">The newline style at the start of the document.</param>
    /// <param name="localNewline">The newline style surrounding the insertion.</param>
    /// <param name="finalNewline">Whether the original document has a final newline.</param>
    [Theory]
    [InlineData("\n", "\n", false)]
    [InlineData("\n", "\n", true)]
    [InlineData("\r\n", "\r\n", false)]
    [InlineData("\r\n", "\r\n", true)]
    [InlineData("\n", "\r\n", false)]
    [InlineData("\n", "\r\n", true)]
    [InlineData("\r\n", "\n", false)]
    [InlineData("\r\n", "\n", true)]
    public void InsertionUsesNearbyDocumentNewlines(string firstNewline, string localNewline, bool finalNewline)
    {
        string ending = finalNewline ? localNewline : "";
        Dockerfile file = Dockerfile.Parse(
            $"FROM alpine{firstNewline}RUN first{localNewline}RUN second{ending}");
        DockerfileConstruct[] original = file.Items.ToArray();
        var inserted = new WorkdirInstruction("/app");

        file.Items.Insert(2, inserted);

        string expected = $"FROM alpine{firstNewline}RUN first{localNewline}WORKDIR /app{localNewline}RUN second{ending}";
        Assert.Equal(expected, file.ToString());
        Assert.Equal(new[] { original[0], original[1], inserted, original[2] }, file.Items);
        Dockerfile reparsed = Dockerfile.Parse(file.ToString());
        Assert.Equal(expected, reparsed.ToString());
        Assert.Collection(reparsed.Items,
            item => Assert.IsType<FromInstruction>(item),
            item => Assert.IsType<RunInstruction>(item),
            item => Assert.IsType<WorkdirInstruction>(item),
            item => Assert.IsType<RunInstruction>(item));
    }

    /// <summary>
    /// Keeps heredoc payload newlines from determining formatting for a new document boundary.
    /// </summary>
    [Theory]
    [InlineData("\n", "\r\n")]
    [InlineData("\r\n", "\n")]
    public void AppendingInfersHeaderBoundaryRatherThanRawHeredocBodyNewlines(string headerNewline, string bodyNewline)
    {
        string original = $"FROM alpine{bodyNewline}RUN <<EOF{headerNewline}raw{bodyNewline}EOF";
        Dockerfile file = Dockerfile.Parse(original);
        RunInstruction run = Assert.Single(file.Items.OfType<RunInstruction>());

        file.Items.Add(new WorkdirInstruction("/app"));

        string expected = original + $"{headerNewline}WORKDIR /app";
        Assert.Equal(expected, file.ToString());
        Assert.Same(run, file.Items[1]);
        Assert.Equal($"raw{bodyNewline}", Assert.Single(run.HeredocBodyTokens).Content);
        Dockerfile reparsed = Dockerfile.Parse(file.ToString());
        Assert.Equal(expected, reparsed.ToString());
        Assert.Collection(reparsed.Items,
            item => Assert.IsType<FromInstruction>(item),
            item => Assert.IsType<RunInstruction>(item),
            item => Assert.IsType<WorkdirInstruction>(item));
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", false)]
    [InlineData("\r\n", true)]
    public void AppendRepairsOnlyMissingFinalBoundary(string newline, bool finalNewline)
    {
        string original = $"FROM alpine{newline}RUN echo old" + (finalNewline ? newline : "");
        Dockerfile file = Dockerfile.Parse(original);
        var inserted = new WorkdirInstruction("/app");

        file.Items.Add(inserted);

        string expected = $"FROM alpine{newline}RUN echo old{newline}WORKDIR /app";
        Assert.Equal(expected, file.ToString());
        Assert.Same(inserted, file.Items.Last());
        Dockerfile reparsed = Dockerfile.Parse(file.ToString());
        Assert.Equal(expected, reparsed.ToString());
        Assert.Equal(3, reparsed.Items.Count);
        Assert.IsType<WorkdirInstruction>(reparsed.Items.Last());
    }

    /// <summary>
    /// Verifies removal retains surrounding trivia and promotes embedded comments using the original comment token.
    /// </summary>
    [Fact]
    public void RemovePreservesStandaloneTriviaAndPromotesEmbeddedComment()
    {
        Dockerfile file = Dockerfile.Parse("FROM alpine\n\n# before\nRUN echo \\\n  # embedded\n  hi\n# after\n");
        RunInstruction run = file.Items.OfType<RunInstruction>().Single();
        CommentToken embedded = run.CommentTokens.Single();

        Assert.True(file.Items.Remove(run));

        Assert.Equal("FROM alpine\n\n# before\n  # embedded\n# after\n", file.ToString());
        Assert.Contains(file.Items.OfType<Comment>(), comment => ReferenceEquals(comment.ValueToken, embedded));
    }

    /// <summary>
    /// Limits explicit trivia discard to the selected construct rather than adjacent standalone comments.
    /// </summary>
    [Fact]
    public void DiscardRemovesOnlySelectedInstructionTrivia()
    {
        Dockerfile file = Dockerfile.Parse("FROM alpine\n# before\nRUN echo \\\n# embedded\nhi\n# after\n");
        file.Items.Remove(file.Items.OfType<RunInstruction>().Single(), TriviaDisposition.Discard);
        Assert.Equal("FROM alpine\n# before\n# after\n", file.ToString());
    }

    /// <summary>
    /// Requires clear either to leave no entries or to reject without changing text or construct identities.
    /// </summary>
    [Fact]
    public void ClearIsAtomicAndActuallyEmptiesCollection()
    {
        Dockerfile file = Dockerfile.Parse("FROM alpine\nRUN echo \\\n# embedded\nhi\n");
        string before = file.ToString();
        DockerfileConstruct[] objects = file.Items.ToArray();
        Assert.Throws<InvalidOperationException>(() => file.Items.Clear());
        Assert.Equal(before, file.ToString());
        Assert.Equal(objects, file.Items);
        file.Items.Clear(TriviaDisposition.Discard);
        Assert.Empty(file.Items);
        Assert.Equal("", file.ToString());
    }

    /// <summary>
    /// Distinguishes explicitly selected trivia from incidental trivia that preservation would otherwise retain.
    /// </summary>
    [Fact]
    public void SelectedCommentsAndBlankLinesCanBeCleared()
    {
        Dockerfile file = Dockerfile.Parse("# selected\n\n");
        file.Items.Clear();
        Assert.Empty(file.Items);
    }

    /// <summary>
    /// Rejects an insertion that would silently reinterpret an active parser directive as an ordinary comment.
    /// </summary>
    [Fact]
    public void DirectiveDemotionFailsBeforeMutation()
    {
        Dockerfile file = Dockerfile.Parse("# syntax=docker/dockerfile:1\nFROM alpine\n");
        string before = file.ToString();
        DockerfileConstruct[] objects = file.Items.ToArray();
        Assert.Throws<InvalidOperationException>(() => file.Items.Insert(0, new Comment(" closes header")));
        Assert.Equal(before, file.ToString());
        Assert.Equal(objects, file.Items);
    }

    /// <summary>
    /// Prevents header insertion from changing the escape context of an existing instruction body.
    /// </summary>
    [Fact]
    public void EscapeChangeWithBodyFailsBeforeMutation()
    {
        Dockerfile file = Dockerfile.Parse("FROM alpine\n");
        Assert.Throws<InvalidOperationException>(() => file.Items.Insert(0, new EscapeDirective('`')));
        Assert.Equal("FROM alpine\n", file.ToString());
    }

    /// <summary>
    /// Allows a recovered duplicate directive to be removed without rejecting the remaining valid header.
    /// </summary>
    [Fact]
    public void ExistingMalformedDirectiveCanBeRemoved()
    {
        Dockerfile file = Dockerfile.TryParse(
            "# syntax=docker/dockerfile:1\n# syntax=duplicate\nFROM alpine\n",
            new DockerfileParseOptions { Mode = DockerfileParseMode.Recover }).Dockerfile!;
        DockerfileConstruct duplicate = file.Items.Single(item => item.ToString().Contains("syntax=duplicate"));
        Assert.True(file.Items.Remove(duplicate, TriviaDisposition.Discard));
        Assert.Equal("# syntax=docker/dockerfile:1\nFROM alpine\n", file.ToString());
    }

    /// <summary>
    /// Ensures policy and index validation occurs before any document mutation.
    /// </summary>
    [Fact]
    public void InvalidPolicyAndIndicesDoNotChangeDocument()
    {
        Dockerfile file = Dockerfile.Parse("FROM alpine\n");
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Items.Clear((TriviaDisposition)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Items.RemoveAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.Items.Move(0, -1));
        Assert.Equal("FROM alpine\n", file.ToString());
    }

    /// <summary>
    /// Keeps the byte-order mark at the document start across moves and requires explicit discard to clear it.
    /// </summary>
    [Fact]
    public void BomRemainsAtStartWhenFirstInstructionMoves()
    {
        Dockerfile file = Dockerfile.Parse("\uFEFFFROM alpine\nRUN echo hi\n");
        file.Items.Move(0, 1);
        Assert.Equal("\uFEFFRUN echo hi\nFROM alpine\n", file.ToString());
        Assert.Throws<InvalidOperationException>(() => file.Items.Clear());
        file.Items.Clear(TriviaDisposition.Discard);
        Assert.Empty(file.Items);
    }

    /// <summary>
    /// Allows header and body removal as one operation and restores the empty document's default escape context.
    /// </summary>
    [Fact]
    public void FullClearCanRemoveAnEscapeDirectiveAndItsBodyTogether()
    {
        Dockerfile file = Dockerfile.Parse("# escape=`\nFROM alpine\nRUN echo hi\n");
        file.Items.Clear(TriviaDisposition.Discard);
        Assert.Empty(file.Items);
        Assert.Equal(Dockerfile.DefaultEscapeChar, file.EscapeChar);
    }

    /// <summary>
    /// Requires replacement targets to identify exactly one entry even when the same object appears more than once.
    /// </summary>
    [Fact]
    public void ReplacementRejectsMissingAndDuplicateValues()
    {
        var from = new FromInstruction("alpine");
        Dockerfile file = new(new[] { from, from });
        Assert.Throws<InvalidOperationException>(() => file.Items.ReplaceItem(from, new RunInstruction("x")));
        Assert.Throws<ArgumentException>(() => file.Items.ReplaceItem(new RunInstruction("missing"), from));
        Assert.Equal(2, file.Items.Count);
    }

    /// <summary>
    /// Rejects incompatible descendants even when the incoming instruction itself has the document's escape context.
    /// </summary>
    /// <param name="replace">Whether to replace an existing item rather than append the incoming instruction.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DocumentAdoptionRejectsNestedEscapeMismatch(bool replace)
    {
        var file = Dockerfile.Parse("# escape=`\nFROM alpine\n");
        var incoming = RunInstruction.Parse("RUN [\"echo\"]", '`');
        var command = ExecFormCommand.Parse("[\"echo\"]");
        incoming.Command = command;
        string before = file.ToString();
        string incomingBefore = incoming.ToString();
        DockerfileConstruct[] items = file.Items.ToArray();
        Token[] incomingTokens = incoming.Tokens.ToArray();

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (replace)
            {
                file.Items[1] = incoming;
            }
            else
            {
                file.Items.Add(incoming);
            }
        });

        Assert.Equal(before, file.ToString());
        Assert.Equal(items, file.Items);
        Assert.Equal(incomingBefore, incoming.ToString());
        Assert.Equal(incomingTokens, incoming.Tokens);
        Assert.Same(command, incoming.Command);
    }

    /// <summary>
    /// Confirms adopted commands with matching context remain safe for later syntax-aware descendant edits.
    /// </summary>
    /// <param name="replace">Whether adoption uses replacement or insertion.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DocumentAdoptionPreservesCompatibleNestedEditing(bool replace)
    {
        var file = Dockerfile.Parse("# escape=`\nFROM alpine\n");
        var incoming = RunInstruction.Parse("RUN [\"echo\"]", '`');
        var command = ExecFormCommand.Parse("[\"echo\"]", '`');
        incoming.Command = command;
        if (replace)
        {
            file.Items[1] = incoming;
        }
        else
        {
            file.Items.Add(incoming);
        }

        command.ValueTokens.Add(new LiteralToken("a`\nb", escapeChar: '`'));

        Assert.Same(command, incoming.Command);
        Assert.Same(incoming, file.Items.Last());
        DockerfileParseResult parsed = Dockerfile.TryParse(file.ToString());
        Assert.True(parsed.Success);
        Assert.Equal(file.ToString(), parsed.Dockerfile!.ToString());
        Assert.Equal(new[] { "echo", "ab" }, command.Values);
    }

    /// <summary>
    /// Preserves recovered malformed text while permitting an unrelated insertion outside that construct.
    /// </summary>
    [Fact]
    public void OpaqueMalformedConstructDoesNotPreventIndependentInsertion()
    {
        Dockerfile file = Dockerfile.TryParse("FROM alpine\nCOPY [\nRUN echo old\n",
            new DockerfileParseOptions { Mode = DockerfileParseMode.Recover }).Dockerfile!;
        string before = file.ToString();
        file.Items.Add(new RunInstruction("echo new"));
        Assert.Equal(before + "RUN echo new", file.ToString());
    }
}
