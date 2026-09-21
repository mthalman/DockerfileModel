using System.Collections;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Exercises shared collection contracts through real semantic, token, and document adapters.
/// </summary>
public class EditableListContractTests
{
    /// <summary>
    /// Semantic enumerators capture values when obtained, not on the first move or after later edits.
    /// </summary>
    /// <param name="nonGeneric">Whether to enumerate through the non-generic interface.</param>
    /// <param name="advanceFirst">Whether enumeration starts before mutation.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SemanticEnumerationCapturesOriginalValues(bool nonGeneric, bool advanceFirst)
    {
        var instruction = ExposeInstruction.Parse("EXPOSE 80 443");

        AssertSnapshot(instruction.Ports, () =>
        {
            instruction.Ports[0] = "8080";
            instruction.Ports.RemoveAt(1);
            instruction.Ports.Add("53/udp");
            instruction.Ports.Move(0, 1);
        }, nonGeneric, advanceFirst);

        Assert.Equal(new[] { "53/udp", "8080" }, instruction.Ports);
        Assert.Equal(instruction.Ports, ExposeInstruction.Parse(instruction.ToString()).Ports);
    }

    /// <summary>
    /// Typed snapshots retain their original sequence but reference live objects rather than cloned elements.
    /// </summary>
    /// <param name="nonGeneric">Whether to enumerate through the non-generic interface.</param>
    /// <param name="advanceFirst">Whether enumeration starts before mutation.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TokenEnumerationRetainsReferencesWithoutCloning(bool nonGeneric, bool advanceFirst)
    {
        var instruction = ExposeInstruction.Parse("EXPOSE 80 443");
        LiteralToken first = instruction.PortTokens[0];
        LiteralToken removed = instruction.PortTokens[1];

        AssertSnapshot(instruction.PortTokens, () =>
        {
            instruction.Ports[0] = "8080";
            instruction.PortTokens.RemoveAt(1);
            instruction.PortTokens.Add(new LiteralToken("53/udp"));
            instruction.PortTokens.Move(0, 1);
        }, nonGeneric, advanceFirst);

        Assert.Equal("8080", first.Value);
        Assert.Equal("443", removed.Value);
        Assert.Same(first, instruction.PortTokens[1]);
        Assert.DoesNotContain(removed, instruction.PortTokens);
    }

    /// <summary>
    /// Document enumerators retain removed constructs while fresh reads reflect the new document order.
    /// </summary>
    /// <param name="nonGeneric">Whether to enumerate through the non-generic interface.</param>
    /// <param name="advanceFirst">Whether enumeration starts before mutation.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DocumentEnumerationSurvivesRemovalAndReordering(bool nonGeneric, bool advanceFirst)
    {
        var document = Dockerfile.Parse("RUN a\nRUN b\n");
        DockerfileConstruct retained = document.Items[1];
        var incoming = new WorkdirInstruction("/app");

        AssertSnapshot(document.Items, () =>
        {
            document.Items.RemoveAt(0);
            document.Items.Add(incoming);
            document.Items.Move(0, 1);
        }, nonGeneric, advanceFirst);

        Assert.Same(incoming, document.Items[0]);
        Assert.Same(retained, document.Items[1]);
        Assert.Equal(document.ToString(), Dockerfile.Parse(document.ToString()).ToString());
    }

    /// <summary>
    /// Indexed insertion handles every boundary consistently across collection representations.
    /// </summary>
    /// <param name="index">The insertion boundary, including the position after the last element.</param>
    /// <param name="expectedOrder">Original element letters and the inserted element represented by x.</param>
    [Theory]
    [InlineData(0, "xabcd")]
    [InlineData(1, "axbcd")]
    [InlineData(2, "abxcd")]
    [InlineData(3, "abcxd")]
    [InlineData(4, "abcdx")]
    public void IndexedInsertionUsesTheSelectedBoundary(int index, string expectedOrder)
    {
        var semantic = ExecFormCommand.Parse("[\"a\", \"b\", \"c\", \"d\"]");
        AssertInsert(semantic.Values, "x", index, expectedOrder);
        Assert.Equal(expectedOrder.Select(character => character.ToString()),
            ExecFormCommand.Parse(semantic.ToString()).Values);

        var typed = ExecFormCommand.Parse("[\"a\", \"b\", \"c\", \"d\"]");
        var incoming = new LiteralToken("x");
        AssertInsert(typed.ValueTokens, incoming, index, expectedOrder);
        Assert.Same(incoming, typed.ValueTokens[expectedOrder.IndexOf('x')]);
        Assert.Equal(semantic.ToString(), typed.ToString());

        var document = Dockerfile.Parse("RUN a\nRUN b\nRUN c\nRUN d\n");
        AssertInsert(document.Items, new RunInstruction("x"), index, expectedOrder);
        Assert.Equal(document.ToString(), Dockerfile.Parse(document.ToString()).ToString());
    }

