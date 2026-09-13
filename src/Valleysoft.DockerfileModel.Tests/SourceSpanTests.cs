namespace Valleysoft.DockerfileModel.Tests;

public class SourceSpanTests
{
    [Fact]
    public void PositionsAndSpansHaveValueEquality()
    {
        SourcePosition start = new(3, 2, 1);
        SourcePosition end = new(8, 2, 6);
        SourcePosition equalStart = new(3, 2, 1);
        SourceSpan span = new(start, end);
        SourceSpan equalSpan = new(equalStart, new SourcePosition(8, 2, 6));
        Assert.Equal(5, span.Length);
        Assert.Equal(start, span.Start);
        Assert.Equal(end, span.End);
        Assert.True(start == equalStart);
        Assert.False(start != equalStart);
        Assert.True(span == equalSpan);
        Assert.False(span != equalSpan);
        Assert.Equal(start.GetHashCode(), equalStart.GetHashCode());
        Assert.Equal(span.GetHashCode(), equalSpan.GetHashCode());
        Assert.False(start.Equals((object?)null));
        Assert.False(span.Equals((object?)null));
        Assert.NotEqual(start, end);
        Assert.NotEqual(span, new SourceSpan(start, start));
        Assert.Equal(0, new SourceSpan(end, end).Length);
    }

