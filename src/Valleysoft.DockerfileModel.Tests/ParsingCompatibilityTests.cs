namespace Valleysoft.DockerfileModel.Tests;

public class ParsingCompatibilityTests
{
    [Theory]
    [InlineData("RUN <<EOF\nbody\n")]
    [InlineData("# escape=`\nFROM scratch\nRUN echo `\n # comment\n hello")]
    [InlineData("# custom=value\nFROM scratch")]
    [InlineData("FROM scratch\r\nRUN echo hello\n")]
    public void LegacyParsePreservesAcceptedText(string text)
    {
        Assert.Equal(text, Dockerfile.Parse(text).ToString());
    }

    [Theory]
    [InlineData("COPY", null)]
    [InlineData("COPY src", "src")]
    [InlineData("COPY []", null)]
    [InlineData("ADD", null)]
    [InlineData("ADD src", "src")]
    [InlineData("ADD []", null)]
    public void FileTransferOperandCountsPreserveLegacyBehavior(string instructionText, string? destination)
    {
        FileTransferInstruction standalone = instructionText.StartsWith("COPY", StringComparison.Ordinal)
            ? CopyInstruction.Parse(instructionText)
            : AddInstruction.Parse(instructionText);
        Assert.Equal(instructionText, standalone.ToString());
        Assert.Empty(standalone.Sources);
        Assert.Equal(destination, standalone.Destination);

        string text = instructionText + "\nFROM scratch\n";
        AssertModel(Dockerfile.Parse(text));
        foreach (DockerfileParseMode mode in Enum.GetValues<DockerfileParseMode>())
        {
            DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions { Mode = mode });
            Assert.True(result.Success);
            Assert.Empty(result.Diagnostics);
            AssertModel(result.Dockerfile!);
        }

        void AssertModel(Dockerfile model)
        {
            Assert.Equal(2, model.Items.Count);
            FileTransferInstruction instruction = Assert.IsAssignableFrom<FileTransferInstruction>(model.Items[0]);
            Assert.IsType(standalone.GetType(), instruction);
            Assert.Empty(instruction.Sources);
            Assert.Equal(destination, instruction.Destination);
            Assert.IsType<FromInstruction>(model.Items[1]);
            SourceSpanTests.AssertPartition(text, model);
        }
    }

    [Fact]
    public void LegacyGenericParserRejectsUnknownInstruction()
    {
        Assert.Throws<ParseException>(() => GenericInstruction.Parse("FUTURE-COPY source target"));
    }

    [Theory]
    [InlineData("RUN")]
    [InlineData("CMD")]
    [InlineData("ENTRYPOINT")]
    [InlineData("HEALTHCHECK CMD")]
    public void LegacyEmptyCommandsKeepTheirExceptionContract(string text)
    {
        Assert.Throws<ArgumentException>(() => Dockerfile.Parse(text));
        DockerfileParseResult result = Dockerfile.TryParse(text);
        Assert.False(result.Success);
        Assert.Null(result.Dockerfile);
        Assert.Equal(DockerfileDiagnosticCodes.InvalidSyntax, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void LegacyFailurePositionIsConstructLocal()
    {
        ParseException standalone = Assert.Throws<ParseException>(() => FromInstruction.Parse("FROM"));
        ParseException dockerfile = Assert.Throws<ParseException>(() => Dockerfile.Parse("RUN echo hello\nFROM"));
        Assert.Equal(standalone.Position, dockerfile.Position);
    }
}
