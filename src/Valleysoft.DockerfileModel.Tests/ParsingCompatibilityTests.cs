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
