using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>Protects heredoc editing on consumer types that inherit supported instruction behavior.</summary>
public class InheritedHeredocStructuralEditingTests
{
    /// <summary>Inherited owners support complete pair edits without losing their instruction kind or payload.</summary>
    /// <param name="kind">The built-in instruction family inherited by the consumer.</param>
    /// <param name="escapeChar">The instruction's parsing context.</param>
    /// <param name="newLine">The exact line ending used in raw heredoc payloads.</param>
    [Theory]
    [InlineData("RUN", '\\', "\n")]
    [InlineData("RUN", '\\', "\r\n")]
    [InlineData("RUN", '`', "\n")]
    [InlineData("RUN", '`', "\r\n")]
    [InlineData("COPY", '\\', "\n")]
    [InlineData("COPY", '\\', "\r\n")]
    [InlineData("COPY", '`', "\n")]
    [InlineData("COPY", '`', "\r\n")]
    [InlineData("ADD", '\\', "\n")]
    [InlineData("ADD", '\\', "\r\n")]
    [InlineData("ADD", '`', "\n")]
    [InlineData("ADD", '`', "\r\n")]
    public void InheritedOwnersSupportPairedEdits(string kind, char escapeChar, string newLine)
    {
        Instruction owner = CreateOwner(kind, escapeChar);
        EditableList<Heredoc> pairs = GetHeredocs(owner);
        Heredoc first = new("FIRST", "first" + newLine);
        Heredoc second = new("SECOND", "second" + newLine);
        Heredoc replacement = new("REPLACEMENT", "replacement" + newLine);
        Assert.NotEqual(typeof(Instruction).Assembly, owner.GetType().Assembly);

        pairs.Add(first);
        Assert.Same(first, Assert.Single(pairs));
        AssertReparses(owner, kind, escapeChar);

        pairs.Insert(0, second);
        Assert.Same(second, pairs[0]);
        Assert.Same(first, pairs[1]);
        AssertReparses(owner, kind, escapeChar);

        pairs.Move(0, 1);
        Assert.Same(first, pairs[0]);
        Assert.Same(second, pairs[1]);
        AssertReparses(owner, kind, escapeChar);

        pairs.ReplaceItem(first, replacement);
        Assert.Same(replacement, pairs[0]);
        Assert.Same(second, pairs[1]);
        AssertReparses(owner, kind, escapeChar);

        pairs.RemoveAt(1);
        Assert.Same(replacement, Assert.Single(pairs));
        AssertReparses(owner, kind, escapeChar);

        pairs.Clear();
        Assert.Empty(pairs);
        AssertReparses(owner, kind, escapeChar);
        if (owner is RunInstruction run)
        {
            Assert.Equal("cat /input", Assert.IsType<ShellFormCommand>(run.Command).Value);
        }
        else
        {
            FileTransferInstruction transfer = Assert.IsAssignableFrom<FileTransferInstruction>(owner);
            Assert.Equal(new[] { "source" }, transfer.Sources);
            Assert.Equal("/out", transfer.Destination);
        }
    }

    /// <summary>Subclass support does not permit a different built-in instruction kind after scalar mutation.</summary>
    /// <param name="kind">The owner's built-in family.</param>
    /// <param name="keyword">A different family with a syntactically valid interpretation of the header.</param>
    [Theory]
    [InlineData("RUN", "COPY")]
    [InlineData("COPY", "ADD")]
    [InlineData("ADD", "COPY")]
    public void DifferentParsedKindStillRejects(string kind, string keyword)
    {
        Instruction owner = CreateOwner(kind, '\\');
        Assert.IsType<StringToken>(Assert.Single(owner.InstructionNameToken.Tokens)).Value = keyword;
        string before = owner.ToString();
        Heredoc incoming = new("DOC", "body\n");
        string marker = incoming.Marker.ToString();
        string body = incoming.Body.ToString();

        Assert.Throws<InvalidOperationException>(() => GetHeredocs(owner).Add(incoming));

        Assert.Equal(before, owner.ToString());
        Assert.Empty(GetHeredocs(owner));
        Assert.Equal(marker, incoming.Marker.ToString());
        Assert.Equal(body, incoming.Body.ToString());
    }

