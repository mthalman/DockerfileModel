using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Protects paired heredoc editing, raw payload fidelity, and the grammar constraints of owning instructions.
/// </summary>
public class HeredocStructuralEditingTests
{
    /// <summary>
    /// Equal-comparing source and destination tokens do not move a new heredoc marker before the source.
    /// </summary>
    [Fact]
    public void FirstCopyHeredocLocatesDestinationByReference()
    {
        var copy = CopyInstruction.Parse("COPY same same\n");
        var source = new EqualHeredocOperand();
        var destination = new EqualHeredocOperand();
        copy.DestinationToken = destination;
        copy.SourceTokens[0] = source;
        var pair = new Heredoc("DOC", "body\n");
        int sourceEqualityCalls = source.EqualsCalls;
        int destinationEqualityCalls = destination.EqualsCalls;

        copy.Heredocs.Add(pair);

        Assert.Equal("COPY same <<DOC same\nbody\nDOC\n", copy.ToString());
        Assert.Same(source, Assert.Single(copy.SourceTokens));
        Assert.Same(destination, copy.DestinationToken);
        Assert.Same(pair, Assert.Single(copy.Heredocs));
        Assert.Equal(0, source.EqualsCalls - sourceEqualityCalls);
        Assert.Equal(0, destination.EqualsCalls - destinationEqualityCalls);
        var parsed = Dockerfile.TryParse(copy.ToString());
        Assert.True(parsed.Success);
        Assert.Equal(copy.ToString(), parsed.Dockerfile!.ToString());
    }

    /// <summary>
    /// Extracting a command's final newline removes that exact boundary, never an earlier equal-comparing text token.
    /// </summary>
    [Fact]
    public void FirstRunHeredocExtractsExactCommandBoundary()
    {
        var text = new BoundaryEqualText();
        var newline = new NewLineToken("\n");
        var builder = new TokenBuilder();
        builder.ShellFormCommand(command => command.Literal(literal =>
        {
            literal.Tokens.Add(text);
            literal.Tokens.Add(newline);
        }));
        var command = Assert.IsType<ShellFormCommand>(Assert.Single(builder.Tokens));
        var run = RunInstruction.Parse("RUN placeholder");
        run.Command = command;
        var pair = new Heredoc("DOC", "body\n");

        run.Heredocs.Add(pair);

        Assert.Equal("RUN <<DOC cat\nbody\nDOC\n", run.ToString());
        Assert.Same(text, Assert.Single(command.ValueToken.Tokens));
        Assert.Same(command.ValueToken, Assert.Single(run.Tokens.OfType<LiteralToken>()));
        Assert.Same(newline, Assert.Single(run.Tokens.OfType<NewLineToken>()));
        Assert.Same(pair, Assert.Single(run.Heredocs));
        Assert.Equal(0, text.EqualsCalls);
        var parsed = Dockerfile.TryParse(run.ToString());
        Assert.True(parsed.Success);
        Assert.Equal(run.ToString(), parsed.Dockerfile!.ToString());
    }

    /// <summary>
    /// Heredoc write admission rejects unsupported header serializers without evaluating them, including no-op writes.
    /// </summary>
    /// <param name="operation">The collection facade entry point to exercise.</param>
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
    public void HeredocWritesAdmitTreeBeforeBookkeeping(string operation)
    {
        var run = Dockerfile.TryParse("RUN --network=none cat <<A <<B\none\nA\ntwo\nB\n")
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var poison = new MutatingHeredocHeaderValue();
        run.NetworkToken = poison;
        var pairs = run.Heredocs.ToArray();
        var incoming = new Heredoc("DOC", "body\n");
        var ownerTree = Descendants(run).ToArray();
        var incomingTree = Descendants(incoming.Marker).Concat(Descendants(incoming.Body)).ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            switch (operation)
            {
                case "add": run.Heredocs.Add(incoming); break;
                case "insert": run.Heredocs.Insert(1, incoming); break;
                case "replace": run.Heredocs[1] = incoming; break;
                case "replace-item": run.Heredocs.ReplaceItem(pairs[1], incoming); break;
                case "remove": run.Heredocs.Remove(pairs[1]); break;
                case "remove-missing": run.Heredocs.Remove(incoming); break;
                case "remove-at": run.Heredocs.RemoveAt(1); break;
                case "clear": run.Heredocs.Clear(); break;
                case "move": run.Heredocs.Move(1, 0); break;
                case "same-index-move": run.Heredocs.Move(0, 0); break;
                case "same-item-replace": run.Heredocs[0] = pairs[0]; break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        });