    /// <summary>
    /// Indexed moves use final positions in both directions, including endpoints and same-position no-ops.
    /// </summary>
    /// <param name="sourceIndex">The moved element's original index.</param>
    /// <param name="finalIndex">The moved element's index after the operation.</param>
    /// <param name="expectedOrder">The expected permutation of the original element letters.</param>
    [Theory]
    [InlineData(0, 1, "bacd")]
    [InlineData(0, 2, "bcad")]
    [InlineData(0, 3, "bcda")]
    [InlineData(3, 0, "dabc")]
    [InlineData(3, 1, "adbc")]
    [InlineData(2, 1, "acbd")]
    [InlineData(1, 1, "abcd")]
    [InlineData(2, 2, "abcd")]
    public void IndexedMovesPreserveOrderAndIdentity(int sourceIndex, int finalIndex, string expectedOrder)
    {
        var semantic = ExecFormCommand.Parse("[\"a\",  \"b\", \"c\", \"d\"]");
        AssertMove(semantic.Values, semantic.ToString, sourceIndex, finalIndex, expectedOrder);
        Assert.Equal(expectedOrder.Select(character => character.ToString()),
            ExecFormCommand.Parse(semantic.ToString()).Values);

        var typed = ExecFormCommand.Parse("[\"a\",  \"b\", \"c\", \"d\"]");
        AssertMove(typed.ValueTokens, typed.ToString, sourceIndex, finalIndex, expectedOrder);
        Assert.Equal(semantic.ToString(), typed.ToString());

        var document = Dockerfile.Parse("RUN a\nRUN b\nRUN c\nRUN d\n");
        AssertMove(document.Items, document.ToString, sourceIndex, finalIndex, expectedOrder);
        Assert.Equal(document.ToString(), Dockerfile.Parse(document.ToString()).ToString());
    }

    /// <summary>
    /// Duplicate semantic strings cannot select a unique replacement, but removal selects their first occurrence.
    /// </summary>
    [Fact]
    public void DuplicateStringsRejectReplacementButRemoveFirstMatch()
    {
        var command = ExecFormCommand.Parse("[\"same\", \"unique\", \"same\"]");
        LiteralToken[] tokens = command.ValueTokens.ToArray();
        string equalValue = new("same".ToCharArray());

        Assert.Equal(0, command.Values.IndexOf(equalValue));
        bool containsEqualValue = command.Values.Contains(equalValue);
        bool containsDifferentCase = command.Values.Contains("Same");
        Assert.True(containsEqualValue);
        Assert.False(containsDifferentCase);
        AssertAmbiguousReplacement(command.Values, equalValue, "incoming", command.ToString);
        Assert.Equal(tokens, command.ValueTokens);

        Assert.True(command.Values.Remove(equalValue));
        Assert.Equal(new[] { "unique", "same" }, command.Values);
        Assert.Same(tokens[1], command.ValueTokens[0]);
        Assert.Same(tokens[2], command.ValueTokens[1]);
        Assert.True(command.Values.Remove(equalValue));
        string beforeMissingRemoval = command.ToString();
        Assert.False(command.Values.Remove(equalValue));
        Assert.Equal(beforeMissingRemoval, command.ToString());
        Assert.Same(tokens[1], Assert.Single(command.ValueTokens));
        Assert.Equal(new[] { "unique" }, ExecFormCommand.Parse(command.ToString()).Values);
    }

    /// <summary>
    /// Assignment equality is ordinal and distinguishes absent from empty values for replacement and removal.
    /// </summary>
    /// <param name="value">The repeated assignment value, including the absent-default case.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("value")]
    public void DuplicateAssignmentsUseKeyAndNullableValueEquality(string? value)
    {
        string suffix = value is null ? "" : $"=\"{value}\"";
        var instruction = ArgInstruction.Parse($"ARG same{suffix} unique=one same{suffix}");
        IKeyValuePair[] original = instruction.Args.ToArray();
        var equal = new Assignment("same", value);
        var incoming = new Assignment("incoming", "two");

        Assert.Equal(0, instruction.Args.IndexOf(equal));
        bool containsDifferentCase = instruction.Args.Contains(new Assignment("Same", value));
        bool containsDifferentValue = instruction.Args.Contains(new Assignment("same", value is null ? "" : null));
        Assert.False(containsDifferentCase);
        Assert.False(containsDifferentValue);
        AssertAmbiguousReplacement(instruction.Args, equal, incoming, instruction.ToString);

        Assert.True(instruction.Args.Remove(equal));
        Assert.Same(original[1], instruction.Args[0]);
        Assert.Same(original[2], instruction.Args[1]);
        instruction.Args.ReplaceItem(equal, incoming);
        Assert.Same(original[2], instruction.Args[1]);
        Assert.Equal("incoming", instruction.Args[1].Key);
        Assert.Equal("two", instruction.Args[1].Value);
        Assert.Equal(instruction.ToString(), ArgInstruction.Parse(instruction.ToString()).ToString());
    }

