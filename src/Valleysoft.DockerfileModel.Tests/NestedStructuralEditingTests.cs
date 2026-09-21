using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Protects nested mount entry identity, representable CSV syntax, and builder escape-context propagation.
/// </summary>
public class NestedStructuralEditingTests
{
    /// <summary>
    /// Equal-valued assignment roots remain distinct edit targets even when a consumer overrides object equality.
    /// </summary>
    /// <param name="operation">The collection write whose exact target identity is checked.</param>
    [Theory]
    [InlineData("insert")]
    [InlineData("append")]
    [InlineData("replace")]
    [InlineData("replace-item")]
    [InlineData("remove-at")]
    [InlineData("remove")]
    [InlineData("move")]
    public void EqualMountAssignmentsAreEditedByReference(string operation)
    {
        var mount = Mount.Parse("type=bind,type=bind");
        var first = new EqualMountAssignment();
        mount.TypeToken = first;
        var original = mount.Entries.ToArray();
        var incoming = new MountEntry("type", "bind");
        MountEntry[] expected;
        switch (operation)
        {
            case "insert":
                mount.Entries.Insert(1, incoming);
                expected = new[] { original[0], incoming, original[1] };
                break;
            case "append":
                mount.Entries.Add(incoming);
                expected = new[] { original[0], original[1], incoming };
                break;
            case "replace":
                mount.Entries[1] = incoming;
                expected = new[] { original[0], incoming };
                break;
            case "replace-item":
                mount.Entries.ReplaceItem(original[1], incoming);
                expected = new[] { original[0], incoming };
                break;
            case "remove-at":
                mount.Entries.RemoveAt(1);
                expected = new[] { original[0] };
                break;
            case "remove":
                Assert.True(mount.Entries.Remove(original[1]));
                expected = new[] { original[0] };
                break;
            case "move":
                mount.Entries.Move(1, 0);
                expected = new[] { original[1], original[0] };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        var actual = mount.Entries.ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Same(expected[i], actual[i]);
            Assert.Same(expected[i].KeyValueToken, actual[i].KeyValueToken);
            Assert.Equal(i, mount.Entries.IndexOf(expected[i]));
        }
        Assert.Equal(0, first.EqualsCalls);
        Assert.Equal(string.Join(",", Enumerable.Repeat("type=bind", expected.Length)), mount.ToString());
        Assert.Equal(mount.ToString(), Mount.Parse(mount.ToString()).ToString());
    }

    /// <summary>
    /// Snapshot reconciliation evicts a detached root even when its replacement compares equal, retaining surviving views.
    /// </summary>
    [Fact]
    public void MountCacheTracksReferenceMembershipAcrossRawReplacement()
    {
        var mount = Mount.Parse("type=bind,type=bind");
        var first = new EqualMountAssignment();
        mount.TypeToken = first;
        var original = mount.Entries.ToArray();
        var replacement = new EqualMountAssignment();

        mount.TypeToken = replacement;
        Assert.Same(replacement, mount.Entries[0].KeyValueToken);
        Assert.Same(original[1], mount.Entries[1]);
        mount.TypeToken = first;

        Assert.NotSame(original[0], mount.Entries[0]);
        Assert.Same(first, mount.Entries[0].KeyValueToken);
        Assert.Same(original[1], mount.Entries[1]);
        Assert.Equal("type=bind,type=bind", mount.ToString());
    }

    /// <summary>
    /// Removing the final entry removes its selected comma, not an equal-comparing separator earlier in the mount.
    /// </summary>
    [Fact]
    public void MountRemovalKeepsTheUnselectedEqualComma()
    {
        var firstComma = new EqualMountComma();
        var secondComma = new SymbolToken(',');
        var builder = new TokenBuilder();
        builder.Mount(fields =>
        {
            fields.KeyValue(new KeywordToken("target"), new LiteralToken("/cache"));
            fields.Tokens.Add(firstComma);
            fields.Keyword("readonly");
            fields.Tokens.Add(secondComma);
            fields.Keyword("required");
        });
        var mount = Assert.IsType<Mount>(Assert.Single(builder.Tokens));
        var entries = mount.Entries.ToArray();

        mount.Entries.RemoveAt(2);

        Assert.Equal("target=/cache,readonly", mount.ToString());
        Assert.Same(firstComma, Assert.Single(mount.Tokens.OfType<SymbolToken>()));
        Assert.Same(entries[0], mount.Entries[0]);
        Assert.Same(entries[1], mount.Entries[1]);
        Assert.Equal("required", entries[2].Key);
        Assert.Equal(0, firstComma.EqualsCalls);
    }