        Assert.Contains("built-in", exception.Message);
        Assert.Equal(0, poison.Calls);
        Assert.Equal("none", Assert.IsType<StringToken>(Assert.Single(poison.Tokens)).Value);
        Assert.Equal(pairs, run.Heredocs);
        Assert.Equal(ownerTree, Descendants(run));
        Assert.Equal(incomingTree, Descendants(incoming.Marker).Concat(Descendants(incoming.Body)));
    }

    private sealed class EqualHeredocOperand() : LiteralToken("same")
    {
        public int EqualsCalls { get; private set; }

        public override bool Equals(object? obj)
        {
            EqualsCalls++;
            return obj is LiteralToken other && Value == other.Value;
        }

        public override int GetHashCode() => 0;
    }

    private sealed class BoundaryEqualText() : StringToken("cat")
    {
        public int EqualsCalls { get; private set; }

        public override bool Equals(object? obj)
        {
            EqualsCalls++;
            return ReferenceEquals(this, obj) || obj is NewLineToken;
        }

        public override int GetHashCode() => 0;
    }

    private sealed class MutatingHeredocHeaderValue() : LiteralToken("none")
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
    /// Verifies explicit discard can change chomp mode and remove closing indentation without mutating the detached pair.
    /// </summary>
    [Fact]
    public void PairReplacementChangesNameQuoteAndChomp()
    {
        RunInstruction run = RunInstruction.Parse("RUN <<-EOF\n\ttext\n\tEOF\n");
        Heredoc pair = run.Heredocs[0];
        Assert.Same(pair, run.Heredocs[0]);
        Assert.Equal(0, run.Heredocs.IndexOf(pair));
        Assert.Equal("\ttext\n", pair.RawContent);
        Assert.Equal("text\n", pair.Content);
        var replacement = new Heredoc("END", pair.RawContent, HeredocQuoteKind.SingleQuoted);
        run.Heredocs.Replace(0, replacement, TriviaDisposition.Discard);
        Assert.Equal("RUN <<'END'\n\ttext\nEND\n", run.ToString());
        Assert.Same(replacement, run.Heredocs[0]);
        Assert.False(replacement.Chomp);
        Assert.False(replacement.Expand);
        Assert.Equal("\ttext\n", replacement.Content);
        Assert.Equal("EOF", pair.Name);
        Assert.True(pair.Chomp);
    }

    /// <summary>
    /// Rejects delimiter collisions and unterminated raw body lines without changing the current pair.
    /// </summary>
    [Fact]
    public void InvalidConstructedReplacementsLeaveCollectionUnchanged()
    {
        var run = RunInstruction.Parse("RUN <<-EOF\n\tEND\nEOF\n");
        var pair = run.Heredocs[0];
        string before = run.ToString();
        Assert.Throws<InvalidOperationException>(() =>
            run.Heredocs[0] = new Heredoc("END", pair.RawContent, chomp: true));
        Assert.Throws<ArgumentException>(() =>
            run.Heredocs[0] = new Heredoc("EOF", "missing newline"));
        Assert.Equal(before, run.ToString());
        Assert.Same(pair, run.Heredocs[0]);
        Assert.Equal("\tEND\n", pair.RawContent);
    }

    /// <summary>
    /// Exercises pair collection edits and restoration of the shell command after the final pair is removed.
    /// </summary>
    [Fact]
    public void AddMoveReplaceRemovePairs()
    {
        RunInstruction run = RunInstruction.Parse("RUN cat\n");
        var a = new Heredoc("A", "one\n");
        var b = new Heredoc("B", "two\n");
        run.Heredocs.Add(a);
        run.Heredocs.Add(b);
        Assert.Same(a, run.Heredocs[0]);
        run.Heredocs.Move(0, 1);
        Assert.Equal("B", run.Heredocs[0].Name);
        Assert.Equal("two\n", run.Heredocs[0].RawContent);
        run.Heredocs[0] = new Heredoc("C", "three\n");
        Assert.Equal("C", run.Heredocs[0].Name);
        run.Heredocs.Clear();
        Assert.Empty(run.Heredocs);
        Assert.NotNull(run.Command);
        Assert.Contains("cat", run.ToString());
    }

    /// <summary>
    /// Prevents empty marker-only commands and implicit conversion of exec-form commands to heredoc syntax.
    /// </summary>
    [Fact]
    public void MarkerOnlyAndJsonRemovalAreRejected()
    {
        RunInstruction run = RunInstruction.Parse("RUN <<EOF\nhello\nEOF\n");
        string text = run.ToString();
        Assert.Throws<InvalidOperationException>(() => run.Heredocs.Clear());
        Assert.Equal(text, run.ToString());
        run = new RunInstruction("echo", new[] { "hello" });
        text = run.ToString();
        Assert.Throws<InvalidOperationException>(() => run.Heredocs.Add(new Heredoc("EOF", "")));
        Assert.Equal(text, run.ToString());
    }

    /// <summary>
    /// Allows removal of heredoc sources only when a regular source still satisfies file-transfer cardinality.
    /// </summary>
    [Fact]
    public void FileTransferSourceCardinality()
    {
        CopyInstruction copy = CopyInstruction.Parse("COPY regular /dest\n");
        copy.Heredocs.Add(new Heredoc("EOF", "body\n"));
        Assert.Equal("regular", copy.Sources.Single());
        Assert.Equal("/dest", copy.Destination);
        copy.Heredocs.Clear();
        Assert.Empty(copy.Heredocs);
        copy = CopyInstruction.Parse("COPY <<EOF /dest\nbody\nEOF\n");
        string text = copy.ToString();
        Assert.Throws<InvalidOperationException>(() => copy.Heredocs.Clear());
        Assert.Equal(text, copy.ToString());
    }

    /// <summary>
    /// Applies the owner's newline style to generated boundaries without normalizing raw body content.
    /// </summary>
    /// <param name="newline">The owner's line separator for newly created heredoc boundaries.</param>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void AddedBoundariesFollowOwnerButNotRawBody(string newline)
    {
        var run = RunInstruction.Parse("RUN cat" + newline);
        var pair = new Heredoc("EOF", "raw\n");
        run.Heredocs.Add(pair);
        Assert.Equal("RUN <<EOF cat" + newline + "raw\nEOF" + newline, run.ToString());
        Assert.Same(pair, run.Heredocs[0]);
        Assert.Equal("raw\n", pair.RawContent);
    }

    /// <summary>
    /// Covers spaced delimiter names and expansion semantics under a nondefault Dockerfile escape context.
    /// </summary>
    /// <param name="quote">The marker quoting style used for the original and replacement pairs.</param>
    [Theory]
    [InlineData(HeredocQuoteKind.Unquoted)]
    [InlineData(HeredocQuoteKind.SingleQuoted)]
    [InlineData(HeredocQuoteKind.DoubleQuoted)]
    public void ComplexNamesAndFixedBackslashGrammar(HeredocQuoteKind quote)
    {
        var run = RunInstruction.Parse("RUN cat", '`');
        var pair = new Heredoc("END WORD", "body\n", quote, escapeChar: '`');
        run.Heredocs.Add(pair);
        Assert.Equal("END WORD", pair.Name);
        Assert.Equal(quote == HeredocQuoteKind.Unquoted, pair.Expand);
        var replacement = new Heredoc("OTHER WORD", pair.RawContent, quote, escapeChar: '`');
        run.Heredocs[0] = replacement;
        Assert.Same(replacement, run.Heredocs[0]);
        Assert.Equal("END WORD", pair.Name);
        Assert.Equal("OTHER WORD", replacement.Name);
        Assert.EndsWith("OTHER WORD", run.ToString());
    }

    /// <summary>
    /// Ensures identical delimiter names do not confuse pair ordering or turn body comments into preserved trivia.
    /// </summary>
    [Fact]
    public void DuplicateDelimitersArePositionalAndBodiesStayPayload()
    {
        var run = Dockerfile.TryParse("RUN cat <<EOF <<EOF\n# payload one\nEOF\n# payload two\nEOF\n")
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var first = run.Heredocs[0];
        var second = run.Heredocs[1];
        run.Heredocs.Move(0, 1);
        Assert.Same(second, run.Heredocs[0]);
        Assert.Same(first, run.Heredocs[1]);
        Assert.Equal("# payload two\n", run.Heredocs[0].RawContent);
        run.Heredocs.RemoveAt(1);
        Assert.DoesNotContain("# payload one", run.ToString());
    }

    /// <summary>
    /// Rejects collection mutation when an unterminated body prevents a safe marker-to-body mapping.
    /// </summary>
    [Fact]
    public void MalformedPairsCannotBeMutated()
    {
        var run = RunInstruction.Parse("RUN <<EOF\nunterminated\n");
        string before = run.ToString();
        Assert.ThrowsAny<Exception>(() => run.Heredocs.Add(new Heredoc("END", "")));
        Assert.Equal(before, run.ToString());
    }

    /// <summary>
    /// Keeps unselected pair wrappers and their marker and body tokens stable during another pair's replacement.
    /// </summary>
    [Fact]
    public void PairReplacementRetainsUnselectedPairAggregates()
    {
        var run = RunInstruction.Parse("RUN <<\"EOF\" <<OTHER\nold\nEOF\nunchanged\nOTHER\n");
        var marker = run.HeredocMarkerTokens.Last();
        var body = run.HeredocBodyTokens.Last();
        var unselected = run.Heredocs[1];
        var replacement = new Heredoc("END", "new\n", HeredocQuoteKind.DoubleQuoted);
        run.Heredocs[0] = replacement;
        Assert.Same(replacement, run.Heredocs[0]);
        Assert.Same(unselected, run.Heredocs[1]);
        Assert.Same(marker, run.HeredocMarkerTokens.Last());
        Assert.Same(body, run.HeredocBodyTokens.Last());
        Assert.Equal("RUN <<\"END\" <<OTHER\nnew\nEND\nunchanged\nOTHER\n", run.ToString());
    }

    /// <summary>
    /// Preserves regular source and destination tokens when removing heredocs from a diagnostically parsed instruction.
    /// </summary>
    [Fact]
    public void MixedDiagnosticSourcesSurviveHeredocRemoval()
    {
        var copy = Dockerfile.TryParse("COPY regular <<EOF /dest\nbody\nEOF\n")
            .Dockerfile!.Items.OfType<CopyInstruction>().Single();
        var source = copy.SourceTokens[0];
        var destination = copy.DestinationToken;
        copy.Heredocs.Clear();
        Assert.Same(source, copy.SourceTokens[0]);
        Assert.Same(destination, copy.DestinationToken);
        Assert.Equal("regular", copy.Sources.Single());
        Assert.Equal("/dest", copy.Destination);
    }

    /// <summary>
    /// Verifies rejected duplicate adoption and required-source removal leave existing pair wrappers intact.
    /// </summary>
    [Fact]
    public void CollectionFailuresRetainPairIdentities()
    {
        var run = RunInstruction.Parse("RUN <<EOF\nbody\nEOF\n");
        var pair = run.Heredocs[0];
        string before = run.ToString();
        Assert.Throws<InvalidOperationException>(() => run.Heredocs.Insert(0, pair));
        Assert.Throws<InvalidOperationException>(() => run.Heredocs.Clear());
        Assert.Equal(before, run.ToString());
        Assert.Same(pair, run.Heredocs[0]);
    }

    /// <summary>
    /// Guards against losing header comments or replacing the command leaf when entering heredoc form.
    /// </summary>
    [Fact]
    public void FirstHeredocRetainsHeaderCommentAndExistingCommandLeaf()
    {
        var run = RunInstruction.Parse("RUN cat # retained\n");
        var command = Assert.IsType<ShellFormCommand>(run.Command);
        var leaf = command.ValueToken.Tokens.First();
        run.Heredocs.Add(new Heredoc("EOF", "body\n"));
        Assert.Contains("# retained", run.ToString());
        Assert.Contains(leaf, run.Tokens.OfType<Valleysoft.DockerfileModel.Tokens.LiteralToken>().First().Tokens);
        Assert.Equal("body\n", run.Heredocs[0].RawContent);
    }

    /// <summary>
    /// Provides command spellings whose hashes and pipelines must not select different textual marker positions.
    /// </summary>
    public static IEnumerable<object[]> FirstHeredocPrefixCases()
    {
        foreach (char escapeChar in new[] { '\\', '`' })
        foreach (string newline in new[] { "\n", "\r\n" })
        foreach (string command in new[]
        {
            "printf '%s' x | cat",
            "printf '#%s' x | cat",
            "echo ${x#p} | cat",
            "cat # retained",
            "echo x && cat",
            "printf '%s' " + escapeChar + newline + "x | cat"
        })
            yield return new object[] { escapeChar, newline, command };
    }

    /// <summary>
    /// Prefixes the first marker after header trivia and flags without interpreting shell syntax or replacing command leaves.
    /// </summary>
    /// <param name="escapeChar">The instruction's Dockerfile escape character.</param>
    /// <param name="newline">The structural header and closing-delimiter newline style.</param>
    /// <param name="commandText">Opaque shell text whose execution semantics are outside this placement contract.</param>
    [Theory]
    [MemberData(nameof(FirstHeredocPrefixCases))]
    public void FirstHeredocUsesTextualPrefixRegardlessOfShellHashes(char escapeChar, string newline, string commandText)
    {
        string directive = escapeChar == '`' ? "# escape=`" + newline : "";
        string header = "RUN " + escapeChar + newline + "# header" + newline +
            "--network=none --mount=target=/cache ";
        var run = Dockerfile.TryParse(directive + header + commandText + newline)
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var command = Assert.IsType<ShellFormCommand>(run.Command).ValueToken;
        string commandValue = command.Value;
        var leaves = Descendants(command).ToArray();
        var network = run.NetworkToken;
        var mount = Assert.Single(run.Mounts);
        var comment = Assert.Single(run.CommentTokens);
        string commentText = comment.ToString();
        var pair = new Heredoc("DOC", "body\r\n", escapeChar: escapeChar);

        run.Heredocs.Add(pair);

        Assert.Equal(header + "<<DOC " + commandText + newline + "body\r\nDOC" + newline, run.ToString());
        Assert.Same(pair, Assert.Single(run.Heredocs));
        Assert.Same(network, run.NetworkToken);
        Assert.Same(mount, Assert.Single(run.Mounts));
        Assert.Same(comment, Assert.Single(run.CommentTokens));
        Assert.Equal(commentText, comment.ToString());
        Assert.Contains(command, run.Tokens);
        Assert.Equal(commandValue, command.Value);
        foreach (Token leaf in leaves) Assert.Contains(leaf, Descendants(run));
        Assert.Null(run.Command);
        var result = Dockerfile.TryParse(directive + run);
        Assert.True(result.Success);
        var parsed = Assert.Single(result.Dockerfile!.Items.OfType<RunInstruction>());
        Assert.Equal(run.ToString(), parsed.ToString());
        Assert.Equal("none", parsed.Network);
        Assert.Equal(mount.ToString(), Assert.Single(parsed.Mounts).ToString());
        Assert.Equal(" " + commandText + newline, string.Concat(parsed.Tokens
            .SkipWhile(token => token is not HeredocMarkerToken).Skip(1)
            .TakeWhile(token => token is not HeredocBodyToken).Select(token => token.ToString())));
        Assert.Equal("body\r\n", Assert.Single(parsed.Heredocs).RawContent);
    }

    /// <summary>
    /// Keeps unsupported command-internal continuation comments atomic instead of weakening pair-association validation.
    /// </summary>
    /// <param name="escapeChar">The original continuation's escape character.</param>
    /// <param name="newline">The original physical line ending.</param>
    [Theory]
    [InlineData('\\', "\n")]
    [InlineData('`', "\r\n")]
    public void FirstHeredocRejectsUnmappableInternalContinuationComments(char escapeChar, string newline)
    {
        string directive = escapeChar == '`' ? "# escape=`" + newline : "";
        string text = "RUN cat " + escapeChar + newline + "# note" + newline + "arg" + newline;
        var run = Dockerfile.TryParse(directive + text).Dockerfile!.Items.OfType<RunInstruction>().Single();
        var incoming = new Heredoc("DOC", "body\n", escapeChar: escapeChar);
        var ownerTree = Descendants(run).Select(token => (token, text: token.ToString())).ToArray();
        var incomingTree = Descendants(incoming.Marker).Concat(Descendants(incoming.Body))
            .Select(token => (token, text: token.ToString())).ToArray();

        Assert.Throws<InvalidOperationException>(() => run.Heredocs.Add(incoming));

        Assert.Equal(ownerTree, Descendants(run).Select(token => (token, text: token.ToString())));
        Assert.Equal(incomingTree, Descendants(incoming.Marker).Concat(Descendants(incoming.Body))
            .Select(token => (token, text: token.ToString())));
        Assert.Empty(run.Heredocs);
    }

    /// <summary>
    /// Editing existing pairs keeps an explicitly parsed suffix position rather than applying the first-insertion prefix policy.
    /// </summary>
    /// <param name="commandText">An opaque pipeline with or without a quoted hash.</param>
    [Theory]
    [InlineData("printf '%s' x | cat")]
    [InlineData("printf '#%s' x | cat")]
    public void ExistingHeredocEditsRetainExplicitSuffixPosition(string commandText)
    {
        var run = Dockerfile.TryParse("RUN " + commandText + " <<A\none\nA\n")
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var commandTokens = run.Tokens.OfType<LiteralToken>().ToArray();
        var original = Assert.Single(run.Heredocs);
        var added = new Heredoc("B", "two\n");
        run.Heredocs.Add(added);
        Assert.Equal("RUN " + commandText + " <<A <<B\none\nA\ntwo\nB\n", run.ToString());

        run.Heredocs.Move(0, 1);
        Assert.Equal("RUN " + commandText + " <<B <<A\ntwo\nB\none\nA\n", run.ToString());
        var replacement = new Heredoc("C", "three\n");
        run.Heredocs[1] = replacement;

        Assert.Equal("RUN " + commandText + " <<B <<C\ntwo\nB\nthree\nC\n", run.ToString());
        Assert.Equal(commandTokens, run.Tokens.OfType<LiteralToken>());
        Assert.Equal(new[] { added, replacement }, run.Heredocs);
        Assert.Equal("one\n", original.RawContent);
        var result = Dockerfile.TryParse(run.ToString());
        Assert.True(result.Success);
        Assert.Equal(run.ToString(), result.Dockerfile!.ToString());
    }

    /// <summary>
    /// Complete pairs retain their tokens and raw bytes when transferred between owners with different Dockerfile escape contexts.
    /// </summary>
    /// <param name="sourceEscape">The construction and initial instruction context.</param>
    /// <param name="targetEscape">The receiving instruction context.</param>
    /// <param name="targetIsRun">Whether the receiving instruction is RUN rather than COPY.</param>
    [Theory]
    [InlineData('\\', '`', false)]
    [InlineData('`', '\\', false)]
    [InlineData('\\', '`', true)]
    [InlineData('`', '\\', true)]
    public void CompletePairsTransferAcrossContextsWithoutReadMutation(char sourceEscape, char targetEscape, bool targetIsRun)
    {
        var source = RunInstruction.Parse("RUN cat\n", sourceEscape);
        Instruction target = targetIsRun ? RunInstruction.Parse("RUN cat\n", targetEscape) :
            CopyInstruction.Parse("COPY regular /out\n", targetEscape);
        var targetPairs = target is RunInstruction run ? run.Heredocs : ((CopyInstruction)target).Heredocs;
        var pair = new Heredoc("END WORD", "\t$VALUE\r\n# raw\n", chomp: true, escapeChar: sourceEscape);
        var marker = pair.Marker;
        var body = pair.Body;
        var contexts = Descendants(marker).Concat(Descendants(body)).OfType<AggregateToken>()
            .Select(token => (token, token.EditingEscapeChar)).ToArray();
        source.Heredocs.Add(pair);
        var pairTree = Descendants(marker).Concat(Descendants(body))
            .Select(token => (token, text: token.ToString())).ToArray();
        source.Heredocs.Remove(pair);

        targetPairs.Add(pair);

        string beforeRead = target.ToString();
        var targetTree = Descendants(target).ToArray();
        int count = targetPairs.Count;
        Assert.Equal(1, count);
        Assert.Equal(0, targetPairs.IndexOf(pair));
        Assert.Contains(pair, targetPairs);
        Assert.Same(pair, Assert.Single(targetPairs));
        Assert.Same(marker, Assert.Single(target.Tokens.OfType<HeredocMarkerToken>()));
        Assert.Same(body, Assert.Single(target.Tokens.OfType<HeredocBodyToken>()));
        Assert.Equal(contexts, Descendants(marker).Concat(Descendants(body)).OfType<AggregateToken>()
            .Select(token => (token, token.EditingEscapeChar)));
        Assert.Equal(pairTree, Descendants(marker).Concat(Descendants(body))
            .Select(token => (token, text: token.ToString())));
        Assert.Equal(beforeRead, target.ToString());
        Assert.Equal(targetTree, Descendants(target));
        Assert.Equal("\t$VALUE\r\n# raw\n", pair.RawContent);
        Assert.Equal("$VALUE\r\n# raw\n", pair.Content);
        string directive = targetEscape == '`' ? "# escape=`\n" : "";
        var result = Dockerfile.TryParse(directive + target);
        Assert.True(result.Success);
        var parsed = Assert.Single(result.Dockerfile!.Items.OfType<Instruction>());
        Assert.Equal(target.ToString(), parsed.ToString());
        if (parsed is CopyInstruction copy)
        {
            Assert.Equal(pair.RawContent, Assert.Single(copy.Heredocs).RawContent);
            Assert.Equal("regular", Assert.Single(copy.Sources));
            Assert.Equal("/out", copy.Destination);
        }
        else
        {
            var parsedRun = Assert.IsType<RunInstruction>(parsed);
            Assert.Equal(pair.RawContent, Assert.Single(parsedRun.Heredocs).RawContent);
            Assert.Equal("cat", Assert.Single(parsedRun.Tokens.OfType<LiteralToken>()).Value);
        }
    }

    /// <summary>
    /// Backslash-escaped unquoted names retain BuildKit parser expansion metadata, not a promise about shell execution.
    /// </summary>
    /// <param name="name">The logical delimiter name.</param>
    /// <param name="spelling">The fixed-backslash unquoted marker spelling, independent of the Dockerfile escape directive.</param>
    [Theory]
    [InlineData("END WORD", "END\\ WORD")]
    [InlineData("END\\WORD", "END\\\\WORD")]
    [InlineData("END'WORD", "END\\'WORD")]
    [InlineData("END\"WORD", "END\\\"WORD")]
    public void EscapedUnquotedNamesRetainParserExpansionMetadata(string name, string spelling)
    {
        foreach (char escapeChar in new[] { '\\', '`' })
        {
            var pair = new Heredoc(name, "$VALUE\n", escapeChar: escapeChar);
            var run = RunInstruction.Parse("RUN cat\n", escapeChar);
            run.Heredocs.Add(pair);

            Assert.True(pair.Expand);
            Assert.False(pair.Marker.IsQuoted);
            Assert.Equal("<<" + spelling, pair.Marker.ToString());
            Assert.Equal("RUN <<" + spelling + " cat\n$VALUE\n" + name + "\n", run.ToString());
            string directive = escapeChar == '`' ? "# escape=`\n" : "";
            var result = Dockerfile.TryParse(directive + run);
            Assert.True(result.Success);
            var parsed = Assert.Single(result.Dockerfile!.Items.OfType<RunInstruction>().Single().Heredocs);
            Assert.Equal(name, parsed.Name);
            Assert.True(parsed.Expand);
            Assert.Equal(pair.RawContent, parsed.RawContent);
        }
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
    /// Ensures leaving heredoc form preserves enough escape-context metadata to adopt a subsequent compatible pair.
    /// </summary>
    [Fact]
    public void RestoredCommandRetainsBacktickEscapeContext()
    {
        var document = Dockerfile.TryParse("# escape=`\nRUN cat <<EOF\nbody\nEOF\n").Dockerfile!;
        var run = document.Items.OfType<RunInstruction>().Single();
        run.Heredocs.Clear();
        var command = Assert.IsType<ShellFormCommand>(run.Command);
        Assert.Equal('`', command.EditingEscapeChar);
        Assert.Equal('`', command.ValueToken.EditingEscapeChar);
        Assert.Equal("cat", command.Value.Trim());
        Assert.Empty(run.Heredocs);
        var replacement = new Heredoc("NEXT", "next\n", escapeChar: '`');
        run.Heredocs.Add(replacement);
        Assert.Same(replacement, Assert.Single(run.Heredocs));
        Assert.Equal("next\n", run.Heredocs[0].RawContent);
    }

    /// <summary>
    /// New separators follow nearby closing-delimiter boundaries, not header or raw payload line endings.
    /// </summary>
    /// <param name="index">The new pair's position among the existing definitions.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void InsertUsesNearestStructuralBodyBoundary(int index)
    {
        var run = Dockerfile.TryParse("RUN <<A <<B\nx\nA\r\ny\r\nB")
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var original = run.Heredocs.ToArray();
        var added = new Heredoc("C", "z\n");

        run.Heredocs.Insert(index, added);

        string expected = index == 1
            ? "RUN <<A <<C <<B\nx\nA\r\nz\nC\r\ny\r\nB"
            : "RUN <<A <<B <<C\nx\nA\r\ny\r\nB\r\nz\nC";
        Assert.Equal(expected, run.ToString());
        Assert.Same(original[0], run.Heredocs[0]);
        Assert.Same(original[1], run.Heredocs[index == 1 ? 2 : 1]);
        Assert.Equal("x\n", original[0].RawContent);
        Assert.Equal("y\r\n", original[1].RawContent);
        Assert.Equal("z\n", added.RawContent);
    }

    /// <summary>
    /// Moving an unterminated final closing line into the body sequence adds only a local structural separator.
    /// </summary>
    [Fact]
    public void MoveUsesNearestStructuralBodyBoundary()
    {
        var run = Dockerfile.TryParse("RUN <<A <<B <<C\nx\nA\r\ny\nB\r\nz\nC")
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var original = run.Heredocs.ToArray();

        run.Heredocs.Move(2, 1);

        Assert.Equal("RUN <<A <<C <<B\nx\nA\r\nz\nC\r\ny\nB\r\n", run.ToString());
        Assert.Same(original[0], run.Heredocs[0]);
        Assert.Same(original[2], run.Heredocs[1]);
        Assert.Same(original[1], run.Heredocs[2]);
        Assert.Equal(new[] { "x\n", "y\n", "z\n" }, original.Select(pair => pair.RawContent));
    }

    /// <summary>
    /// Inserting before a descriptor-bound marker must not split the descriptor from its existing redirection.
    /// </summary>
    [Fact]
    public void InsertRejectsInseparableDescriptorWithoutMutation()
    {
        const string text = "RUN cat 3<<A <&3\none\nA\n";
        var run = Dockerfile.TryParse(text).Dockerfile!.Items.OfType<RunInstruction>().Single();
        var roots = run.Tokens.ToArray();
        var original = Assert.Single(run.Heredocs);
        var incoming = new Heredoc("NEW", "new\n");
        string incomingBody = incoming.Body.ToString();

        Assert.Throws<InvalidOperationException>(() => run.Heredocs.Insert(0, incoming));

        Assert.Equal(text, run.ToString());
        Assert.Equal(roots, run.Tokens);
        Assert.Same(original, Assert.Single(run.Heredocs));
        Assert.Same(original.Marker, Assert.Single(run.HeredocMarkerTokens));
        Assert.Same(original.Body, Assert.Single(run.HeredocBodyTokens));
        Assert.Equal(incomingBody, incoming.Body.ToString());
    }

    /// <summary>
    /// Removing the final pair restores arguments following continuation comments and retains their original leaves.
    /// </summary>
    /// <param name="escapeChar">The instruction's continuation escape character.</param>
    /// <param name="newline">The physical line ending used by continuation and comment trivia.</param>
    /// <param name="argumentText">The surviving argument's original shell spelling, including quotes.</param>
    [Theory]
    [InlineData('\\', "\n", "arg")]
    [InlineData('`', "\r\n", "arg")]
    [InlineData('\\', "\n", "'arg space'")]
    [InlineData('`', "\r\n", "\"$ARG\"")]
    public void ClearRestoresEntireCommandAcrossContinuationComments(char escapeChar, string newline, string argumentText)
    {
        string directive = escapeChar == '`' ? "# escape=`" + newline : "";
        string header = "RUN cat <<EOF " + escapeChar + newline + "# note" + newline + argumentText + newline;
        var run = Dockerfile.TryParse(directive + header + "body" + newline + "EOF" + newline)
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var argument = run.Tokens.OfType<LiteralToken>().Last();
        var argumentLeaves = argument.Tokens.ToArray();
        var comment = Assert.Single(run.CommentTokens);

        run.Heredocs.Clear();

        var command = Assert.IsType<ShellFormCommand>(run.Command);
        var reparsed = Dockerfile.TryParse(directive + run).Dockerfile!.Items.OfType<RunInstruction>().Single();
        Assert.Equal("cat  " + argumentText, command.Value);
        Assert.Equal(Assert.IsType<ShellFormCommand>(reparsed.Command).Value, command.Value);
        foreach (Token leaf in argumentLeaves) Assert.Contains(leaf, command.ValueToken.Tokens);
        Assert.Same(comment, Assert.Single(run.CommentTokens));
        Assert.Equal(header.Replace("<<EOF", ""), run.ToString());
        Assert.Empty(run.Heredocs);
    }

    /// <summary>
    /// Replacing a pair preserves its closing-line terminator rather than inferring from a differently styled header.
    /// </summary>
    /// <param name="headerNewline">The unrelated header's line ending.</param>
    /// <param name="closingNewline">The selected pair's closing-delimiter line ending.</param>
    [Theory]
    [InlineData("\n", "\r\n")]
    [InlineData("\r\n", "\n")]
    public void ReplacePreservesOutgoingClosingNewline(string headerNewline, string closingNewline)
    {
        var run = Dockerfile.TryParse("RUN cat <<EOF" + headerNewline + "body\nEOF" + closingNewline)
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var old = Assert.Single(run.Heredocs);
        var closing = Assert.Single(old.Body.Tokens.OfType<NewLineToken>());
        var replacement = new Heredoc("NEXT", "new\n");

        run.Heredocs[0] = replacement;

        Assert.Equal("RUN cat <<NEXT" + headerNewline + "new\nNEXT" + closingNewline, run.ToString());
        Assert.Same(replacement, Assert.Single(run.Heredocs));
        Assert.Same(closing, Assert.Single(replacement.Body.Tokens.OfType<NewLineToken>()));
        Assert.Equal("new\n", replacement.RawContent);
        Assert.Equal("body\nEOF" + closingNewline, old.Body.ToString());
    }

    /// <summary>
    /// Conflicting replacement terminators reject atomically under Preserve, while Discard selects incoming formatting.
    /// </summary>
    /// <param name="oldNewline">The existing pair's closing-line terminator.</param>
    /// <param name="newNewline">The explicitly supplied replacement's different closing-line terminator.</param>
    [Theory]
    [InlineData("\n", "\r\n")]
    [InlineData("\r\n", "\n")]
    public void ReplaceRejectsConflictingClosingNewlineUnlessDiscarded(string oldNewline, string newNewline)
    {
        string text = "RUN cat <<EOF\nbody\nEOF" + oldNewline;
        var run = Dockerfile.TryParse(text).Dockerfile!.Items.OfType<RunInstruction>().Single();
        var old = Assert.Single(run.Heredocs);
        var roots = run.Tokens.ToArray();
        var replacement = Dockerfile.TryParse("RUN <<NEXT\nnew\nNEXT" + newNewline)
            .Dockerfile!.Items.OfType<RunInstruction>().Single().Heredocs[0];
        Token[] incomingTokens = replacement.Body.Tokens.ToArray();
        string incomingText = replacement.Body.ToString();

        Assert.Throws<InvalidOperationException>(() => run.Heredocs[0] = replacement);

        Assert.Equal(text, run.ToString());
        Assert.Equal(roots, run.Tokens);
        Assert.Same(old, Assert.Single(run.Heredocs));
        Assert.Equal(incomingText, replacement.Body.ToString());
        Assert.Equal(incomingTokens, replacement.Body.Tokens);

        run.Heredocs.Replace(0, replacement, TriviaDisposition.Discard);

        Assert.Same(replacement, Assert.Single(run.Heredocs));
        Assert.Equal("RUN cat <<NEXT\nnew\nNEXT" + newNewline, run.ToString());
        Assert.Equal(incomingTokens, replacement.Body.Tokens);
    }

    /// <summary>
    /// Explicit discard still permits boundary inference when the replacement supplies no closing newline.
    /// </summary>
    /// <param name="headerNewline">The remaining structural boundary used for inference.</param>
    /// <param name="oldNewline">The discarded outgoing closing-line ending.</param>
    [Theory]
    [InlineData("\n", "\r\n")]
    [InlineData("\r\n", "\n")]
    public void ReplaceDiscardInfersAbsentClosingNewline(string headerNewline, string oldNewline)
    {
        var run = Dockerfile.TryParse("RUN cat <<EOF" + headerNewline + "body\nEOF" + oldNewline)
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var replacement = new Heredoc("NEXT", "new\n");

        run.Heredocs.Replace(0, replacement, TriviaDisposition.Discard);

        Assert.Equal("RUN cat <<NEXT" + headerNewline + "new\nNEXT" + headerNewline, run.ToString());
        Assert.Equal("new\n", replacement.RawContent);
    }

    /// <summary>
    /// Closing-line tabs are preserved independently of the replaced payload, or rejected if the new chomp mode forbids them.
    /// </summary>
    /// <param name="chomp">Whether the replacement permits retaining closing-delimiter indentation.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplacePreservesClosingIndentationOnlyWhenCompatible(bool chomp)
    {
        const string text = "RUN <<-EOF\nold\n\tEOF\r\n";
        var run = Dockerfile.TryParse(text).Dockerfile!.Items.OfType<RunInstruction>().Single();
        var old = Assert.Single(run.Heredocs);
        var prefix = old.Body.Tokens.OfType<StringToken>().Last();
        var replacement = new Heredoc("NEXT", "new\n", chomp: chomp);
        Token[] incoming = replacement.Body.Tokens.ToArray();

        if (chomp)
        {
            run.Heredocs[0] = replacement;
            Assert.Equal("RUN <<-NEXT\nnew\n\tNEXT\r\n", run.ToString());
            Assert.Contains(prefix, replacement.Body.Tokens);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => run.Heredocs[0] = replacement);
            Assert.Equal(text, run.ToString());
            Assert.Same(old, Assert.Single(run.Heredocs));
            Assert.Equal(incoming, replacement.Body.Tokens);
        }
        Assert.Equal("new\n", replacement.RawContent);
    }

    /// <summary>
    /// Compatible incoming closing trivia is retained in place rather than duplicated or replaced.
    /// </summary>
    [Fact]
    public void ReplaceRetainsMatchingIncomingClosingTrivia()
    {
        var run = Dockerfile.TryParse("RUN <<-EOF\nold\n\tEOF\r\n")
            .Dockerfile!.Items.OfType<RunInstruction>().Single();
        var replacement = Dockerfile.TryParse("RUN <<-NEXT\nnew\n\tNEXT\r\n")
            .Dockerfile!.Items.OfType<RunInstruction>().Single().Heredocs[0];
        Token[] incoming = replacement.Body.Tokens.ToArray();

        run.Heredocs[0] = replacement;

        Assert.Equal("RUN <<-NEXT\nnew\n\tNEXT\r\n", run.ToString());
        Assert.Same(replacement, Assert.Single(run.Heredocs));
        Assert.Equal(incoming, replacement.Body.Tokens);
    }
}
