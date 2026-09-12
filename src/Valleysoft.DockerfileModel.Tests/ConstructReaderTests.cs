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
}
