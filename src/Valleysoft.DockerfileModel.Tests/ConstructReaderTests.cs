namespace Valleysoft.DockerfileModel.Tests;

public class ConstructReaderTests
{
    [Theory]
    [InlineData("RUN <<EOF \\\n && echo ready\nbody\nEOF\n")]
    [InlineData("RUN <<FIRST \\\n <<SECOND\none\nFIRST\ntwo\nSECOND\n")]
    [InlineData("ONBUILD RUN <<EOF\nFROM body\nEOF\n")]
    [InlineData("R\\\nUN <<EOF\nbody\nEOF\n")]
    [InlineData("RUN echo \"\\\n<<NOT_A_MARKER\"\n")]
    [InlineData("RUN <<EOF # <<NOT_A_MARKER\nbody\nEOF\n")]
    [InlineData("RUN <<-\"EOF\"\n\tbody\n\tEOF\n")]
    [InlineData("RUN echo hi \\\n# comment \\\n\n && echo done\n")]
    public void ReadsCompleteLogicalRegion(string region)
    {
        string text = region + "FROM scratch\n";
        ConstructReader.Region result = ConstructReader.Read(text, 0, '\\');

        Assert.Equal(region.Length, result.End);
        Assert.Null(result.UnterminatedMarker);
    }

    [Theory]
    [InlineData("RUN <<EOF", 4)]
    [InlineData("RUN <<EOF\nRUN echo body\n", 4)]
    [InlineData("RUN <<FIRST \\\n <<SECOND\none\nFIRST\n", 15)]
    public void MissingTerminatorConsumesToEnd(string text, int markerOffset)
    {
        ConstructReader.Region result = ConstructReader.Read(text, 0, '\\');

        Assert.Equal(text.Length, result.End);
        Assert.Equal(markerOffset, result.UnterminatedMarker);
    }

    [Fact]
    public void TopLevelCommentDoesNotContinue()
    {
        string text = "# comment \\\nFROM scratch\n";
        ConstructReader.Region result = ConstructReader.Read(text, 0, '\\');
        Assert.Equal(text.IndexOf('\n') + 1, result.End);
    }

    [Theory]
    [InlineData('\\', 1)]
    [InlineData('\\', 2)]
    [InlineData('\\', 3)]
    [InlineData('`', 1)]
    [InlineData('`', 2)]
    [InlineData('`', 3)]
    public void RepeatedEscapesFollowBuildKitContinuationBoundaries(char escapeChar, int count)
    {
        foreach (string newline in new[] { "\n", "\r\n" })
        {
            string suffix = new string(escapeChar, count) + " \t" + newline;
            string nextLine = "RUN echo next" + newline;
            string header = "RUN echo ready " + suffix;
            ConstructReader.Region ordinary = ConstructReader.Read(
                header + nextLine + "FROM scratch" + newline, 0, escapeChar);
            Assert.Equal(header.Length + (count == 1 ? nextLine.Length : 0), ordinary.End);
            Assert.Null(ordinary.UnterminatedMarker);

            header = "RUN <<EOF " + suffix;
            string regionText = header + nextLine + "body" + newline + "EOF" + newline;
            ConstructReader.Region region = ConstructReader.Read(
                regionText + "FROM scratch" + newline, 0, escapeChar);
            int expectedHeaderEnd = header.Length + (count == 1 ? nextLine.Length : 0);
            Assert.Equal(expectedHeaderEnd, region.HeaderEnd);
            Assert.Equal(expectedHeaderEnd, Assert.Single(region.Heredocs).BodyStart);
            Assert.Equal(regionText.Length, region.End);
            Assert.Null(region.UnterminatedMarker);
        }
    }

    [Theory]
    [InlineData("COPY <<EOF /dest #tail\n")]
    [InlineData("COPY <<EOF \\\n# header 'comment\n \"/dest #name\" #tail\r\n")]
    [InlineData("COPY <\\\n<EOF \\\n /dest #tail\n")]
    [InlineData("RUN <<EOF echo \"#name\" #tail\n")]
    public void TrailingCommentOffsetRefersToOriginalSource(string header)
    {
        const string prefix = "FROM scratch\n";
        string text = prefix + header + "body\nEOF\nFROM next\n";
        ConstructReader.Region result = ConstructReader.Read(text, prefix.Length, '\\');

        Assert.Equal(text.IndexOf("#tail", StringComparison.Ordinal), result.TrailingCommentStart);
        Assert.Equal(prefix.Length + header.Length, result.HeaderEnd);
        Assert.Equal(prefix.Length + header.Length + "body\nEOF\n".Length, result.End);
        Assert.Equal("EOF", Assert.Single(result.Heredocs).Name);
        Assert.Null(result.UnterminatedMarker);
    }
}
