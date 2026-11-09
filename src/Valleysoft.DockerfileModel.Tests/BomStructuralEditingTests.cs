using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.InstructionCollectionEditing;

namespace Valleysoft.DockerfileModel.Tests;

public class BomStructuralEditingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SourcesPreservePrefixTriviaAndTokenIdentity(bool diagnostic, bool bom)
    {
        string prefix = bom ? "\uFEFF" : "";
        Dockerfile document = Parse($"{prefix}COPY src \\\r\n# retained\r\n/dest\r\nRUN unchanged\r\n", diagnostic);
        CopyInstruction copy = Assert.IsType<CopyInstruction>(document.Items[0]);
        Token first = copy.Tokens.First();
        LiteralToken source = copy.SourceTokens[0];
        LiteralToken destination = copy.DestinationToken!;
        CommentToken comment = Assert.Single(copy.CommentTokens);
        DockerfileConstruct following = document.Items[1];
        Token[] trivia = Descendants(copy).Where(token =>
            token is WhitespaceToken or LineContinuationToken or CommentToken).ToArray();

        copy.Sources.Add("extra");
        LiteralToken incoming = new("typed");
        copy.SourceTokens.Insert(1, incoming);
        Assert.Same(incoming, copy.SourceTokens[1]);
        copy.SourceTokens.Remove(incoming);
        copy.Sources[0] = "renamed";
        copy.Sources.Move(1, 0);
        copy.Sources.RemoveAt(0);
        copy.Excludes.Add("*.tmp");
        copy.Excludes.Clear();

        Assert.Equal(new[] { "renamed" }, copy.Sources);
        Assert.Same(first, copy.Tokens.First());
        Assert.Same(source, copy.SourceTokens[0]);
        Assert.Same(destination, copy.DestinationToken);
        Assert.Same(comment, Assert.Single(copy.CommentTokens));
        Assert.Same(following, document.Items[1]);
        foreach (Token token in trivia)
        {
            Assert.Contains(Descendants(copy), candidate => ReferenceEquals(token, candidate));
        }
        Assert.Equal($"{prefix}COPY renamed \\\r\n# retained\r\n/dest\r\nRUN unchanged\r\n", document.ToString());
        Assert.Equal(document.ToString(), Parse(document.ToString(), diagnostic).ToString());
    }

    [Theory]
    [InlineData(false, "COPY src /dest\n")]
    [InlineData(true, "COPY src /dest\n")]
    [InlineData(false, "ADD src /dest\n")]
    [InlineData(true, "ADD src /dest\n")]
    [InlineData(false, "COPY [\"src\", \"/dest\"]\n")]
    [InlineData(true, "COPY [\"src\", \"/dest\"]\n")]
    [InlineData(false, "ADD [\"src\", \"/dest\"]\n")]
    [InlineData(true, "ADD [\"src\", \"/dest\"]\n")]
    [InlineData(false, "ENV one=1\n")]
    [InlineData(true, "ENV one=1\n")]
    [InlineData(false, "ENV one value\n")]
    [InlineData(true, "ENV one value\n")]
    [InlineData(false, "LABEL one=1\n")]
    [InlineData(true, "LABEL one=1\n")]
    [InlineData(false, "ARG one=1\n")]
    [InlineData(true, "ARG one=1\n")]
    [InlineData(false, "EXPOSE 80\n")]
    [InlineData(true, "EXPOSE 80\n")]
    [InlineData(false, "VOLUME /one\n")]
    [InlineData(true, "VOLUME /one\n")]
    [InlineData(false, "VOLUME [\"/one\"]\n")]
    [InlineData(true, "VOLUME [\"/one\"]\n")]
    [InlineData(false, "RUN echo ok\n")]
    [InlineData(true, "RUN echo ok\n")]
    [InlineData(false, "RUN [\"echo\", \"ok\"]\n")]
    [InlineData(true, "RUN [\"echo\", \"ok\"]\n")]
    [InlineData(false, "ONBUILD COPY src /dest\n")]
    [InlineData(true, "ONBUILD COPY src /dest\n")]
    public void FirstInstructionCollectionsRemainEditable(bool diagnostic, string text)
    {
        Dockerfile document = Parse("\uFEFF" + text, diagnostic);
        Instruction instruction = Assert.IsAssignableFrom<Instruction>(document.Items[0]);
        Token bom = instruction.Tokens.First();
        switch (instruction)
        {
            case FileTransferInstruction transfer:
                transfer.Sources.Add("extra");
                Assert.Equal(new[] { "src", "extra" }, transfer.Sources);
                break;
            case EnvInstruction env:
                env.Variables.Add(new PairValue { Key = "two", Value = "2" });
                Assert.Equal(2, env.Variables.Count);
                break;
            case LabelInstruction label:
                label.Labels.Add(new PairValue { Key = "two", Value = "2" });
                Assert.Equal(2, label.Labels.Count);
                break;
            case ArgInstruction arg:
                arg.Args.Add(new PairValue { Key = "two", Value = "2" });
                Assert.Equal(2, arg.Args.Count);
                break;
            case ExposeInstruction expose:
                expose.Ports.Add("443");
                Assert.Equal(new[] { "80", "443" }, expose.Ports);
                break;
            case VolumeInstruction volume:
                volume.Paths.Add("/two");
                Assert.Equal(new[] { "/one", "/two" }, volume.Paths);
                break;
            case RunInstruction run:
                run.Mounts.Add(Mount.Parse("type=cache,target=/cache"));
                run.Mounts[0].Entries.Add(new MountEntry("sharing", "locked"));
                if (run.Command is ExecFormCommand exec)
                {
                    exec.Values.Add("extra");
                    Assert.Equal(new[] { "echo", "ok", "extra" }, exec.Values);
                }
                break;
            case OnBuildInstruction onBuild:
                CopyInstruction copy = Assert.IsType<CopyInstruction>(onBuild.Instruction);
                copy.Sources.Add("extra");
                Assert.Equal(new[] { "src", "extra" }, copy.Sources);
                break;
            default:
                throw new InvalidOperationException();
        }
        instruction.Comments.Add("new");
        instruction.Comments[0] = "updated";
        instruction.Comments.Clear(TriviaDisposition.Discard);

        Assert.Same(bom, instruction.Tokens.First());
        Assert.Equal("\uFEFF", bom.ToString());
        Assert.Equal(1, document.ToString().Count(character => character == '\uFEFF'));
        Assert.Equal(document.ToString(), Parse(document.ToString(), diagnostic).ToString());
    }

    [Theory]
    [InlineData(false, "COPY src /dest\n")]
    [InlineData(true, "COPY src /dest\n")]
    [InlineData(false, "RUN echo ok\n")]
    [InlineData(true, "RUN echo ok\n")]
    [InlineData(true, "COPY src <<EOF /dest\nbody\nEOF\n")]
    [InlineData(true, "RUN cat <<EOF\nbody\nEOF\n")]
    public void HeredocCollectionsPreserveBomAndSurvivingPairs(bool diagnostic, string text)
    {
        Dockerfile document = Parse("\uFEFF" + text, diagnostic);
        Instruction instruction = Assert.IsAssignableFrom<Instruction>(document.Items[0]);
        Token bom = instruction.Tokens.First();
        EditableList<Heredoc> pairs = instruction is RunInstruction run
            ? run.Heredocs : ((FileTransferInstruction)instruction).Heredocs;
        Heredoc[] original = pairs.ToArray();
        Heredoc incoming = new("EXTRA", "new body\n");

        instruction.Comments.Add("keep");
        pairs.Add(incoming);
        pairs.Move(pairs.Count - 1, 0);
        pairs.RemoveAt(0);

        Assert.Same(bom, instruction.Tokens.First());
        Assert.Equal(original, pairs);
        foreach (Heredoc pair in original)
        {
            Assert.Contains(instruction.Tokens, token => ReferenceEquals(token, pair.Marker));
            Assert.Contains(instruction.Tokens, token => ReferenceEquals(token, pair.Body));
        }
        Assert.StartsWith("\uFEFF", document.ToString());
        Assert.Equal(document.ToString(), Parse(document.ToString(), diagnostic).ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSourceEditsRemainAtomic(bool diagnostic)
    {
        Dockerfile document = Parse("\uFEFFCOPY src /dest\n", diagnostic);
        CopyInstruction copy = Assert.IsType<CopyInstruction>(document.Items[0]);
        Token[] tokens = Descendants(copy).ToArray();
        string before = document.ToString();
        LiteralToken incoming = new("<<EOF");
        Token[] incomingTokens = Descendants(incoming).ToArray();

        Assert.Throws<InvalidOperationException>(() => copy.SourceTokens.Add(incoming));
        Assert.Throws<InvalidOperationException>(() => copy.SourceTokens.Add(copy.SourceTokens[0]));
        Assert.Throws<InvalidOperationException>(() => copy.Sources.Clear(TriviaDisposition.Discard));

        Assert.Equal(before, document.ToString());
        Assert.Equal(tokens.Length, Descendants(copy).Count());
        Assert.All(tokens.Zip(Descendants(copy)), pair => Assert.Same(pair.First, pair.Second));
        Assert.All(incomingTokens.Zip(Descendants(incoming)), pair => Assert.Same(pair.First, pair.Second));
        Assert.Equal(incomingTokens.Length, Descendants(incoming).Count());
        Assert.Equal("<<EOF", incoming.ToString());
        Assert.Null(incoming.QuoteChar);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericReplacementRetainsBomAcrossLocalEdits(bool diagnostic)
    {
        Dockerfile document = Parse("\uFEFFRUN echo ok\n", diagnostic);
        Token bom = document.Items[0].Tokens.First();
        GenericInstruction generic = GenericInstruction.Parse("RUN echo ok\n");
        document.Items[0] = generic;

        generic.ArgLines.Add("echo extra");
        generic.Comments.Add("retained");
        CommentToken comment = Assert.Single(generic.CommentTokens);
        generic.ArgLines[1] = "echo updated";
        generic.Comments[0] = "updated";

        Assert.Same(generic, document.Items[0]);
        Assert.Same(bom, generic.Tokens.First());
        Assert.Same(comment, Assert.Single(generic.CommentTokens));
        Assert.Equal(new[] { "echo ok", "echo updated" }, generic.ArgLines);
        Assert.Equal(document.ToString(), Parse(document.ToString(), diagnostic).ToString());
        string before = document.ToString();
        Token[] tokens = Descendants(generic).ToArray();

        Assert.Throws<InvalidOperationException>(() => generic.ArgLines.Add("cat <<EOF"));

        Assert.Equal(before, document.ToString());
        Assert.Equal(tokens.Length, Descendants(generic).Count());
        Assert.All(tokens.Zip(Descendants(generic)), pair => Assert.Same(pair.First, pair.Second));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BomOnEscapeDirectiveRemainsDocumentTrivia(bool diagnostic)
    {
        Dockerfile document = Parse("\uFEFF# escape=`\nCOPY src /dest\n", diagnostic);
        Token bom = document.Items[0].Tokens.First();
        CopyInstruction copy = Assert.IsType<CopyInstruction>(document.Items[1]);
        Heredoc incoming = new("EOF", "body\n", escapeChar: '`');

        copy.Heredocs.Add(incoming);
        copy.Sources.Add("extra");
        copy.Comments.Add("retained");
        copy.Heredocs.RemoveAt(0);

        Assert.Same(bom, document.Items[0].Tokens.First());
        Assert.Equal(new[] { "src", "extra" }, copy.Sources);
        Assert.Equal('\uFEFF', document.ToString()[0]);
        Assert.Equal(document.ToString(), Parse(document.ToString(), diagnostic).ToString());
        string before = document.ToString();

        Assert.Throws<InvalidOperationException>(() => document.Items.RemoveAt(0));

        Assert.Equal(before, document.ToString());
        Assert.Same(bom, document.Items[0].Tokens.First());
    }

    [Fact]
    public void RecoveryDocumentKeepsBomAndUnrelatedMalformedText()
    {
        DockerfileParseResult result = Dockerfile.TryParse(
            "\uFEFFCOPY src /dest\nFROM\n",
            new DockerfileParseOptions { Mode = DockerfileParseMode.Recover });
        Assert.False(result.Success);
        Dockerfile document = result.Dockerfile!;
        CopyInstruction copy = Assert.IsType<CopyInstruction>(document.Items[0]);
        DockerfileConstruct malformed = document.Items[1];
        Token bom = copy.Tokens.First();

        copy.Sources.Add("extra");
        copy.Comments.Add("retained");

        Assert.Same(bom, copy.Tokens.First());
        Assert.Same(malformed, document.Items[1]);
        Assert.Equal("FROM\n", malformed.ToString());
        Assert.Equal(new[] { "src", "extra" }, copy.Sources);
        Assert.StartsWith("\uFEFF", document.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyTheDocumentPrefixBomIsExcludedFromGrammar(bool diagnostic)
    {
        Dockerfile document = Parse("\uFEFF \tCOPY src /dest\n", diagnostic);
        CopyInstruction copy = Assert.IsType<CopyInstruction>(document.Items[0]);
        Token bom = copy.Tokens.First();

        copy.Sources.Add("extra\uFEFF");
        copy.Comments.Add("retained");

        Assert.Same(bom, copy.Tokens.First());
        Assert.Equal(new[] { "src", "extra\uFEFF" }, copy.Sources);
        Assert.Equal(2, document.ToString().Count(character => character == '\uFEFF'));
        Assert.StartsWith("\uFEFF \tCOPY", document.ToString());
        Assert.Equal(document.ToString(), Parse(document.ToString(), diagnostic).ToString());
    }

    private static Dockerfile Parse(string text, bool diagnostic)
    {
        if (!diagnostic)
        {
            return Dockerfile.Parse(text);
        }
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.True(result.Success);
        return result.Dockerfile!;
    }

    [Theory]
    [InlineData("RUNecho ok\n")]
    [InlineData("RUN#argument\n")]
    [InlineData("RUN.echo ok\n")]
    public void MalformedInstructionNamesRejectHeredocWrites(string text)
    {
        RunInstruction run = RunInstruction.Parse(text);
        Token[] tokens = Descendants(run).ToArray();
        Heredoc incoming = new("EOF", "body\n");
        Token[] incomingTokens = Descendants(incoming.Marker).Concat(Descendants(incoming.Body)).ToArray();
        Assert.False(Dockerfile.TryParse(text).Success);

        run.Heredocs.Clear();
        Assert.Throws<InvalidOperationException>(() => run.Heredocs.Add(incoming));

        Assert.Equal(text, run.ToString());
        Assert.Empty(run.Heredocs);
        Assert.Equal(tokens.Length, Descendants(run).Count());
        Assert.All(tokens.Zip(Descendants(run)), pair => Assert.Same(pair.First, pair.Second));
        Assert.All(incomingTokens.Zip(Descendants(incoming.Marker).Concat(Descendants(incoming.Body))),
            pair => Assert.Same(pair.First, pair.Second));
        Assert.Equal("body\n", incoming.RawContent);
    }

    [Theory]
    [InlineData("RUNecho ok\n", false)]
    [InlineData("RUNecho ok\n", true)]
    [InlineData("\uFEFFRUNecho ok\n", false)]
    [InlineData("\uFEFFRUNecho ok\n", true)]
    [InlineData("RUN#argument\n", false)]
    [InlineData("RUN#argument\n", true)]
    public void InstructionValidationRejectsUnknownNamesAndMissingBoundaries(string text, bool generic)
    {
        Assert.Throws<InvalidOperationException>(
            () => EditValidation.ParseInstruction(text, '\\', generic));
    }

    private sealed class PairValue : IKeyValuePair
    {
        public string Key { get; set; } = "";
        public string? Value { get; set; }
    }
}