    /// <summary>
    /// Equal token text does not make distinct token instances interchangeable as replacement or removal targets.
    /// </summary>
    [Fact]
    public void TokenMembershipUsesIdentityDespiteEqualText()
    {
        var instruction = ExposeInstruction.Parse("EXPOSE 80 80 443");
        LiteralToken[] original = instruction.PortTokens.ToArray();
        var inserted = new LiteralToken("90");
        var foreign = new LiteralToken("80");
        string before = instruction.ToString();

        Assert.Equal(-1, instruction.PortTokens.IndexOf(foreign));
        Assert.False(instruction.PortTokens.Remove(foreign));
        Assert.Throws<ArgumentException>(() => instruction.PortTokens.ReplaceItem(foreign, inserted));
        Assert.Equal(before, instruction.ToString());
        Assert.Equal("90", inserted.ToString());

        instruction.PortTokens.Insert(instruction.PortTokens.IndexOf(original[1]), inserted);

        Assert.Equal("EXPOSE 80 90 80 443", instruction.ToString());
        Assert.Same(original[0], instruction.PortTokens[0]);
        Assert.Same(inserted, instruction.PortTokens[1]);
        Assert.Same(original[1], instruction.PortTokens[2]);
        Assert.Same(original[2], instruction.PortTokens[3]);
    }

    /// <summary>
    /// Missing replacement targets are rejected without blocking subsequent valid edits.
    /// </summary>
    [Fact]
    public void MissingReplacementRejectsWithoutMutation()
    {
        var command = ExecFormCommand.Parse("[\"a\", \"b\"]");
        LiteralToken[] tokens = command.ValueTokens.ToArray();
        string before = command.ToString();
        Assert.Throws<ArgumentException>(() => command.Values.ReplaceItem("missing", "x"));
        Assert.Equal(before, command.ToString());
        Assert.Equal(tokens, command.ValueTokens);

        command.Values.Insert(command.Values.IndexOf("b"), "x");
        Assert.Equal(new[] { "a", "x", "b" }, command.Values);
        Assert.Same(tokens[0], command.ValueTokens[0]);
        Assert.Same(tokens[1], command.ValueTokens[2]);
    }

