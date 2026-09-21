using Valleysoft.DockerfileModel.Tokens;
using Xunit.Abstractions;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Exercises interleaved nested collection edits against independent semantic lists, including recoverable rejected calls.
/// </summary>
/// <param name="output">Records the seed's operation trace so a failure can be replayed at the same step.</param>
public class StructuralEditingMixedSequenceTests(ITestOutputHelper output)
{
    /// <summary>
    /// Reduces the generated clear/repopulate failure: comment insertion must not change how RUN flags parse.
    /// </summary>
    /// <param name="newline">The original header continuation's line ending.</param>
    /// <param name="escapeChar">The original continuation escape character.</param>
    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '`')]
    public void RepopulatingCommentsPreservesHeredocRunFlags(string newline, char escapeChar)
    {
        string directive = escapeChar == '`' ? "# escape=`" + newline : "";
        string text = "RUN " + escapeChar + newline + "#old" + newline +
            "--network=none --mount=target=/cache cat <<D" + newline + "body\nD" + newline;
        var run = Dockerfile.TryParse(directive + text).Dockerfile!.Items.OfType<RunInstruction>().Single();
        var network = run.NetworkToken;
        var mount = Assert.Single(run.Mounts);
        var pair = Assert.Single(run.Heredocs);

        run.CommentTokens.Clear();
        run.CommentTokens.Add(new CommentToken("new"));

        output.WriteLine(run.ToString());
        var result = Dockerfile.TryParse(directive + run);
        Assert.True(result.Success);
        var parsed = Assert.Single(result.Dockerfile!.Items.OfType<RunInstruction>());
        Assert.Same(network, run.NetworkToken);
        Assert.Same(mount, Assert.Single(run.Mounts));
        Assert.Same(pair, Assert.Single(run.Heredocs));
        Assert.Equal("none", parsed.Network);
        Assert.Equal("target=/cache", Assert.Single(parsed.Mounts).ToString());
        Assert.Equal("body\n", Assert.Single(parsed.Heredocs).RawContent);
    }

    /// <summary>
    /// Reduces a mixed-sequence failure where header continuations caused flags to be captured as restored command text.
    /// </summary>
    /// <param name="newline">The original header continuation's line ending.</param>
    /// <param name="escapeChar">The original continuation escape character.</param>
    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '`')]
    public void ClearingHeredocsKeepsContinuedHeaderFlagsOutsideCommand(string newline, char escapeChar)
    {
        string directive = escapeChar == '`' ? "# escape=`" + newline : "";
        string header = "RUN " + escapeChar + newline + "#note" + newline +
            "--network=none --mount=target=/cache cat <<D" + newline;
        var run = Dockerfile.TryParse(directive + header + "body\nD" + newline)
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var network = run.NetworkToken;
        var mount = Assert.Single(run.Mounts);
        var comment = Assert.Single(run.CommentTokens);
        var continuation = Assert.Single(run.Tokens.OfType<LineContinuationToken>());
        var commandLeaves = run.Tokens.OfType<LiteralToken>().First().Tokens.ToArray();

        run.Heredocs.Clear();

        Assert.Equal(header.Replace("<<D", ""), run.ToString());
        Assert.Same(network, run.NetworkToken);
        Assert.Same(mount, Assert.Single(run.Mounts));
        Assert.Same(comment, Assert.Single(run.CommentTokens));
        Assert.Contains(continuation, run.Tokens);
        var command = Assert.IsType<ShellFormCommand>(run.Command);
        Assert.Equal("cat", command.Value.Trim());
        foreach (Token leaf in commandLeaves) Assert.Contains(leaf, command.ValueToken.Tokens);
        var parsed = Dockerfile.TryParse(directive + run).Dockerfile!.Items.OfType<RunInstruction>().Single();
        Assert.Equal("none", parsed.Network);
        Assert.Equal("target=/cache", Assert.Single(parsed.Mounts).ToString());
        Assert.Equal(command.Value, Assert.IsType<ShellFormCommand>(parsed.Command).Value);
    }

    /// <summary>
    /// The first heredoc must be inserted at the command boundary, not before an earlier header continuation comment.
    /// </summary>
    /// <param name="newline">The original header continuation's line ending.</param>
    /// <param name="escapeChar">The original continuation escape character.</param>
    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '`')]
    public void AddingFirstHeredocSkipsEarlierHeaderComments(string newline, char escapeChar)
    {
        string directive = escapeChar == '`' ? "# escape=`" + newline : "";
        string header = "RUN " + escapeChar + newline + "#note" + newline +
            "--network=none --mount=target=/cache cat";
        var run = Dockerfile.TryParse(directive + header + newline)
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var network = run.NetworkToken;
        var mount = Assert.Single(run.Mounts);
        var comment = Assert.Single(run.CommentTokens);
        var pair = new Heredoc("D", "body\n", escapeChar: escapeChar);
        var command = Assert.IsType<ShellFormCommand>(run.Command).ValueToken;

        run.Heredocs.Add(pair);

        Assert.Equal(header.Replace("/cache cat", "/cache <<D cat") + newline + "body\nD" + newline, run.ToString());
        Assert.Same(network, run.NetworkToken);
        Assert.Same(mount, Assert.Single(run.Mounts));
        Assert.Same(comment, Assert.Single(run.CommentTokens));
        Assert.Same(pair, Assert.Single(run.Heredocs));
        Assert.Null(run.Command);
        Assert.Same(command, Assert.Single(run.Tokens.OfType<LiteralToken>()));
        Assert.Equal("cat", command.Value.Trim());
        var parsed = Dockerfile.TryParse(directive + run).Dockerfile!.Items.OfType<RunInstruction>().Single();
        Assert.Equal("none", parsed.Network);
        Assert.Equal("target=/cache", Assert.Single(parsed.Mounts).ToString());
        Assert.Equal("cat", Assert.Single(parsed.Tokens.OfType<LiteralToken>()).Value.Trim());
        Assert.Equal("body\n", Assert.Single(parsed.Heredocs).RawContent);
    }

    /// <summary>
    /// Every seed executes every operation family; generated positions and repeated values vary the intermediate states.
    /// </summary>
    /// <param name="seed">The deterministic generator seed for positions, values, quoting, and chomp modes.</param>
    /// <param name="newline">The header and closing-delimiter newline style.</param>
    /// <param name="escapeChar">The context shared by the document and incoming typed objects.</param>
    [Theory]
    [InlineData(3, "\n", '\\')]
    [InlineData(17, "\r\n", '\\')]
    [InlineData(41, "\n", '`')]
    [InlineData(73, "\r\n", '`')]
    [InlineData(101, "\n", '\\')]
    [InlineData(211, "\r\n", '\\')]
    [InlineData(509, "\n", '`')]
    [InlineData(997, "\r\n", '`')]
    public void MixedNestedEditsRemainConsistentAfterEveryCall(int seed, string newline, char escapeChar)
    {
        var state = new SequenceState(seed, newline, escapeChar, output);
        state.Verify();
        for (int round = 0; round < 2; round++)
        {
            state.InsertSource();
            state.InsertMount();
            state.AddComment(copy: true);
            state.AddComment(copy: false);
            state.InsertPair(copy: true);
            state.InsertPair(copy: false);
            state.InsertEntry();
            state.RejectAliasedSource();
            state.ReplaceSource();
            state.RejectDuplicatePair();
            state.ReplacePair(copy: true);
            state.RejectDuplicateEntry();
            state.ReplaceEntry();
            state.MoveSource();
            state.MoveMount();
            state.MovePair(copy: true);
            state.ReplacePair(copy: false);
            state.ReplaceComment();
            state.MoveComment();
            state.RejectIncompatibleMount();
            state.ReplaceMount();
            state.RemoveMount();
            state.RemoveSource();
            state.MoveEntry();
            state.RemoveEntry();
            state.RemovePair();
            state.MovePair(copy: false);
            state.RemoveComment(copy: true);
            state.RemoveComment(copy: false);
            state.ClearMounts();
            state.InsertMount();
            state.ClearComments(copy: false);
            state.AddComment(copy: false);
            state.ClearSources();
            state.RejectLastCopySourceRemoval();
            state.InsertSource();
            state.ClearPairs(copy: true);
            state.InsertPair(copy: true);
            state.ClearPairs(copy: false);
            state.InsertPair(copy: false);
            state.RejectEmptyMount();
            state.InsertEntry();
            state.ClearComments(copy: true);
            state.AddComment(copy: true);
        }
        Assert.Equal(12, state.RejectedCalls);
        Assert.Equal(88, state.Calls);
    }

    private sealed record SourceState(LiteralToken Item, string Value);
    private sealed record CommentState(CommentToken Item, string Value);
    private sealed record PairState(Heredoc Item, string Name, string Raw, HeredocQuoteKind Quote, bool Chomp);
    private sealed record EntryState(MountEntry Item, string Key, string? Value);
    private sealed record MountState(Mount Item, List<EntryState> Entries);

    private sealed class SequenceState
    {
        private readonly Random random;
        private readonly string newline;
        private readonly string payloadNewline;
        private readonly char escape;
        private readonly ITestOutputHelper output;
        private readonly Dockerfile file;
        private readonly DockerfileConstruct[] documentItems;
        private readonly CopyInstruction copy;
        private readonly RunInstruction run;
        private readonly LiteralToken destination;
        private readonly LiteralToken from;
        private readonly LiteralToken network;
        private readonly List<SourceState> sources = new();
        private readonly List<MountState> mounts = new();
        private readonly List<PairState> copyPairs = new();
        private readonly List<PairState> runPairs = new();
        private readonly List<CommentState> copyComments = new();
        private readonly List<CommentState> runComments = new();
        private readonly TreeSnapshot siblings;
        private readonly LineContinuationToken[] fixedContinuations;

        public int Calls { get; private set; }
        public int RejectedCalls { get; private set; }

        public SequenceState(int seed, string newline, char escape, ITestOutputHelper output)
        {
            random = new(seed);
            this.newline = newline;
            payloadNewline = newline == "\n" ? "\r\n" : "\n";
            this.escape = escape;
            this.output = output;
            string directive = escape == '`' ? "# escape=`" + newline : "";
            string raw = "\t# payload 0" + payloadNewline;
            string text = directive + "FROM alpine AS base" + newline + "# untouched sibling  \r\n" +
                "COPY " + escape + newline + "#copy-note" + newline +
                "--from=base source0 <<D /dst" + newline + raw + "D" + newline +
                "RUN " + escape + newline + "#run-note" + newline +
                "--network=none --mount=target=/cache,readonly cat <<D" + newline + raw + "D" + newline +
                "RUN echo untouched" + newline;
            file = Parse(text);
            documentItems = file.Items.ToArray();
            copy = Assert.Single(file.Items.OfType<CopyInstruction>());
            run = file.Items.OfType<RunInstruction>().First();
            destination = copy.DestinationToken!;
            from = copy.FromStageNameToken!;
            network = run.NetworkToken!;
            sources.Add(new(copy.SourceTokens[0], "source0"));
            copyPairs.Add(new(copy.Heredocs[0], "D", raw, HeredocQuoteKind.Unquoted, false));
            runPairs.Add(new(run.Heredocs[0], "D", raw, HeredocQuoteKind.Unquoted, false));
            copyComments.Add(new(copy.CommentTokens[0], "copy-note"));
            runComments.Add(new(run.CommentTokens[0], "run-note"));
            Mount initial = run.Mounts[0];
            mounts.Add(new(initial, new()
            {
                new(initial.Entries[0], "target", "/cache"),
                new(initial.Entries[1], "readonly", null)
            }));
            siblings = new(documentItems.Where(item => !ReferenceEquals(item, copy) && !ReferenceEquals(item, run)));
            fixedContinuations = copy.Tokens.Concat(run.Tokens).OfType<LineContinuationToken>().ToArray();
        }

        public void InsertSource()
        {
            int index = random.Next(sources.Count + 1);
            string value = "source" + random.Next(3);
            var item = new LiteralToken(value, canContainVariables: true, escape);
            Apply("source insert", copy, () => copy.SourceTokens.Insert(index, item),
                () => sources.Insert(index, new(item, value)));
        }

        public void ReplaceSource()
        {
            int index = random.Next(sources.Count);
            string value = "source" + random.Next(3);
            var item = new LiteralToken(value, canContainVariables: true, escape);
            Apply("source replace", copy, () => copy.SourceTokens[index] = item,
                () => sources[index] = new(item, value));
        }

        public void MoveSource()
        {
            int old = random.Next(sources.Count), next = (old + 1) % sources.Count;
            Apply("source move", copy, () => copy.SourceTokens.Move(old, next), () => Move(sources, old, next));
        }

        public void RemoveSource()
        {
            int index = random.Next(sources.Count);
            Apply("source remove", copy, () => copy.SourceTokens.RemoveAt(index), () => sources.RemoveAt(index));
        }

        public void ClearSources() =>
            Apply("source clear with heredoc", copy, () => copy.SourceTokens.Clear(), sources.Clear);

        public void InsertMount()
        {
            MountState item = NewMount();
            int index = random.Next(mounts.Count + 1);
            Apply("mount insert/repopulate", run, () => run.Mounts.Insert(index, item.Item),
                () => mounts.Insert(index, item));
        }

        public void ReplaceMount()
        {
            MountState item = NewMount();
            int index = random.Next(mounts.Count);
            Apply("mount replace", run, () => run.Mounts[index] = item.Item, () => mounts[index] = item);
        }

        public void MoveMount()
        {
            int old = random.Next(mounts.Count), next = (old + 1) % mounts.Count;
            Apply("mount move", run, () => run.Mounts.Move(old, next), () => Move(mounts, old, next));
        }

        public void ClearMounts() => Apply("mount clear", run, () => run.Mounts.Clear(), mounts.Clear);

        public void RemoveMount()
        {
            int index = random.Next(mounts.Count);
            Apply("mount remove", run, () => run.Mounts.RemoveAt(index), () => mounts.RemoveAt(index));
        }

        public void InsertEntry()
        {
            MountState mount = mounts[random.Next(mounts.Count)];
            EntryState entry = NewEntry();
            int index = random.Next(mount.Entries.Count + 1);
            Apply("nested entry insert", run, () => mount.Item.Entries.Insert(index, entry.Item),
                () => mount.Entries.Insert(index, entry), mount.Item);
        }

        public void ReplaceEntry()
        {
            MountState mount = mounts[random.Next(mounts.Count)];
            EntryState entry = NewEntry();
            int index = random.Next(mount.Entries.Count);
            Apply("nested entry replace", run, () => mount.Item.Entries[index] = entry.Item,
                () => mount.Entries[index] = entry, mount.Item);
        }

        public void MoveEntry()
        {
            MountState mount = mounts[random.Next(mounts.Count)];
            int old = random.Next(mount.Entries.Count), next = (old + 1) % mount.Entries.Count;
            Apply("nested entry move", run, () => mount.Item.Entries.Move(old, next),
                () => Move(mount.Entries, old, next), mount.Item);
        }

        public void RemoveEntry()
        {
            MountState mount = mounts.First(item => item.Entries.Count > 1);
            int index = random.Next(mount.Entries.Count);
            Apply("nested entry remove", run, () => mount.Item.Entries.RemoveAt(index),
                () => mount.Entries.RemoveAt(index), mount.Item);
        }

        public void InsertPair(bool copy)
        {
            var (owner, actual, expected) = Pairs(copy);
            PairState pair = NewPair();
            int index = random.Next(expected.Count + 1);
            Apply("heredoc insert/repopulate " + owner.InstructionName, owner,
                () => actual.Insert(index, pair.Item), () => expected.Insert(index, pair));
        }

        public void ReplacePair(bool copy)
        {
            var (owner, actual, expected) = Pairs(copy);
            PairState pair = NewPair();
            int index = random.Next(expected.Count);
            Apply("heredoc replace " + owner.InstructionName, owner,
                () => actual[index] = pair.Item, () => expected[index] = pair);
        }

        public void MovePair(bool copy)
        {
            var (owner, actual, expected) = Pairs(copy);
            int old = random.Next(expected.Count), next = (old + 1) % expected.Count;
            Apply("heredoc move " + owner.InstructionName, owner,
                () => actual.Move(old, next), () => Move(expected, old, next));
        }

        public void RemovePair()
        {
            int index = random.Next(copyPairs.Count);
            Apply("heredoc remove COPY", copy, () => copy.Heredocs.RemoveAt(index), () => copyPairs.RemoveAt(index));
        }

        public void ClearPairs(bool copy)
        {
            var (owner, actual, expected) = Pairs(copy);
            Apply("heredoc clear " + owner.InstructionName, owner, () => actual.Clear(), expected.Clear);
        }

        public void AddComment(bool copy)
        {
            var (owner, expected) = Comments(copy);
            string value = "note" + random.Next(3);
            var item = new CommentToken(value);
            Apply("comment add " + owner.InstructionName, owner, () => owner.CommentTokens.Add(item),
                () => expected.Add(new(item, value)));
        }

        public void ReplaceComment()
        {
            int index = random.Next(runComments.Count);
            string value = "note" + random.Next(3);
            var item = new CommentToken(value);
            Apply("comment replace", run, () => run.CommentTokens[index] = item,
                () => runComments[index] = new(item, value));
        }

        public void MoveComment()
        {
            int old = random.Next(copyComments.Count), next = (old + 1) % copyComments.Count;
            Apply("comment move", copy, () => copy.CommentTokens.Move(old, next),
                () => Move(copyComments, old, next));
        }

        public void RemoveComment(bool copy)
        {
            var (owner, expected) = Comments(copy);
            int index = random.Next(expected.Count);
            Apply("comment remove " + owner.InstructionName, owner,
                () => owner.CommentTokens.RemoveAt(index), () => expected.RemoveAt(index));
        }

        public void ClearComments(bool copy)
        {
            var (owner, expected) = Comments(copy);
            Apply("comment clear " + owner.InstructionName, owner, () => owner.CommentTokens.Clear(), expected.Clear);
        }

        public void RejectAliasedSource() =>
            Reject("reject destination adoption as source", () => copy.SourceTokens.Add(destination), destination);

        public void RejectDuplicatePair() =>
            Reject("reject duplicate heredoc adoption", () => copy.Heredocs.Add(copyPairs[0].Item));

        public void RejectDuplicateEntry()
        {
            MountState mount = mounts[random.Next(mounts.Count)];
            MountEntry entry = mount.Entries[0].Item;
            Reject("reject duplicate nested entry", () => mount.Item.Entries.Add(entry),
                (Token?)entry.KeyValueToken ?? entry.KeyToken);
        }

        public void RejectIncompatibleMount()
        {
            var incoming = Mount.Parse("target=/alien,readonly", escape == '`' ? '\\' : '`');
            Reject("reject incompatible typed mount", () => run.Mounts.Add(incoming), incoming);
        }

        public void RejectLastCopySourceRemoval() =>
            Reject("reject removing final COPY source kind", () => copy.Heredocs.Clear());

        public void RejectEmptyMount()
        {
            MountState mount = mounts[random.Next(mounts.Count)];
            Reject("reject empty required mount entries", () => mount.Item.Entries.Clear());
        }

        private void Apply(string name, Instruction owner, Action edit, Action update, Mount? changedMount = null)
        {
            output.WriteLine($"{++Calls}: {name}");
            var sibling = new TreeSnapshot(new Token[] { ReferenceEquals(owner, copy) ? run : copy });
            var untouched = new TreeSnapshot(ProtectedRoots(changedMount));
            edit();
            update();
            sibling.AssertUnchanged();
            untouched.AssertUnchanged();
            Verify();
        }

        private void Reject(string name, Action edit, params Token[] incoming)
        {
            output.WriteLine($"{++Calls}: {name}");
            string text = file.ToString();
            var snapshot = new TreeSnapshot(file.Items.Cast<Token>().Concat(incoming));
            Assert.Throws<InvalidOperationException>(edit);
            Assert.Equal(text, file.ToString());
            snapshot.AssertUnchanged();
            RejectedCalls++;
            Verify();
        }

        private IEnumerable<Token> ProtectedRoots(Mount? changedMount) =>
            new Token[] { destination, from, network, copy.InstructionNameToken, run.InstructionNameToken }
                .Concat(sources.Select(item => item.Item))
                .Concat(copy.HeredocMarkerTokens).Concat(copy.HeredocBodyTokens)
                .Concat(run.HeredocMarkerTokens).Concat(run.HeredocBodyTokens)
                .Concat(copyComments.Select(item => item.Item)).Concat(runComments.Select(item => item.Item))
                .Concat(mounts.SelectMany(item => item.Entries)
                    .Select(entry => (Token?)entry.Item.KeyValueToken ?? entry.Item.KeyToken))
                .Concat(fixedContinuations)
                .Concat(mounts.Where(item => !ReferenceEquals(item.Item, changedMount)).Select(item => item.Item));

        public void Verify()
        {
            Assert.Equal(documentItems.Length, file.Items.Count);
            for (int i = 0; i < documentItems.Length; i++) Assert.Same(documentItems[i], file.Items[i]);
            siblings.AssertUnchanged();
            Assert.Same(destination, copy.DestinationToken);
            Assert.Same(from, copy.FromStageNameToken);
            Assert.Same(network, run.NetworkToken);
            foreach (LineContinuationToken token in fixedContinuations)
                Assert.Contains(token, copy.Tokens.Concat(run.Tokens));
            Check(copy, run, identities: true);
            Dockerfile parsed = Parse(file.ToString());
            Assert.Equal(file.ToString(), parsed.ToString());
            Check(Assert.Single(parsed.Items.OfType<CopyInstruction>()),
                parsed.Items.OfType<RunInstruction>().First(), identities: false);
        }

        private void Check(CopyInstruction copy, RunInstruction run, bool identities)
        {
            Assert.Equal("/dst", copy.Destination);
            Assert.Equal("base", copy.FromStageName);
            Assert.Equal("none", run.Network);
            Assert.Equal(sources.Select(item => item.Value), copy.Sources);
            Assert.Equal(sources.Count, copy.SourceTokens.Count);
            if (identities)
                for (int i = 0; i < sources.Count; i++) Assert.Same(sources[i].Item, copy.SourceTokens[i]);
            CheckPairs(copyPairs, copy.Heredocs, copy.HeredocMarkerTokens, copy.HeredocBodyTokens, identities);
            CheckPairs(runPairs, run.Heredocs, run.HeredocMarkerTokens, run.HeredocBodyTokens, identities);
            CheckComments(copyComments, copy, identities);
            CheckComments(runComments, run, identities);
            Assert.Equal(mounts.Count, run.Mounts.Count);
            for (int i = 0; i < mounts.Count; i++)
            {
                MountState expected = mounts[i];
                Mount actual = run.Mounts[i];
                if (identities) Assert.Same(expected.Item, actual);
                Assert.Equal(expected.Entries.Count, actual.Entries.Count);
                for (int j = 0; j < expected.Entries.Count; j++)
                {
                    EntryState entry = expected.Entries[j];
                    if (identities) Assert.Same(entry.Item, actual.Entries[j]);
                    Assert.Equal(entry.Key, actual.Entries[j].Key);
                    Assert.Equal(entry.Value, actual.Entries[j].Value);
                    Assert.Equal(entry.Value is null, actual.Entries[j].IsBareKeyword);
                }
            }
            if (runPairs.Count == 0)
                Assert.Equal("cat", Assert.IsType<ShellFormCommand>(run.Command).Value.Trim());
            else
            {
                Assert.Null(run.Command);
                Assert.Equal("cat", string.Concat(run.Tokens.Where(token => token is LiteralToken or StringToken)
                    .Select(token => token.ToString(TokenStringOptions.CreateOptionsForValueString()))).Trim());
            }
        }

        private void CheckPairs(List<PairState> expected, EditableList<Heredoc> actual,
            IEnumerable<HeredocMarkerToken> markers, IEnumerable<HeredocBodyToken> bodies, bool identities)
        {
            Assert.Equal(expected.Count, actual.Count);
            var markerArray = markers.ToArray();
            var bodyArray = bodies.ToArray();
            Assert.Equal(expected.Count, markerArray.Length);
            Assert.Equal(expected.Count, bodyArray.Length);
            for (int i = 0; i < expected.Count; i++)
            {
                PairState pair = expected[i];
                if (identities) Assert.Same(pair.Item, actual[i]);
                Assert.Equal(pair.Name, actual[i].Name);
                Assert.Equal(pair.Raw, actual[i].RawContent);
                Assert.Equal(pair.Chomp
                    ? string.Join("\n", pair.Raw.Split('\n').Select(line => line.TrimStart('\t')))
                    : pair.Raw, actual[i].Content);
                Assert.Equal(pair.Chomp, actual[i].Chomp);
                Assert.Equal(pair.Quote == HeredocQuoteKind.Unquoted, actual[i].Expand);
                string quote = pair.Quote == HeredocQuoteKind.SingleQuoted ? "'" :
                    pair.Quote == HeredocQuoteKind.DoubleQuoted ? "\"" : "";
                Assert.Equal("<<" + (pair.Chomp ? "-" : "") + quote + pair.Name + quote, markerArray[i].ToString());
                Assert.Equal(pair.Raw + pair.Name + newline, bodyArray[i].ToString());
            }
        }

        private static void CheckComments(List<CommentState> expected, Instruction owner, bool identities)
        {
            Assert.Equal(expected.Select(item => item.Value), owner.Comments);
            Assert.Equal(expected.Count, owner.CommentTokens.Count);
            if (identities)
                for (int i = 0; i < expected.Count; i++) Assert.Same(expected[i].Item, owner.CommentTokens[i]);
        }

        private MountState NewMount()
        {
            string value = "/cache" + random.Next(3);
            Mount item = Mount.Parse("target=" + value + ",source=,readonly,target=/duplicate", escape);
            return new(item, new()
            {
                new(item.Entries[0], "target", value), new(item.Entries[1], "source", ""),
                new(item.Entries[2], "readonly", null), new(item.Entries[3], "target", "/duplicate")
            });
        }

        private EntryState NewEntry()
        {
            string key = random.Next(2) == 0 ? "target" : "source";
            string? value = random.Next(3) switch { 0 => null, 1 => "", _ => "/value" };
            return new(new MountEntry(key, value, escape), key, value);
        }

        private PairState NewPair()
        {
            string raw = "\t# payload " + random.Next(3) + payloadNewline + "data" + newline;
            var quote = (HeredocQuoteKind)random.Next(3);
            bool chomp = random.Next(2) == 0;
            return new(new Heredoc("D", raw, quote, chomp, escape), "D", raw, quote, chomp);
        }

        private (Instruction Owner, EditableList<Heredoc> Actual, List<PairState> Expected) Pairs(bool isCopy) =>
            isCopy ? (copy, copy.Heredocs, copyPairs) : (run, run.Heredocs, runPairs);

        private (Instruction Owner, List<CommentState> Expected) Comments(bool isCopy) =>
            isCopy ? (copy, copyComments) : (run, runComments);

        private static void Move<T>(List<T> items, int old, int next)
        {
            T item = items[old];
            items.RemoveAt(old);
            items.Insert(next, item);
        }

        private static Dockerfile Parse(string text)
        {
            DockerfileParseResult result = Dockerfile.TryParse(text);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            return Assert.IsType<Dockerfile>(result.Dockerfile);
        }
    }

    private sealed class TreeSnapshot
    {
        private readonly List<(Token Item, string Text, Token[] Children)> nodes = new();

        public TreeSnapshot(IEnumerable<Token> roots)
        {
            foreach (Token token in roots) Capture(token);
        }

        public void AssertUnchanged()
        {
            foreach (var node in nodes)
            {
                Assert.Equal(node.Text, node.Item.ToString());
                if (node.Item is AggregateToken aggregate)
                {
                    Token[] actual = aggregate.Tokens.ToArray();
                    Assert.Equal(node.Children.Length, actual.Length);
                    for (int i = 0; i < actual.Length; i++) Assert.Same(node.Children[i], actual[i]);
                }
            }
        }

        private void Capture(Token token)
        {
            Token[] children = token is AggregateToken aggregate ? aggregate.Tokens.ToArray() : Array.Empty<Token>();
            nodes.Add((token, token.ToString(), children));
            foreach (Token child in children) Capture(child);
        }
    }
}