    /// <summary>
    /// Every mount write performs metadata admission, including no-op paths, while ordinary snapshots remain readable.
    /// </summary>
    /// <param name="operation">The facade entry point invoked against an unsupported existing serializer.</param>
    [Theory]
    [InlineData("add")]
    [InlineData("insert")]
    [InlineData("replace")]
    [InlineData("replace-item")]
    [InlineData("remove")]
    [InlineData("remove-missing")]
    [InlineData("remove-at")]
    [InlineData("clear")]
    [InlineData("move")]
    [InlineData("same-index-move")]
    [InlineData("same-item-replace")]
    public void MountWritesAdmitTreeBeforeBookkeeping(string operation)
    {
        var mount = Mount.Parse("type=bind,readonly");
        var poison = new MutatingMountValue();
        mount.TypeToken = new KeyValueToken<KeywordToken, LiteralToken>(new KeywordToken("type"), poison);
        var entries = mount.Entries.ToArray();
        var incoming = new MountEntry("required");
        var ownerTree = Descendants(mount).ToArray();
        var incomingTree = Descendants(incoming.Token).ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            switch (operation)
            {
                case "add": mount.Entries.Add(incoming); break;
                case "insert": mount.Entries.Insert(1, incoming); break;
                case "replace": mount.Entries[1] = incoming; break;
                case "replace-item": mount.Entries.ReplaceItem(entries[1], incoming); break;
                case "remove": mount.Entries.Remove(entries[1]); break;
                case "remove-missing": mount.Entries.Remove(incoming); break;
                case "remove-at": mount.Entries.RemoveAt(1); break;
                case "clear": mount.Entries.Clear(); break;
                case "move": mount.Entries.Move(1, 0); break;
                case "same-index-move": mount.Entries.Move(0, 0); break;
                case "same-item-replace": mount.Entries[0] = entries[0]; break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        });