    /// <summary>Matching the RUN family does not admit a subclass with custom effective serialization.</summary>
    [Fact]
    public void OverriddenSerializationStillRejectsBeforeInvocation()
    {
        OverridingRun owner = new();
        Token[] tokens = owner.Tokens.ToArray();
        Heredoc incoming = new("DOC", "body\n");

        Assert.Throws<InvalidOperationException>(() => owner.Heredocs.Add(incoming));

        Assert.Equal(0, owner.Calls);
        Assert.Equal(tokens, owner.Tokens);
        Assert.Empty(owner.Heredocs);
    }

    /// <summary>Creates a consumer subclass without overriding token serialization.</summary>
    private static Instruction CreateOwner(string kind, char escapeChar) => kind switch
    {
        "RUN" => new InheritedRun(escapeChar),
        "COPY" => new InheritedCopy(escapeChar),
        "ADD" => new InheritedAdd(escapeChar),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Selects the same paired view exposed by either supported owner family.</summary>
    private static EditableList<Heredoc> GetHeredocs(Instruction owner) => owner switch
    {
        RunInstruction run => run.Heredocs,
        FileTransferInstruction transfer => transfer.Heredocs,
        _ => throw new ArgumentOutOfRangeException(nameof(owner))
    };

    /// <summary>Checks complete serialization, built-in kind, and ordered pair semantics after each edit.</summary>
    private static void AssertReparses(Instruction owner, string kind, char escapeChar)
    {
        string text = owner.ToString();
        string prefix = escapeChar == '`' ? "# escape=`\n" : "";
        DockerfileParseResult result = Dockerfile.TryParse(prefix + text);
        Assert.True(result.Success);
        Instruction parsed = Assert.Single(result.Dockerfile!.Items.OfType<Instruction>());
        Assert.Equal(kind, parsed.InstructionNameToken.Value);
        Assert.Equal(text, parsed.ToString());
        Assert.Equal(
            GetHeredocs(owner).Select(pair => (pair.Name, pair.RawContent, pair.Chomp, pair.Expand)),
            GetHeredocs(parsed).Select(pair => (pair.Name, pair.RawContent, pair.Chomp, pair.Expand)));
    }

    /// <summary>Uses the inherited RUN implementation from a consumer assembly.</summary>
    /// <param name="escapeChar">The parsing context passed to the built-in constructor.</param>
    private sealed class InheritedRun(char escapeChar) : RunInstruction("cat /input", escapeChar);

    /// <summary>Uses the inherited COPY implementation from a consumer assembly.</summary>
    /// <param name="escapeChar">The parsing context passed to the built-in constructor.</param>
    private sealed class InheritedCopy(char escapeChar)
        : CopyInstruction(new[] { "source" }, "/out", escapeChar: escapeChar);

    /// <summary>Uses the inherited ADD implementation from a consumer assembly.</summary>
    /// <param name="escapeChar">The parsing context passed to the built-in constructor.</param>
    private sealed class InheritedAdd(char escapeChar)
        : AddInstruction(new[] { "source" }, "/out", escapeChar: escapeChar);

    /// <summary>Detects accidental invocation of unsupported consumer serialization during admission.</summary>
    private sealed class OverridingRun() : RunInstruction("cat")
    {
        /// <summary>Counts serializer invocations that must not occur during the rejected edit.</summary>
        public int Calls { get; private set; }

        /// <summary>Fails if structural admission invokes the consumer override.</summary>
        /// <param name="options">The requested serialization options.</param>
        /// <returns>No value; this implementation always throws.</returns>
        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            Calls++;
            throw new InvalidOperationException("Unsupported serialization was invoked.");
        }
    }
}