    [Theory]
    [InlineData(-1, 1, 1)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 1, 0)]
    public void PositionRejectsInvalidCoordinates(int offset, int line, int column)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourcePosition(offset, line, column));
    }

    [Fact]
    public void SpanRejectsReversedRange()
    {
        Assert.Throws<ArgumentException>(() => new SourceSpan(new SourcePosition(4, 1, 5), new SourcePosition(2, 1, 3)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n# café 🚀\nFROM scratch\r\nRUN echo end")]
    [InlineData("# escape=`\nFROM `\r\n scratch\n\nRUN echo hi\n")]
    [InlineData("RUN <<EOF\r\nFROM body\r\nEOF\r\nFROM scratch")]
    public void BothDockerfileParsersAssignPartitioningOriginalSpans(string text)
    {
        AssertPartition(text, Dockerfile.Parse(text));
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        AssertPartition(text, Assert.IsType<Dockerfile>(result.Dockerfile));
    }

    [Theory]
    [InlineData("# 🚀\t\rx\nFUTURE café🚀", 2, 14)]
    [InlineData("# first\r\nFUTURE 🚀\r", 2, 11)]
    [InlineData("# first\nFUTURE 🚀\r\n", 3, 1)]
    public void Utf16TabsNewlinesAndLoneCarriageReturnHaveDefinedCoordinates(string text, int endLine, int endColumn)
    {
        DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions
        {
            UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
        });
        Assert.True(result.Success);
        AssertPartition(text, result.Dockerfile!);
        SourceSpan last = Assert.IsType<SourceSpan>(result.Dockerfile!.Items.Last().SourceSpan);
        Assert.Equal(new SourcePosition(text.Length, endLine, endColumn), last.End);
        SourceSpan keyword = Assert.Single(result.Diagnostics).SourceSpan;
        Assert.Equal(new SourcePosition(text.IndexOf("FUTURE", StringComparison.Ordinal), 2, 1), keyword.Start);
    }

    [Fact]
    public void DiagnosticAfterMultilineHeredocUsesGlobalCoordinates()
    {
        const string prefix = "RUN <<EOF\r\n🚀 body\r\nEOF\r\n\t";
        const string text = prefix + "FUTURE value";
        DockerfileParseResult result = Dockerfile.TryParse(text);
        DockerfileDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("DFP002", diagnostic.Code);
        Assert.Equal(new SourcePosition(prefix.Length, 4, 2), diagnostic.SourceSpan.Start);
        Assert.Equal(new SourcePosition(prefix.Length + 6, 4, 8), diagnostic.SourceSpan.End);
    }

    [Fact]
    public void EofSyntaxFailureCanHaveZeroLengthSpan()
    {
        const string text = "FROM scratch\nFROM ";
        DockerfileParseResult result = Dockerfile.TryParse(text);
        DockerfileDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("DFP001", diagnostic.Code);
        Assert.Equal(new SourcePosition(text.Length, 2, 6), diagnostic.SourceSpan.Start);
        Assert.Equal(diagnostic.SourceSpan.Start, diagnostic.SourceSpan.End);
        Assert.Equal(0, diagnostic.SourceSpan.Length);
    }

    [Fact]
    public void RecoverySpansCoverEveryCharacterAndDiagnosticsAreRepeatable()
    {
        const string text = "# escape=\r\nFROM scratch\nFUTURE 🚀 \\\n # comment\n value\nARG\nRUN echo end";
        DockerfileParseOptions options = new()
        {
            Mode = DockerfileParseMode.Recover,
            UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
        };
        DockerfileParseResult first = Dockerfile.TryParse(text, options);
        DockerfileParseResult second = Dockerfile.TryParse(text, options);
        Assert.False(first.Success);
        AssertPartition(text, first.Dockerfile!);
        Assert.Equal(first.Diagnostics.Select(d => (d.Code, d.Severity, d.SourceSpan)),
            second.Diagnostics.Select(d => (d.Code, d.Severity, d.SourceSpan)));
        Assert.Equal(first.Diagnostics.OrderBy(d => d.SourceSpan.Start.Offset), first.Diagnostics);
        foreach (DockerfileDiagnostic diagnostic in first.Diagnostics)
        {
            Assert.InRange(diagnostic.SourceSpan.Start.Offset, 0, text.Length);
            Assert.InRange(diagnostic.SourceSpan.End.Offset, diagnostic.SourceSpan.Start.Offset, text.Length);
            Assert.Equal(PositionAt(text, diagnostic.SourceSpan.Start.Offset), diagnostic.SourceSpan.Start);
            Assert.Equal(PositionAt(text, diagnostic.SourceSpan.End.Offset), diagnostic.SourceSpan.End);
        }
    }

    [Fact]
    public void StandaloneConstructsAndNestedInstructionsHaveNoSpan()
    {
        Assert.Null(new FromInstruction("scratch").SourceSpan);
        Assert.Null(FromInstruction.Parse("FROM scratch").SourceSpan);
        Assert.Null(Comment.Parse("# comment").SourceSpan);
        Dockerfile model = Dockerfile.TryParse("ONBUILD RUN echo hi").Dockerfile!;
        OnBuildInstruction onBuild = Assert.IsType<OnBuildInstruction>(Assert.Single(model.Items));
        Assert.NotNull(onBuild.SourceSpan);
        Assert.Null(onBuild.Instruction.SourceSpan);
        Assert.Empty(new Dockerfile().Items);
    }

    [Fact]
    public void MutationAndReorderingKeepSpansAndDiagnosticsAsOriginalSnapshots()
    {
        const string text = "FROM scratch\nFUTURE arg\nRUN echo end\n";
        DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions
        {
            UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
        });
        Dockerfile model = result.Dockerfile!;
        DockerfileConstruct[] items = model.Items.ToArray();
        SourceSpan?[] spans = items.Select(item => item.SourceSpan).ToArray();
        DockerfileDiagnostic[] diagnostics = result.Diagnostics.ToArray();
        SourceSpan diagnosticSpan = diagnostics[0].SourceSpan;
        Assert.IsType<FromInstruction>(items[0]).ImageName = "longer/image:tag";
        model.Items.Remove(items[0]);
        model.Items.Add(items[0]);
        Assert.Equal(spans, items.Select(item => item.SourceSpan));
        Assert.Equal(diagnostics, result.Diagnostics);
        Assert.Equal(diagnosticSpan, result.Diagnostics[0].SourceSpan);
        Assert.Equal("FROM scratch\n", text.Substring(spans[0]!.Value.Start.Offset, spans[0]!.Value.Length));

        if (result.Diagnostics is IList<DockerfileDiagnostic> mutableInterface)
        {
            Assert.True(mutableInterface.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => mutableInterface.Clear());
        }

        Dockerfile reparsed = Dockerfile.TryParse(model.ToString(), new DockerfileParseOptions
        {
            UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
        }).Dockerfile!;
        AssertPartition(model.ToString(), reparsed);
        Assert.NotEqual(spans[0], reparsed.Items.Last().SourceSpan);
    }

    internal static void AssertPartition(string text, Dockerfile model)
    {
        Assert.Equal(text, model.ToString());
        int offset = 0;
        foreach (DockerfileConstruct construct in model.Items)
        {
            SourceSpan span = Assert.IsType<SourceSpan>(construct.SourceSpan);
            Assert.Equal(offset, span.Start.Offset);
            Assert.InRange(span.End.Offset, offset, text.Length);
            Assert.Equal(construct.ToString(), text.Substring(offset, span.Length));
            Assert.Equal(PositionAt(text, offset), span.Start);
            Assert.Equal(PositionAt(text, span.End.Offset), span.End);
            offset = span.End.Offset;
        }
        Assert.Equal(text.Length, offset);
    }

    private static SourcePosition PositionAt(string text, int offset)
    {
        int line = 1;
        int column = 1;
        foreach (char character in text.Take(offset))
        {
            if (character == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        return new SourcePosition(offset, line, column);
    }
}
