using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class DirectiveHeaderTests
{
    [Theory]
    [InlineData("# unknown=value\n")]
    [InlineData("# ordinary comment\n")]
    [InlineData("\n")]
    [InlineData(" \t\r\n")]
    [InlineData("FROM scratch\n")]
    [InlineData("# syntax=\n")]
    [InlineData("# escape=\n")]
    public void NonDirectiveEndsHeader(string prefix)
    {
        string text = prefix + "# escape=`\n# syntax=docker/dockerfile:1\n";
        foreach (Dockerfile file in ParseBoth(text))
        {
            Assert.Equal(text, file.ToString());
            Assert.Empty(file.Items.OfType<ParserDirective>());
            Assert.Equal('\\', file.EscapeChar);
        }
    }

    [Theory]
    [InlineData("#syntax=docker/dockerfile:1")]
    [InlineData(" \t# SyNtAx \t= docker/dockerfile:1.20.0-labs  \r\n")]
    [InlineData("#check=skip=Foo, Bar; error=true")]
    public void DirectiveAtEofPreservesEntireValue(string text)
    {
        foreach (Dockerfile file in ParseBoth(text))
        {
            Assert.Equal(text, file.ToString());
            Assert.IsAssignableFrom<ParserDirective>(Assert.Single(file.Items));
        }
    }

    [Theory]
    [InlineData("#syntax=a\n#SYNTAX=b\n")]
    [InlineData("#check=skip=all\n#Check=error=true\n")]
    [InlineData("#escape=\\\n#ESCAPE=`\n")]
    [InlineData("#escape=x\n")]
    public void InvalidHeaderFailsStrictAndRecoversAsComment(string header)
    {
        string text = header + "#syntax=ignored\nFROM scratch\n";
        Assert.Throws<ParseException>(() => Dockerfile.Parse(text));
        DockerfileParseResult strict = Dockerfile.TryParse(text);
        Assert.Null(strict.Dockerfile);
        Assert.Equal("DFP004", Assert.Single(strict.Diagnostics).Code);

        DockerfileParseResult recovered = Dockerfile.TryParse(text,
            new DockerfileParseOptions { Mode = DockerfileParseMode.Recover });
        Assert.False(recovered.Success);
        Assert.Equal("DFP004", Assert.Single(recovered.Diagnostics).Code);
        Assert.Equal(text, recovered.Dockerfile!.ToString());
        Assert.Empty(recovered.Dockerfile.Items.OfType<MalformedConstruct>());
        Assert.IsType<Comment>(recovered.Dockerfile.Items[header.Count(c => c == '\n') - 1]);
        Assert.Single(recovered.Dockerfile.Items.OfType<FromInstruction>());
        Assert.Equal('\\', recovered.Dockerfile.EscapeChar);
    }

    [Fact]
    public void UnknownCommentPreventsLaterEscapeFromChangingContinuation()
    {
        const string text = "#unknown=value\n#escape=`\nFROM \\\nscratch\n";
        foreach (Dockerfile file in ParseBoth(text))
        {
            Assert.Equal(text, file.ToString());
            Assert.Empty(file.Items.OfType<ParserDirective>());
            Assert.Single(file.Items.OfType<FromInstruction>());
        }
    }

    [Theory]
    [InlineData("\uFEFF")]
    [InlineData("\uFEFF#syntax=docker/dockerfile:1")]
    [InlineData("\uFEFF# ordinary comment\r\n#escape=`\r\nFROM scratch")]
    [InlineData("\uFEFFFROM scratch\n")]
    [InlineData("\uFEFF#escape=`\r\nFROM `\r\nscratch\n")]
    public void InitialBomIsPreservedWithOriginalSpans(string text)
    {
        foreach (Dockerfile file in ParseBoth(text))
        {
            SourceSpanTests.AssertPartition(text, file);
            Assert.Equal(text, file.ToString());
        }
    }

    [Fact]
    public void DuplicateDiagnosticCoversTheOffendingOriginalLine()
    {
        const string first = "\uFEFF#check=skip=all\r\n";
        const string duplicate = "\t#ChEcK=error=true";
        DockerfileParseResult result = Dockerfile.TryParse(first + duplicate,
            new DockerfileParseOptions { Mode = DockerfileParseMode.Recover });
        DockerfileDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(new SourcePosition(first.Length, 2, 1), diagnostic.SourceSpan.Start);
        Assert.Equal(duplicate.Length, diagnostic.SourceSpan.Length);
        SourceSpanTests.AssertPartition(first + duplicate, result.Dockerfile!);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.20")]
    [InlineData("99.123.456-labs")]
    public void VersionMetadataNeverGatesInstructionParsing(string version)
    {
        string text = $"#syntax=docker/dockerfile:{version}\nFROM scratch\nCOPY --parents a /b\n";
        foreach (Dockerfile file in ParseBoth(text))
        {
            Assert.Equal(text, file.ToString());
            Assert.True(Assert.Single(file.Items.OfType<CopyInstruction>()).Parents);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("# ordinary comment\n")]
    [InlineData("#unknown=value\n")]
    [InlineData("#escape=x\n")]
    [InlineData("#SYNTAX=duplicate\n")]
    [InlineData("FROM scratch\n")]
    public void HeaderQueriesDoNotSerializeConstructsAfterTheHeader(string terminator)
    {
        CountingConstruct body = new("RUN echo body\n");
        Dockerfile file = new(new DockerfileConstruct[]
        {
            SyntaxDirective.Parse("#syntax=docker/dockerfile:1\n"),
            new CountingConstruct(terminator)
        }.Concat(Enumerable.Repeat(body, 1000)));

        Assert.Equal("docker/dockerfile:1", file.Frontend.Reference);
        Assert.Equal('\\', file.EscapeChar);
        Assert.Equal(0, body.SerializationCount);
    }

    [Theory]
    [InlineData("", '\\', null)]
    [InlineData("\uFEFF", '\\', null)]
    [InlineData("#syntax=docker/dockerfile:1", '\\', "docker/dockerfile:1")]
    [InlineData("\uFEFF \t#syntax=docker/dockerfile:1\r\n#escape=`\r\nFROM scratch\n", '`', "docker/dockerfile:1")]
    [InlineData("#escape=`\n#escape=\\\n#syntax=ignored", '`', null)]
    [InlineData("#syntax=docker/dockerfile:1\n\n#escape=`", '\\', "docker/dockerfile:1")]
    [InlineData("#syntax=docker/dockerfile:1\n\uFEFF#escape=`", '\\', "docker/dockerfile:1")]
    [InlineData("# ordinary comment\n#syntax=ignored", '\\', null)]
    public void HeaderQueriesPreservePhysicalLinesAcrossConstructBoundaries(
        string text, char escapeChar, string? reference)
    {
        for (int split = 0; split <= text.Length; split++)
        {
            Dockerfile file = new(new DockerfileConstruct[]
            {
                new CountingConstruct(text.Substring(0, split)),
                new CountingConstruct(text.Substring(split))
            });

            Assert.Equal(escapeChar, file.EscapeChar);
            Assert.Equal(reference, file.Frontend.Reference);
            Assert.Equal(text, file.ToString());
        }
    }

    [Fact]
    public void HeaderQueriesReflectTokenEditsWithoutChangingPreviousSnapshots()
    {
        SyntaxDirective syntax = SyntaxDirective.Parse("#syntax=docker/dockerfile:1\n");
        EscapeDirective escape = EscapeDirective.Parse("#escape=`\n");
        Dockerfile file = new(new DockerfileConstruct[] { syntax, escape });
        DockerfileFrontendMetadata original = file.Frontend;
        Assert.Equal('`', file.EscapeChar);

        syntax.DirectiveValueToken.Value = "example.com/frontend:2";
        escape.DirectiveValueToken.Value = "\\";

        Assert.Equal("docker/dockerfile:1", original.Reference);
        Assert.Equal("example.com/frontend:2", file.Frontend.Reference);
        Assert.Equal('\\', file.EscapeChar);
    }

    private sealed class CountingConstruct(string text) : DockerfileConstruct(new[] { new StringToken(text) })
    {
        public int SerializationCount { get; private set; }

        public override ConstructType Type => ConstructType.Comment;

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            SerializationCount++;
            return base.GetUnderlyingValue(options);
        }
    }

    private static IEnumerable<Dockerfile> ParseBoth(string text)
    {
        yield return Dockerfile.Parse(text);
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        yield return result.Dockerfile!;
    }
}
