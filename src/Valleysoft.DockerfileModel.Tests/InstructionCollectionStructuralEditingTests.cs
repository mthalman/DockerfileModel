using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Protects instruction collection edits across syntax forms, required cardinalities, and token ownership boundaries.
/// </summary>
public class InstructionCollectionStructuralEditingTests
{
    /// <summary>
    /// Exercises mixed source edits without replacing unrelated operands, embedded comments, or the following instruction.
    /// </summary>
    /// <param name="newline">The document's physical line separator.</param>
    /// <param name="escape">The escape character used by the document and its continuation.</param>
    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '\\')]
    [InlineData("\n", '`')]
    [InlineData("\r\n", '`')]
    public void SourcesKeepDestinationCommentsAndFollowingInstruction(string newline, char escape)
    {
        string directive = escape == '`' ? $"# escape=`{newline}" : "";
        Dockerfile document = Dockerfile.Parse(
            $"{directive}COPY --chown=1 one {escape}{newline}# keep{newline}two /out/{newline}RUN unchanged{newline}");
        CopyInstruction copy = document.Items.OfType<CopyInstruction>().Single();
        Token destination = copy.DestinationToken!;
        CommentToken comment = copy.CommentTokens.Single();
        Instruction following = document.Items.OfType<RunInstruction>().Single();
        string followingText = following.ToString();
        LiteralToken first = copy.SourceTokens[0];

        copy.Sources.Add("three");
        copy.Sources.Insert(0, "zero");
        copy.SourceTokens.Move(0, 2);
        copy.Sources.Remove("three");

        Assert.Equal(new[] { "one", "two", "zero" }, copy.Sources);
        Assert.Same(first, copy.SourceTokens[0]);
        Assert.Same(destination, copy.DestinationToken);
        Assert.Same(comment, copy.CommentTokens.Single());
        Assert.Same(following, document.Items.OfType<RunInstruction>().Single());
        Assert.Equal(followingText, following.ToString());
        Assert.Contains($"# keep{newline}", document.ToString());
        Assert.Equal(document.ToString(), Dockerfile.Parse(document.ToString()).ToString());
    }

    /// <summary>
    /// Keeps JSON source editing separate from the destination while adopting and quoting incoming source tokens.
    /// </summary>
    [Fact]
    public void JsonSourcesUseCommasAndNeverIncludeDestination()
    {
        CopyInstruction copy = CopyInstruction.Parse("COPY [\"a\",  \"b\", \"/dest/\"]\r\n");
        LiteralToken destination = copy.DestinationToken!;
        LiteralToken inserted = new("c");

        copy.SourceTokens.Insert(1, inserted);
        copy.Sources.RemoveAt(0);
        copy.Sources.Move(1, 0);

        Assert.Equal(new[] { "b", "c" }, copy.Sources);
        Assert.Same(inserted, copy.SourceTokens[1]);
        Assert.Same(destination, copy.DestinationToken);
        Assert.Equal('"', inserted.QuoteChar);
        Assert.Equal(copy.Sources, CopyInstruction.Parse(copy.ToString()).Sources);
        Assert.EndsWith("\r\n", copy.ToString());
    }

    /// <summary>
    /// Ensures explicit trivia discard cannot bypass the requirement for at least one file-transfer source.
    /// </summary>
    /// <param name="text">A shell- or JSON-form instruction with one required source.</param>
    [Theory]
    [InlineData("COPY one /dest")]
    [InlineData("COPY [\"one\", \"/dest\"]")]
    public void ClearingOnlySourceIsAtomic(string text)
    {
        CopyInstruction copy = CopyInstruction.Parse(text);
        Token[] tokens = copy.Tokens.ToArray();
        Assert.Throws<InvalidOperationException>(() => copy.Sources.Clear(TriviaDisposition.Discard));
        Assert.Equal(text, copy.ToString());
        Assert.Equal(tokens, copy.Tokens);
    }

    /// <summary>
    /// Verifies optional exclude flags remain in the flag region and can be removed without disturbing operands.
    /// </summary>
    /// <param name="text">An ADD or COPY instruction containing an unrelated flag.</param>
    [Theory]
    [InlineData("COPY --chown=123 src /dest")]
    [InlineData("ADD --chmod=755 src /dest")]
    public void OptionalExcludesRemainBeforeOperandsAndCanBeCleared(string text)
    {
        FileTransferInstruction instruction = text.StartsWith("COPY", StringComparison.Ordinal)
            ? CopyInstruction.Parse(text) : AddInstruction.Parse(text);
        EditableList<string> excludes = instruction is CopyInstruction copy ? copy.Excludes : ((AddInstruction)instruction).Excludes;
        LiteralToken source = instruction.SourceTokens.Single();
        LiteralToken destination = instruction.DestinationToken!;

        excludes.Add("*.tmp");
        excludes.Insert(0, "*.log");
        excludes.Move(0, 1);
        Assert.Equal(new[] { "*.tmp", "*.log" }, excludes);
        Assert.True(instruction.ToString().IndexOf("--exclude", StringComparison.Ordinal) <
            instruction.ToString().IndexOf("src", StringComparison.Ordinal));
        excludes.Clear();

        Assert.Empty(excludes);
        Assert.Same(source, instruction.SourceTokens.Single());
        Assert.Same(destination, instruction.DestinationToken);
        Assert.Equal(text, instruction.ToString());
    }

    /// <summary>
    /// Ensures cached semantic views reflect typed edits while retaining port tokens and enforcing nonempty cardinality.
    /// </summary>
    [Fact]
    public void PortsAreLiveAndRequired()
    {
        ExposeInstruction expose = ExposeInstruction.Parse("EXPOSE 80 443\n");
        EditableList<string> ports = expose.Ports;
        LiteralToken first = expose.PortTokens[0];
        ports[0] = "8080";
        Assert.Same(first, expose.PortTokens[0]);
        ports.Add("53/udp");
        ports.Insert(1, "8000/tcp");
        expose.PortTokens.Move(3, 0);
        Assert.Equal(new[] { "53/udp", "8080", "8000/tcp", "443" }, ports);
        ports.RemoveAt(2);
        string before = expose.ToString();
        Assert.Throws<InvalidOperationException>(() => ports.Clear());
        Assert.Equal(before, expose.ToString());
    }

    /// <summary>
    /// Distinguishes semantic replacement from token adoption and rejects duplicate ownership of an adopted token.
    /// </summary>
    [Fact]
    public void TypedReplaceAdoptsTokenButValueReplaceKeepsIdentity()
    {
        ExposeInstruction expose = ExposeInstruction.Parse("EXPOSE 80 443");
        LiteralToken original = expose.PortTokens[0];
        expose.Ports.Replace(0, "8080");
        Assert.Same(original, expose.PortTokens[0]);
        LiteralToken replacement = new("9000");
        expose.PortTokens.Replace(0, replacement);
        Assert.Same(replacement, expose.PortTokens[0]);
        string before = expose.ToString();
        Assert.Throws<InvalidOperationException>(() => expose.PortTokens.Add(replacement));
        Assert.Equal(before, expose.ToString());
    }

    /// <summary>
    /// Requires incoming operands to match the owner's escape context before adoption.
    /// </summary>
    [Fact]
    public void TypedOperandsRejectIncompatibleEscapeContextAtomically()
    {
        ExposeInstruction expose = ExposeInstruction.Parse("EXPOSE 80", '`');
        LiteralToken incompatible = new("443");
        Assert.Throws<InvalidOperationException>(() => expose.PortTokens.Add(incompatible));
        Assert.Equal("EXPOSE 80", expose.ToString());
        LiteralToken compatible = new("443", escapeChar: '`');
        expose.PortTokens.Add(compatible);
        Assert.Same(compatible, expose.PortTokens[1]);
    }

    /// <summary>
    /// Covers JSON separator repair through empty and repopulated command argument lists, including empty strings.
    /// </summary>
    [Fact]
    public void ExecValuesCanBecomeEmptyAndBeRepopulated()
    {
        ExecFormCommand command = ExecFormCommand.Parse("[\"a\", \"b\"]");
        EditableList<string> values = command.Values;
        values.Insert(1, "hello world");
        Assert.Equal(new[] { "a", "hello world", "b" }, ExecFormCommand.Parse(command.ToString()).Values);
        command.ValueTokens.Move(2, 0);
        values.RemoveAt(1);
        values.Clear();
        Assert.Empty(values);
        Assert.Empty(ExecFormCommand.Parse(command.ToString()).Values);
        values.Add("");
        values.Add("x");
        Assert.Equal(new[] { "", "x" }, ExecFormCommand.Parse(command.ToString()).Values);
    }

    /// <summary>
    /// Requires reference-based mount membership and preserves the command and unrelated flags across mount edits.
    /// </summary>
    [Fact]
    public void MountListAdoptsIdentityKeepsCommandAndCanBeCleared()
    {
        RunInstruction run = RunInstruction.Parse("RUN --network=host echo unchanged\n");
        Command command = run.Command!;
        Mount first = Mount.Parse("type=cache,target=/cache");
        Mount second = Mount.Parse("type=bind,source=.,target=/src");
        run.Mounts.Add(first);
        run.Mounts.Insert(0, second);
        Assert.Same(second, run.Mounts[0]);
        Assert.Same(first, run.Mounts[1]);
        Assert.Contains(first, run.Mounts);
        Assert.DoesNotContain(Mount.Parse(first.ToString()), run.Mounts);
        run.Mounts.Move(1, 0);
        Assert.Same(first, run.Mounts[0]);
        Assert.Equal(2, RunInstruction.Parse(run.ToString()).Mounts.Count);
        Assert.Same(command, run.Command);
        run.Mounts.Clear();
        Assert.Equal("RUN --network=host echo unchanged\n", run.ToString());
        Assert.Same(command, run.Command);
    }

    /// <summary>
    /// Requires projected self-replacement to keep the backing flag, nested mount, and all existing formatting.
    /// </summary>
    /// <param name="trivia">The replacement policy, neither of which should alter a self-replacement.</param>
    [Theory]
    [InlineData(TriviaDisposition.Preserve)]
    [InlineData(TriviaDisposition.Discard)]
    public void ProjectedMountSelfReplacementKeepsBackingTokens(TriviaDisposition trivia)
    {
        var run = RunInstruction.Parse("RUN --mount=target=/cache  echo ok\n");
        Mount mount = run.Mounts[0];
        Token[] tokens = run.Tokens.ToArray();
        Command? command = run.Command;
        string before = run.ToString();

        run.Mounts[0] = mount;
        run.Mounts.Replace(0, mount, trivia);
        run.Mounts.ReplaceItem(mount, mount, trivia);

        Assert.Equal(before, run.ToString());
        Assert.Equal(tokens, run.Tokens);
        Assert.Same(mount, Assert.Single(run.Mounts));
        Assert.Same(command, run.Command);
    }

    /// <summary>
    /// Keeps the self-replacement exception from allowing the same mount to become owned by two flags.
    /// </summary>
    [Fact]
    public void ProjectedMountReplacementRejectsAnotherSlotsMount()
    {
        var run = RunInstruction.Parse("RUN --mount=target=/first --mount=target=/second echo ok");
        Mount[] mounts = run.Mounts.ToArray();
        Token[] tokens = run.Tokens.ToArray();
        string before = run.ToString();

        Assert.Throws<InvalidOperationException>(() => run.Mounts[0] = mounts[1]);

        Assert.Equal(before, run.ToString());
        Assert.Equal(tokens, run.Tokens);
        Assert.Same(mounts[0], run.Mounts[0]);
        Assert.Same(mounts[1], run.Mounts[1]);
    }

    /// <summary>
    /// Guards against a compatible flag wrapper hiding an incompatible nested mount during token adoption.
    /// </summary>
    [Fact]
    public void TypedMountFlagCannotMaskNestedEscapeMismatch()
    {
        RunInstruction run = RunInstruction.Parse("RUN echo ok", '`');
        TokenList<MountFlag> flags = new(run);
        Mount incompatible = Mount.Parse("target=/cache");
        MountFlag wrapper = new(incompatible, '`');

        Assert.Throws<InvalidOperationException>(() => flags.Add(wrapper));
        Assert.Equal("RUN echo ok", run.ToString());
        Assert.Empty(flags);
        Assert.Same(incompatible, wrapper.ValueToken);
    }

    /// <summary>
    /// Requires escape-context validation to inspect assignment values rather than trusting their wrapper.
    /// </summary>
    [Fact]
    public void AssignmentWrapperCannotMaskValueEscapeMismatch()
    {
        EnvInstruction env = EnvInstruction.Parse("ENV A=one", '`');
        LiteralToken incompatible = new("two");
        KeyValueToken<Variable, LiteralToken> wrapper = new(
            new Variable("B", '`'), incompatible, isFlag: false, escapeChar: '`');

        Assert.Throws<InvalidOperationException>(() => env.VariableTokens.Add(wrapper));
        Assert.Equal("ENV A=one", env.ToString());
        Assert.Single(env.VariableTokens);
        Assert.Same(incompatible, wrapper.ValueToken);
    }

    /// <summary>
    /// Preserves shell versus JSON representation while editing the same semantic path collection.
    /// </summary>
    /// <param name="text">The initial VOLUME instruction in the syntax form to retain.</param>
    [Theory]
    [InlineData("VOLUME /one /two")]
    [InlineData("VOLUME [\"/one\", \"/two\"]")]
    public void VolumeListKeepsItsSyntaxForm(string text)
    {
        VolumeInstruction volume = VolumeInstruction.Parse(text);
        volume.Paths.Add("/three");
        volume.Paths.Move(2, 0);
        volume.Paths.RemoveAt(1);
        Assert.Equal(new[] { "/three", "/two" }, VolumeInstruction.Parse(volume.ToString()).Paths);
        Assert.Equal(text.Contains('['), volume.ToString().Contains('['));
    }

    /// <summary>
    /// Allows JSON VOLUME lists to become empty and then regain an element without invalid separator syntax.
    /// </summary>
    [Fact]
    public void JsonVolumeCanBecomeEmpty()
    {
        VolumeInstruction volume = VolumeInstruction.Parse("VOLUME [\"/one\"]");
        volume.Paths.Clear();
        Assert.Empty(VolumeInstruction.Parse(volume.ToString()).Paths);
        volume.Paths.Add("/two");
        Assert.Equal("/two", volume.Paths.Single());
    }

    /// <summary>
    /// Verifies adding a second legacy ENV variable quotes the original value without replacing its assignment token.
    /// </summary>
    [Fact]
    public void LegacyEnvironmentConvertsOnlyAffectedAssignment()
    {
        EnvInstruction env = EnvInstruction.Parse("ENV greeting hello world\n");
        var first = env.VariableTokens.Single();
        env.Variables.Add(Pair("NEXT", "value"));
        Assert.Same(first, env.VariableTokens[0]);
        Assert.Equal("hello world", env.Variables[0].Value);
        Assert.Equal(new[] { "greeting", "NEXT" }, env.Variables.Select(pair => pair.Key));
        Assert.Equal("ENV greeting=\"hello world\" NEXT=value\n", env.ToString());
        Assert.Equal("hello world", EnvInstruction.Parse(env.ToString()).Variables[0].Value);
    }

    /// <summary>
    /// Establishes case-sensitive value comparison for assignment views while preserving token identity on value edits.
    /// </summary>
    /// <param name="text">An ENV, LABEL, or ARG instruction whose assignment collection is exercised.</param>
    [Theory]
    [InlineData("ENV A=1 B=2")]
    [InlineData("LABEL A=1 B=2")]
    [InlineData("ARG A=1 B=2")]
    public void AssignmentViewsUseValueEqualityAndKeepTypedIdentity(string text)
    {
        Instruction owner;
        EditableList<IKeyValuePair> pairs;
        if (text.StartsWith("ENV", StringComparison.Ordinal))
        {
            EnvInstruction env = EnvInstruction.Parse(text);
            owner = env;
            pairs = env.Variables;
        }
        else if (text.StartsWith("LABEL", StringComparison.Ordinal))
        {
            LabelInstruction label = LabelInstruction.Parse(text);
            owner = label;
            pairs = label.Labels;
        }
        else
        {
            ArgInstruction arg = ArgInstruction.Parse(text);
            owner = arg;
            pairs = arg.Args;
        }
        IKeyValuePair original = pairs[0];
        Assert.Equal(0, pairs.IndexOf(Pair("A", "1")));
        Assert.Equal(-1, pairs.IndexOf(Pair("a", "1")));
        pairs[0] = Pair("A", "updated");
        Assert.Same(original, pairs[0]);
        pairs.Add(Pair("C", "3"));
        pairs.Move(2, 0);
        Assert.True(pairs.Remove(Pair("B", "2")));
        Assert.Equal(new[] { "C", "A" }, pairs.Select(pair => pair.Key));
        string before = owner.ToString();
        Assert.Throws<InvalidOperationException>(() => pairs.Clear());
        Assert.Equal(before, owner.ToString());
    }

    /// <summary>
    /// Keeps a bare ARG declaration distinct from an assignment when inserting and removing declarations.
    /// </summary>
    [Fact]
    public void ArgDeclarationsAllowMissingDefaults()
    {
        ArgInstruction arg = ArgInstruction.Parse("ARG A");
        arg.Args.Add(Pair("B", null));
        arg.Args.Insert(1, Pair("C", "three"));
        Assert.Null(arg.Args[2].Value);
        Assert.Equal("ARG A C=three B", arg.ToString());
        arg.Args.RemoveAt(0);
        Assert.Equal(new[] { "C", "B" }, arg.Args.Select(pair => pair.Key));
    }

    /// <summary>
    /// Repairs generic-instruction continuations and rejects embedded physical newlines in a single logical line.
    /// </summary>
    /// <param name="newline">The separator used for generated continuations.</param>
    /// <param name="escape">The owner's continuation escape character.</param>
    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '`')]
    public void GenericLinesRepairContinuations(string newline, char escape)
    {
        GenericInstruction instruction = GenericInstruction.Parse($"RUN echo one{newline}", escape);
        instruction.ArgLines.Add("&& echo two");
        instruction.ArgLines.Insert(1, "&& echo middle");
        Assert.Equal(new[] { "echo one", "&& echo middle", "&& echo two" }, instruction.ArgLines);
        Assert.Contains($"{escape}{newline}", instruction.ToString());
        instruction.ArgLines.Move(1, 2);
        instruction.ArgLines.RemoveAt(2);
        Assert.Equal(new[] { "echo one", "&& echo two" }, GenericInstruction.Parse(instruction.ToString(), escape).ArgLines);
        string before = instruction.ToString();
        Assert.Throws<ArgumentException>(() => instruction.ArgLines.Add("bad\nline"));
        Assert.Equal(before, instruction.ToString());
    }

    /// <summary>
    /// Prevents appending a line from normalizing a mixed-newline instruction to its first continuation's style.
    /// </summary>
    [Fact]
    public void NewGenericLineUsesNearbyNewlineInsteadOfFirstNewline()
    {
        GenericInstruction instruction = GenericInstruction.Parse(
            "RUN echo one \\\n&& echo two \\\r\n&& echo three\r\n");

        instruction.ArgLines.Add("&& echo four");

        Assert.Contains("&& echo three \\\r\n&& echo four\r\n", instruction.ToString());
        Assert.Contains("echo one \\\n", instruction.ToString());
        Assert.Equal(4, GenericInstruction.Parse(instruction.ToString()).ArgLines.Count);
    }

    /// <summary>
    /// Rejects instruction-spilling strings and multi-operand tokens before changing the owner's text or token list.
    /// </summary>
    [Fact]
    public void InvalidItemCannotTruncateOrPartiallyMutateOwner()
    {
        ExposeInstruction instruction = ExposeInstruction.Parse("EXPOSE 80 443");
        string before = instruction.ToString();
        Token[] tokens = instruction.Tokens.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => instruction.Ports.Add("90\nRUN injected"));
        Assert.Throws<InvalidOperationException>(() => instruction.PortTokens.Add(new LiteralToken("90 91")));
        Assert.Equal(before, instruction.ToString());
        Assert.Equal(tokens, instruction.Tokens);
    }

    /// <summary>
    /// Creates a detached semantic value so comparisons cannot succeed merely through shared token identity.
    /// </summary>
    private static IKeyValuePair Pair(string key, string? value) => new PairValue { Key = key, Value = value };

    /// <summary>
    /// Supplies assignment values independently of the production token-backed implementations.
    /// </summary>
    private sealed class PairValue : IKeyValuePair
    {
        public string Key { get; set; } = "";
        public string? Value { get; set; }
    }
}