    /// <summary>
    /// Concrete default arguments and standard list interfaces apply the same preservation policy.
    /// </summary>
    /// <param name="operation">The removal operation whose default policy is exercised.</param>
    /// <param name="viaInterface">Whether to call through the standard list interface.</param>
    [Theory]
    [InlineData("Remove", false)]
    [InlineData("Remove", true)]
    [InlineData("RemoveAt", false)]
    [InlineData("RemoveAt", true)]
    [InlineData("Clear", false)]
    [InlineData("Clear", true)]
    public void DefaultRemovalPolicyPreservesTrivia(string operation, bool viaInterface)
    {
        var document = Dockerfile.Parse("RUN echo \\\n# retained\nok\n");
        DockerfileConstruct instruction = document.Items[0];
        CommentToken comment = Assert.Single(Assert.IsType<RunInstruction>(instruction).CommentTokens);
        string before = document.ToString();
        IList<DockerfileConstruct> list = document.Items;
        Action remove = (operation, viaInterface) switch
        {
            ("Remove", false) => () => Assert.True(document.Items.Remove(instruction)),
            ("Remove", true) => () => Assert.True(list.Remove(instruction)),
            ("RemoveAt", false) => () => document.Items.RemoveAt(0),
            ("RemoveAt", true) => () => list.RemoveAt(0),
            ("Clear", false) => () => document.Items.Clear(),
            ("Clear", true) => () => list.Clear(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        if (operation == "Clear")
        {
            Assert.Throws<InvalidOperationException>(remove);
            Assert.Equal(before, document.ToString());
            Assert.Same(instruction, Assert.Single(document.Items));
        }
        else
        {
            remove();
            Assert.Equal("# retained\n", document.ToString());
            Assert.Same(comment, Assert.IsType<Comment>(Assert.Single(document.Items)).ValueToken);
        }

        document.Items.Clear(TriviaDisposition.Discard);
        Assert.Empty(document.Items);
        Assert.Equal("", document.ToString());
    }

    /// <summary>
    /// Standard interface signatures remain usable as delegates when concrete methods take an optional policy.
    /// </summary>
    [Fact]
    public void StandardInterfaceRemovalDelegatesRemainCallable()
    {
        var command = ExecFormCommand.Parse("[\"a\", \"b\", \"c\"]");
        IList<LiteralToken> list = command.ValueTokens;
        Func<LiteralToken, bool> remove = list.Remove;
        Action<int> removeAt = list.RemoveAt;
        Action clear = list.Clear;
        LiteralToken last = list[2];

        Assert.True(remove(list[0]));
        removeAt(0);
        Assert.Same(last, Assert.Single(list));
        clear();
        Assert.Empty(list);
        Assert.Empty(ExecFormCommand.Parse(command.ToString()).Values);

        command.Values.Add("again");
        Action clearConcrete = () => command.Values.Clear();
        clearConcrete();
        Assert.Empty(command.Values);
    }

    /// <summary>
    /// Interface-based copying retains token references, checks destination bounds, and never edits its source.
    /// </summary>
    [Fact]
    public void CopyToPreservesReferencesAndValidatesDestination()
    {
        var command = ExecFormCommand.Parse("[\"a\", \"b\"]");
        ICollection<LiteralToken> collection = command.ValueTokens;
        LiteralToken[] original = command.ValueTokens.ToArray();
        var sentinel = new LiteralToken("sentinel");
        LiteralToken[] destination = [sentinel, sentinel, sentinel, sentinel];

        collection.CopyTo(destination, 1);

        Assert.Same(sentinel, destination[0]);
        Assert.Same(original[0], destination[1]);
        Assert.Same(original[1], destination[2]);
        Assert.Same(sentinel, destination[3]);
        Assert.Throws<ArgumentNullException>(() => collection.CopyTo(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => collection.CopyTo(destination, -1));
        Assert.Throws<ArgumentException>(() => collection.CopyTo(destination, 3));
        Assert.Throws<ArgumentException>(() => collection.CopyTo(new LiteralToken[1], 0));
        Assert.Equal("[\"a\", \"b\"]", command.ToString());
        Assert.Equal(original, command.ValueTokens);
    }

    /// <summary>Separates enumerator creation from its first move to detect deferred snapshot capture.</summary>
    private static void AssertSnapshot<T>(EditableList<T> collection, Action edit, bool nonGeneric, bool advanceFirst)
        where T : class
    {
        T[] original = collection.ToArray();
        IEnumerator enumerator = nonGeneric ? ((IEnumerable)collection).GetEnumerator() : collection.GetEnumerator();
        var observed = new List<object?>();
        try
        {
            if (advanceFirst)
            {
                Assert.True(enumerator.MoveNext());
                observed.Add(enumerator.Current);
            }
            edit();
            while (enumerator.MoveNext())
            {
                observed.Add(enumerator.Current);
            }
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
        Assert.Equal(original.Cast<object?>(), observed);
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] is not string)
            {
                Assert.Same(original[i], observed[i]);
            }
        }
        Assert.NotEqual(original, collection.ToArray());
    }

    /// <summary>Checks a caller-specified ordering without deriving the expectation from adapter indices.</summary>
    private static void AssertInsert<T>(EditableList<T> collection, T incoming,
        int index, string expectedOrder)
        where T : class
    {
        T[] original = collection.ToArray();
        collection.Insert(index, incoming);
        AssertOrder(expectedOrder.Select(character => character == 'x' ? incoming : original[character - 'a']), collection);
    }

    /// <summary>Requires same-position moves to preserve formatting as well as the reference sequence.</summary>
    private static void AssertMove<T>(EditableList<T> collection, Func<string> render,
        int sourceIndex, int finalIndex, string expectedOrder)
        where T : class
    {
        T[] original = collection.ToArray();
        string originalText = render();
        collection.Move(sourceIndex, finalIndex);
        AssertOrder(expectedOrder.Select(character => original[character - 'a']), collection);
        if (expectedOrder == "abcd")
        {
            Assert.Equal(originalText, render());
        }
    }

    /// <summary>Requires ambiguous replacement to leave both text and object identities unchanged.</summary>
    private static void AssertAmbiguousReplacement<T>(EditableList<T> collection,
        T ambiguous, T incoming, Func<string> render)
        where T : class
    {
        T[] original = collection.ToArray();
        string before = render();
        Assert.Throws<InvalidOperationException>(() => collection.ReplaceItem(ambiguous, incoming));
        Assert.Equal(before, render());
        AssertOrder(original, collection);
    }

    /// <summary>Distinguishes semantic string equality from model-object identity in shared contract checks.</summary>
    private static void AssertOrder<T>(IEnumerable<T> expected, EditableList<T> actual)
        where T : class
    {
        T[] items = expected.ToArray();
        Assert.Equal(items, actual);
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] is not string)
            {
                Assert.Same(items[i], actual[i]);
            }
        }
    }

    /// <summary>Supplies detached values so equality checks cannot succeed through shared token identity.</summary>
    private sealed class Assignment(string key, string? value) : IKeyValuePair
    {
        public string Key { get; set; } = key;
        public string? Value { get; set; } = value;
    }
}