        Assert.Contains("built-in", exception.Message);
        Assert.Equal(0, poison.Calls);
        Assert.Equal("bind", Assert.IsType<StringToken>(Assert.Single(poison.Tokens)).Value);
        Assert.Equal(entries, mount.Entries);
        Assert.Equal(ownerTree, Descendants(mount));
        Assert.Equal(incomingTree, Descendants(incoming.Token));
    }

    private sealed class EqualMountAssignment() :
        KeyValueToken<KeywordToken, LiteralToken>(new KeywordToken("type"), new LiteralToken("bind"))
    {
        public int EqualsCalls { get; private set; }

        public override bool Equals(object? obj)
        {
            EqualsCalls++;
            return obj is KeyValueToken<KeywordToken, LiteralToken> other && Key == other.Key && Value == other.Value;
        }

        public override int GetHashCode() => 0;
    }

    private sealed class EqualMountComma() : SymbolToken(',')
    {
        public int EqualsCalls { get; private set; }

        public override bool Equals(object? obj)
        {
            EqualsCalls++;
            return obj is SymbolToken { Value: "," };
        }

        public override int GetHashCode() => 0;
    }

    private sealed class MutatingMountValue() : LiteralToken("bind")
    {
        public int Calls { get; private set; }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            Calls++;
            ((StringToken)Tokens.First()).Value = "mutated";
            throw new InvalidOperationException("Consumer serializer must not execute during admission.");
        }
    }

    /// <summary>
    /// Rejects incompatible entry roots or children immediately, rather than leaving a mount that cannot be adopted later.
    /// </summary>
    /// <param name="replace">Whether to replace an entry instead of inserting one.</param>
    /// <param name="mismatch">The incoming token role constructed in the incompatible context.</param>
    [Theory]
    [InlineData(false, "root")]
    [InlineData(true, "root")]
    [InlineData(false, "key")]
    [InlineData(true, "key")]
    [InlineData(false, "value")]
    [InlineData(true, "value")]
    public void MountEntryContextMismatchRejectsAtomically(bool replace, string mismatch)
    {
        var mount = Mount.Parse("target=/cache,readonly", '`');
        var incoming = mismatch == "root" ? new MountEntry("required") :
            new MountEntry("source", "/src", escapeChar: '`');
        if (mismatch == "key") incoming.KeyValueToken!.KeyToken = new KeywordToken("source");
        if (mismatch == "value") incoming.KeyValueToken!.ValueToken = new LiteralToken("/src");
        var entries = mount.Entries.ToArray();
        var ownerTree = Descendants(mount).Select(token => (token, text: token.ToString())).ToArray();
        var incomingTree = Descendants(incoming.Token).Select(token => (token, text: token.ToString())).ToArray();

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (replace) mount.Entries[0] = incoming;
            else mount.Entries.Insert(1, incoming);
        });

        Assert.Equal(entries, mount.Entries);
        Assert.Equal(ownerTree, Descendants(mount).Select(token => (token, text: token.ToString())));
        Assert.Equal(incomingTree, Descendants(incoming.Token).Select(token => (token, text: token.ToString())));
        Assert.DoesNotContain(incoming, mount.Entries);
        mount.Entries.Add(new MountEntry("required", escapeChar: '`'));
        Assert.Equal("target=/cache,readonly,required", mount.ToString());
    }

    /// <summary>
    /// Matching entry edits preserve identities and leave the complete mount eligible for matching-context adoption.
    /// </summary>
    /// <param name="escapeChar">The shared mount, entry, and receiving instruction context.</param>
    [Theory]
    [InlineData('\\')]
    [InlineData('`')]
    public void EditedMountRemainsAdoptableInMatchingContext(char escapeChar)
    {
        var mount = Mount.Parse("target=/cache,readonly", escapeChar);
        var retained = mount.Entries[1];
        var inserted = new MountEntry("required", escapeChar: escapeChar);
        var replacement = new MountEntry("source", "/src", escapeChar);
        mount.Entries.Insert(1, inserted);
        mount.Entries[0] = replacement;
        var run = RunInstruction.Parse("RUN echo ok", escapeChar);

        run.Mounts.Add(mount);

        Assert.Same(mount, Assert.Single(run.Mounts));
        Assert.Equal(new[] { replacement, inserted, retained }, mount.Entries);
        Assert.Equal("RUN --mount=source=/src,required,readonly echo ok", run.ToString());
        Assert.Equal("echo ok", Assert.IsType<ShellFormCommand>(run.Command).Value);
        Assert.Equal(run.ToString(), RunInstruction.Parse(run.ToString(), escapeChar).ToString());
    }

    private static IEnumerable<Token> Descendants(Token token)
    {
        yield return token;
        if (token is AggregateToken aggregate)
            foreach (Token child in aggregate.Tokens)
                foreach (Token descendant in Descendants(child))
                    yield return descendant;
    }

    /// <summary>
    /// Ensures mount entry wrappers stay stable across reads and collection edits adopt the caller's entry object.
    /// </summary>
    [Fact]
    public void MountEntriesRemainStableAndAdoptTokens()
    {
        Mount mount = Mount.Parse("target=/a,readonly,source=,target=/b");
        MountEntry first = mount.Entries[0];
        Assert.Same(first, mount.Entries[0]);
        Assert.Equal(0, mount.Entries.IndexOf(first));
        var entry = new MountEntry("sharing", "locked");
        mount.Entries.Insert(1, entry);
        Assert.Same(entry, mount.Entries[1]);
        mount.Entries.Move(1, 4);
        Assert.Equal("target=/a,readonly,source=,target=/b,sharing=locked", mount.ToString());
        mount.Entries.Remove(first);
        Assert.Equal("readonly,source=,target=/b,sharing=locked", mount.ToString());
    }

    /// <summary>
    /// Covers explicit and default mount types, duplicate type entries, and rejection of an empty mount.
    /// </summary>
    [Fact]
    public void MountTypeEntriesAndCardinalityFailuresAreAtomic()
    {
        Mount mount = Mount.Parse("readonly");
        mount.Entries.Insert(0, new MountEntry("type", "cache"));
        Assert.Equal("type=cache,readonly", mount.ToString());
        Assert.Equal("cache", mount.Type);
        mount.Entries.RemoveAt(0);
        Assert.Equal("readonly", mount.ToString());
        Assert.Equal("bind", mount.Type);
        MountEntry remaining = mount.Entries[0];
        Assert.Throws<InvalidOperationException>(() => mount.Entries.Clear());
        Assert.Equal("readonly", mount.ToString());
        Assert.Same(remaining, mount.Entries[0]);
        mount = Mount.Parse("type=bind,type=cache");
        MountEntry first = mount.Entries[0];
        mount.Entries[1] = new MountEntry("type", "tmpfs");
        Assert.Equal("type=bind,type=tmpfs", mount.ToString());
        Assert.Same(first, mount.Entries[0]);
    }

    /// <summary>
    /// Distinguishes duplicate keys by position and preserves the difference between bare keywords and empty assignments.
    /// </summary>
    [Fact]
    public void MountIndexedDuplicatesAndEmptyValues()
    {
        Mount mount = Mount.Parse("target=/a,target=/b,source=,readonly");
        Assert.Equal("", mount.Entries[2].Value);
        Assert.Null(mount.Entries[3].Value);
        Assert.True(mount.Entries[3].IsBareKeyword);
        mount.Entries[1] = new MountEntry("target", "/c");
        Assert.Equal("target=/a,target=/c,source=,readonly", mount.ToString());
        mount.Entries.RemoveAt(3);
        mount.Entries.RemoveAt(1);
        Assert.Equal("target=/a,source=", mount.ToString());
    }

    /// <summary>
    /// Ensures replacing a raw type token invalidates its old wrapper without hiding the newly attached token.
    /// </summary>
    [Fact]
    public void MountViewsReconcileRawRootReplacement()
    {
        Mount mount = Mount.Parse("type=cache,target=/a");
        MountEntry old = mount.Entries[0];
        var replacement = new MountEntry("type", "bind");
        mount.TypeToken = replacement.KeyValueToken!;
        Assert.NotSame(old, mount.Entries[0]);
        Assert.DoesNotContain(old, mount.Entries);
        Assert.Same(replacement.KeyToken, mount.Entries[0].KeyToken);
    }

    /// <summary>
    /// Rejects edits when escaped CSV text cannot be mapped safely to the modeled entries.
    /// </summary>
    [Fact]
    public void MountUnmappedCsvIsRejectedWithoutMutation()
    {
        Mount mount = Mount.Parse("target=a\\,from=external,readonly");
        string before = mount.ToString();
        Assert.ThrowsAny<Exception>(() => mount.Entries.Add(new MountEntry("required")));
        Assert.Equal(before, mount.ToString());
    }

    /// <summary>
    /// Preserves existing quoted and variable-bearing entries while rejecting unsupported constructed values with spaces.
    /// </summary>
    [Fact]
    public void MountSupportsQuotedValuesAndVariables()
    {
        Mount mount = Mount.Parse("target='/a',source=$SOURCE");
        mount.Entries.Add(new MountEntry("readonly"));
        Assert.Equal("target='/a',source=$SOURCE,readonly", mount.ToString());
        Assert.Equal("/a", mount.Entries[0].Value);
        Assert.Equal("$SOURCE", mount.Entries[1].Value);
        Assert.ThrowsAny<Exception>(() => new MountEntry("target", "/path with spaces"));
    }

    /// <summary>
    /// Verifies callback-built shell commands propagate the builder's escape context into the command and value token.
    /// </summary>
    [Fact]
    public void CallbackBuiltShellCommandRetainsEscapeContext()
    {
        var builder = new TokenBuilder { EscapeChar = '`', DefaultNewLine = "\n" };
        builder.ShellFormCommand(tokens =>
            tokens.Literal(value => value.String("echo ").LineContinuation().String("ok")));
        var command = Assert.IsType<ShellFormCommand>(Assert.Single(builder.Tokens));
        Assert.Equal('`', command.EditingEscapeChar);
        Assert.Equal('`', command.ValueToken.EditingEscapeChar);
        Assert.Equal("echo `\nok", command.ToString());
        Assert.Equal("echo ok", command.Value);
    }

    /// <summary>
    /// Ensures callback-built bare mount entries retain the builder's context when adopted by an instruction.
    /// </summary>
    [Fact]
    public void CallbackBuiltKeywordRetainsEscapeContextForMountAdoption()
    {
        var builder = new TokenBuilder { EscapeChar = '`' };
        builder.Mount(mount => mount.Keyword(keyword => keyword.String("readonly")));
        var mount = Assert.IsType<Mount>(Assert.Single(builder.Tokens));
        var keyword = Assert.Single(mount.Tokens.OfType<KeywordToken>());
        var run = RunInstruction.Parse("RUN echo ok", '`');

        run.Mounts.Add(mount);

        Assert.Equal('`', keyword.EditingEscapeChar);
        Assert.Same(mount, Assert.Single(run.Mounts));
        Assert.Equal("RUN --mount=readonly echo ok", run.ToString());
        Assert.Equal(run.ToString(), RunInstruction.Parse(run.ToString(), '`').ToString());
    }

    /// <summary>
    /// Verifies callback-built exec commands can adopt replacement values using the builder's nondefault escape context.
    /// </summary>
    [Fact]
    public void CallbackBuiltExecCommandRetainsEscapeContext()
    {
        var builder = new TokenBuilder { EscapeChar = '`' };
        builder.ExecFormCommand(tokens => tokens.Symbol('[').Literal("\"echo\"").Symbol(']'));
        var command = Assert.IsType<ExecFormCommand>(Assert.Single(builder.Tokens));
        var replacement = new LiteralToken("changed", escapeChar: '`') { QuoteChar = '"' };
        command.ValueTokens[0] = replacement;
        Assert.Same(replacement, command.ValueTokens[0]);
        Assert.Equal("[\"changed\"]", command.ToString());
    }
}
