using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class HeredocRecoveryModelTests
{
    [Theory]
    [InlineData("RUN <<EO\\\nF\nbody\nEOF\n")]
    [InlineData("RUN <<EOF \\\n && echo ready\nbody\nEOF\n")]
    [InlineData("RUN cat << EOF\nbody\nEOF\n")]
    [InlineData("RUN << 'EOF'\nbody\nEOF\n")]
    [InlineData("RUN <<EOF \\\n # header comment\n && echo ready\nbody\nEOF\n")]
    public void LogicalHeaderProducesCorrectTypedHeredoc(string instruction)
    {
        string text = instruction + "FROM scratch\n";
        DockerfileParseResult result = Dockerfile.TryParse(text);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(text, result.Dockerfile!.ToString());
        RunInstruction run = Assert.IsType<RunInstruction>(result.Dockerfile.Items[0]);
        Heredoc heredoc = Assert.Single(run.Heredocs);
        Assert.Equal("EOF", heredoc.Name);
        Assert.Equal("body\n", heredoc.Content);
        Assert.Equal("EOF", Assert.Single(run.HeredocBodyTokens).ClosingDelimiter);
        Assert.IsType<FromInstruction>(result.Dockerfile.Items[1]);
    }

    [Fact]
    public void MultipleHeredocsOnContinuedHeaderHaveSeparateBodies()
    {
        const string text = "RUN <<ONE \\\n <<TWO\nfirst\nONE\nsecond\nTWO\nFROM scratch\n";
        DockerfileParseResult result = Dockerfile.TryParse(text);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        RunInstruction run = Assert.IsType<RunInstruction>(result.Dockerfile!.Items[0]);
        Assert.Collection(run.Heredocs,
            first =>
            {
                Assert.Equal("ONE", first.Name);
                Assert.Equal("first\n", first.Content);
            },
            second =>
            {
                Assert.Equal("TWO", second.Name);
                Assert.Equal("second\n", second.Content);
            });
        Assert.Equal(text, result.Dockerfile.ToString());
    }

    [Theory]
    [InlineData("COPY <<EOF \\\n /destination\nbody\nEOF\n")]
    [InlineData("ADD << EOF \\\n /destination\nbody\nEOF\n")]
    public void ContinuedDestinationRemainsAFileTransferDestination(string text)
    {
        DockerfileParseResult result = Dockerfile.TryParse(text);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        FileTransferInstruction instruction = Assert.IsAssignableFrom<FileTransferInstruction>(Assert.Single(result.Dockerfile!.Items));
        Assert.Equal("/destination", instruction.Destination);
        Assert.Equal("body\n", Assert.Single(instruction.Heredocs).Content);
        Assert.Equal(text, result.Dockerfile.ToString());
    }

    [Theory]
    [InlineData("RUN <<<word\n")]
    [InlineData("RUN echo foo<<EOF\n")]
    [InlineData("RUN echo \"<<EOF\"\n")]
    [InlineData("RUN [\"echo\", \"<<EOF\"]\n")]
    public void FalseMarkersDoNotConsumeLaterInstructions(string instruction)
    {
        string text = instruction + "FROM scratch\n";
        DockerfileParseResult result = Dockerfile.TryParse(text);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(text, result.Dockerfile!.ToString());
        Assert.IsType<FromInstruction>(result.Dockerfile.Items[1]);
        Assert.Empty(Assert.IsType<RunInstruction>(result.Dockerfile.Items[0]).Heredocs);
    }

    [Fact]
    public void OnBuildSharesLogicalHeredocBoundaries()
    {
        const string text = "ONBUILD RUN <<EO\\\nF\nbody\nEOF\nFROM scratch\n";
        DockerfileParseResult result = Dockerfile.TryParse(text);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        OnBuildInstruction onBuild = Assert.IsType<OnBuildInstruction>(result.Dockerfile!.Items[0]);
        RunInstruction run = Assert.IsType<RunInstruction>(onBuild.Instruction);
        Assert.Equal("EOF", Assert.Single(run.Heredocs).Name);
        Assert.Equal("body\n", Assert.Single(run.Heredocs).Content);
        Assert.Equal(text, result.Dockerfile.ToString());
    }

    [Fact]
    public void CustomEscapeAppliesToContinuedMarkerAndSourceModel()
    {
        const string text = "# ESCAPE=`\nRUN <<EO`\nF\nbody\nEOF\nFROM scratch\n";
        DockerfileParseResult result = Dockerfile.TryParse(text);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal('`', result.Dockerfile!.EscapeChar);
        RunInstruction run = Assert.IsType<RunInstruction>(result.Dockerfile.Items[1]);
        Assert.Equal("EOF", Assert.Single(run.Heredocs).Name);
        Assert.Equal("body\n", Assert.Single(run.Heredocs).Content);
        Assert.Equal(text, result.Dockerfile.ToString());
    }

    [Theory]
    [InlineData("'space name'", "space name")]
    [InlineData("\0EOF", "\0EOF")]
    public void DelimiterTextDoesNotUseProgrammaticIdentifierGuards(string marker, string name)
    {
        string text = $"RUN <<{marker}\nbody\n{name}\nFROM scratch\n";
        DockerfileParseResult result = Dockerfile.TryParse(text);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        RunInstruction run = Assert.IsType<RunInstruction>(result.Dockerfile!.Items[0]);
        Assert.Equal(name, Assert.Single(run.Heredocs).Name);
        Assert.Equal("body\n", Assert.Single(run.Heredocs).Content);
        Assert.IsType<FromInstruction>(result.Dockerfile.Items[1]);
        Assert.Equal(text, result.Dockerfile.ToString());
    }

    [Theory]
    [InlineData(DockerfileParseMode.Strict)]
    [InlineData(DockerfileParseMode.Recover)]
    public void HashPrefixedDelimiterKeepsBodyOutOfStages(DockerfileParseMode mode)
    {
        const string text = "RUN <<#EOF\nFROM scratch\n#EOF\n";
        DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions { Mode = mode });

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
        RunInstruction run = Assert.IsType<RunInstruction>(Assert.Single(result.Dockerfile!.Items));
        Assert.Equal("#EOF", Assert.Single(run.Heredocs).Name);
        Assert.Equal("FROM scratch\n", Assert.Single(run.Heredocs).Content);
        Assert.Empty(new StagesView(result.Dockerfile).Stages);
        SourceSpanTests.AssertPartition(text, result.Dockerfile);
    }

    [Theory]
    [InlineData("\"E\\OF\"", "E\\OF", '\\', false)]
    [InlineData("'E\\OF'", "E\\OF", '\\', false)]
    [InlineData("\"E\\$OF\"", "E$OF", '\\', false)]
    [InlineData("\"E\\\\OF\"", "E\\OF", '\\', false)]
    [InlineData("E\\OF", "EOF", '\\', true)]
    [InlineData("E`OF", "E`OF", '`', true)]
    [InlineData("\"E`OF\"", "E`OF", '`', false)]
    [InlineData("\"E\\OF\"", "E\\OF", '`', false)]
    [InlineData("\"E\\$OF\"", "E$OF", '`', false)]
    [InlineData("\"E\\`OF\"", "E\\`OF", '\\', false)]
    [InlineData("\"E\\\"OF\"", "E\"OF", '\\', false)]
    [InlineData("'E'\"OF\"", "EOF", '\\', false)]
    [InlineData("E\"OF\"", "EOF", '\\', false)]
    [InlineData("EOF\\", "EOF", '`', true)]
    [InlineData("'EOF'\\", "EOF", '`', false)]
    [InlineData("EOF\\\\", "EOF\\", '`', true)]
    [InlineData("EOF\\\\\\", "EOF\\", '`', true)]
    [InlineData("EOF\\ ", "EOF ", '`', true)]
    public void ShellDelimiterEscapesAreIndependentOfDockerfileEscape(
        string marker, string name, char escapeChar, bool expand)
    {
        string directive = $"# escape={escapeChar}\n";
        string text = $"{directive}RUN <<{marker}\n{name}\nFROM scratch\n";
        foreach (DockerfileParseMode mode in Enum.GetValues<DockerfileParseMode>())
        {
            DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions { Mode = mode });

            Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Assert.Empty(result.Diagnostics);
            RunInstruction run = Assert.IsType<RunInstruction>(result.Dockerfile!.Items[1]);
            Heredoc heredoc = Assert.Single(run.Heredocs);
            Assert.Equal(name, heredoc.Name);
            Assert.Equal("", heredoc.Content);
            Assert.Equal(expand, heredoc.Expand);
            Assert.Equal(name, Assert.Single(run.HeredocBodyTokens).ClosingDelimiter);
            Assert.IsType<FromInstruction>(result.Dockerfile.Items[2]);
            Assert.Single(new StagesView(result.Dockerfile).Stages);
            SourceSpanTests.AssertPartition(text, result.Dockerfile);
        }
    }

    [Theory]
    [InlineData("<\\\n<EOF", '\\', false)]
    [InlineData("<\\\n<-EOF", '\\', true)]
    [InlineData("<<\\\n-EOF", '\\', true)]
    [InlineData("<\\\r\n<\\\r\n-EOF", '\\', true)]
    [InlineData("<`\n<EOF", '`', false)]
    [InlineData("<<`\n-EOF", '`', true)]
    [InlineData("<\\\n# operator comment\n<\\\n-EOF", '\\', true)]
    public void SplitOperatorsKeepPhysicalTokensAndLogicalChomp(string marker, char escapeChar, bool chomp)
    {
        string body = chomp ? "\tbody\n" : "body\n";
        string closing = chomp ? "\tEOF\n" : "EOF\n";
        string text = $"# escape={escapeChar}\nRUN {marker}\n{body}{closing}FROM scratch\n";
        foreach (DockerfileParseMode mode in Enum.GetValues<DockerfileParseMode>())
        {
            DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions { Mode = mode });

            Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            RunInstruction run = Assert.IsType<RunInstruction>(result.Dockerfile!.Items[1]);
            Heredoc heredoc = Assert.Single(run.Heredocs);
            Assert.Equal("EOF", heredoc.Name);
            Assert.Equal(chomp, heredoc.Chomp);
            Assert.Equal("body\n", heredoc.Content);
            Assert.Equal(body, Assert.Single(run.HeredocBodyTokens).Content);
            Assert.Equal(marker, Assert.Single(run.HeredocMarkerTokens).ToString());
            Assert.IsType<FromInstruction>(result.Dockerfile.Items[2]);
            SourceSpanTests.AssertPartition(text, result.Dockerfile);
        }
    }

    [Theory]
    [InlineData("COPY", " /target")]
    [InlineData("ADD", " /target")]
    [InlineData("ONBUILD RUN", "")]
    public void SharedInstructionPathsUseSourceAwareHeredocMarkers(string instruction, string arguments)
    {
        string text = $"{instruction} <\\\n<\\\n-\"E\\OF\"{arguments}\n\tbody\n\tE\\OF\nFROM scratch\n";
        DockerfileParseResult result = Dockerfile.TryParse(text);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Instruction parsed = Assert.IsAssignableFrom<Instruction>(result.Dockerfile!.Items[0]);
        Heredoc heredoc;
        if (parsed is FileTransferInstruction transfer)
        {
            Assert.Equal("/target", transfer.Destination);
            heredoc = Assert.Single(transfer.Heredocs);
        }
        else
        {
            OnBuildInstruction onBuild = Assert.IsType<OnBuildInstruction>(parsed);
            heredoc = Assert.Single(Assert.IsType<RunInstruction>(onBuild.Instruction).Heredocs);
        }
        Assert.Equal("E\\OF", heredoc.Name);
        Assert.Equal("body\n", heredoc.Content);
        Assert.True(heredoc.Chomp);
        Assert.False(heredoc.Expand);
        Assert.IsType<FromInstruction>(result.Dockerfile.Items[1]);
        SourceSpanTests.AssertPartition(text, result.Dockerfile);
    }

    [Fact]
    public void DecodedDelimiterNameReflectsTokenEdits()
    {
        DockerfileParseResult result = Dockerfile.TryParse("RUN <<\"E\\$OF\"\nE$OF\n");
        Assert.True(result.Success);
        RunInstruction run = Assert.IsType<RunInstruction>(Assert.Single(result.Dockerfile!.Items));
        HeredocMarkerToken marker = Assert.Single(run.HeredocMarkerTokens);
        HeredocDelimiterToken delimiter = Assert.Single(marker.Tokens.OfType<HeredocDelimiterToken>());

        delimiter.Value = "OTHER\\$END";

        Assert.Equal("OTHER$END", marker.DelimiterName);
        Assert.Equal("OTHER$END", Assert.Single(run.Heredocs).Name);
        Assert.Equal("<<\"OTHER\\$END\"", marker.ToString());
    }

    [Theory]
    [InlineData("\\")]
    [InlineData("''")]
    [InlineData("\"\"")]
    [InlineData("''\\")]
    [InlineData("\"\"\\")]
    [InlineData("''\"\"\\")]
    [InlineData("\"\"''\\")]
    public void EmptyDecodedDelimiterDoesNotCreateAHeredoc(string marker)
    {
        string text = $"# escape=`\nRUN <<{marker}\nFROM scratch\n";
        foreach (DockerfileParseMode mode in Enum.GetValues<DockerfileParseMode>())
        {
            DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions { Mode = mode });

            Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Assert.Empty(result.Diagnostics);
            RunInstruction run = Assert.IsType<RunInstruction>(result.Dockerfile!.Items[1]);
            Assert.Empty(run.Heredocs);
            Assert.IsType<FromInstruction>(result.Dockerfile.Items[2]);
            Assert.Single(new StagesView(result.Dockerfile).Stages);
            SourceSpanTests.AssertPartition(text, result.Dockerfile);
        }
    }
}
