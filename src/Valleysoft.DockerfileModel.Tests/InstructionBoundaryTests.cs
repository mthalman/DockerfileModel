using System.Diagnostics;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class InstructionBoundaryTests
{
    [Theory]
    [InlineData('\\', 1)]
    [InlineData('\\', 2)]
    [InlineData('\\', 3)]
    [InlineData('`', 1)]
    [InlineData('`', 2)]
    [InlineData('`', 3)]
    public void RepeatedEscapesFollowUpstreamContinuationBoundaries(char escapeChar, int count)
    {
        foreach (string newline in new[] { "\n", "\r\n" })
        foreach (string trailingWhitespace in new[] { "", " \t" })
        foreach (bool explicitDirective in new[] { false, true })
        {
            if (escapeChar == '`' && !explicitDirective)
            {
                continue;
            }
            string directive = explicitDirective ? $"# escape={escapeChar}{newline}" : "";
            string runText = "RUN echo " + new string(escapeChar, count) + trailingWhitespace + newline;
            string fromText = "FROM scratch" + newline;
            string text = directive + runText + fromText;

            foreach (Dockerfile dockerfile in ParseBoth(text))
            {
                Instruction[] instructions = dockerfile.Items.OfType<Instruction>().ToArray();
                Assert.Equal(count == 1 ? 1 : 2, instructions.Length);
                Assert.IsType<RunInstruction>(instructions[0]);
                Assert.Equal(count == 1 ? runText + fromText : runText, instructions[0].ToString());
                if (count > 1)
                {
                    Assert.IsType<FromInstruction>(instructions[1]);
                    Assert.Equal(fromText, instructions[1].ToString());
                }
            }
        }
    }

    [Theory]
    [InlineData('\\', '`')]
    [InlineData('`', '\\')]
    public void InactiveEscapeDoesNotContinueInstructions(char escapeChar, char inactiveEscape)
    {
        foreach (string newline in new[] { "\n", "\r\n" })
        {
            string text = $"# escape={escapeChar}{newline}RUN echo {inactiveEscape}{newline}FROM scratch{newline}";
            foreach (Dockerfile dockerfile in ParseBoth(text))
            {
                Assert.Equal(2, dockerfile.Items.OfType<Instruction>().Count());
                Assert.Single(dockerfile.Items.OfType<FromInstruction>());
            }
        }
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('`')]
    public void GenuineContinuationPreservesCommentsAndFollowingInstruction(char escapeChar)
    {
        foreach (string newline in new[] { "\n", "\r\n" })
        {
            string runText = $"RUN echo {escapeChar}{newline}# continued comment {escapeChar}{newline}done{newline}";
            string text = $"# escape={escapeChar}{newline}" + runText + "FROM scratch" + newline;
            foreach (Dockerfile dockerfile in ParseBoth(text))
            {
                Assert.Equal(2, dockerfile.Items.OfType<Instruction>().Count());
                Assert.Equal(runText, Assert.Single(dockerfile.Items.OfType<RunInstruction>()).ToString());
                Assert.Single(dockerfile.Items.OfType<FromInstruction>());
            }
        }
    }

    [Theory]
    [InlineData("COPY")]
    [InlineData("ADD")]
    public void FileTransferHereStringSourceDoesNotOpenHeredoc(string instruction)
    {
        foreach (string newline in new[] { "\n", "\r\n" })
        foreach (char escapeChar in new[] { '\\', '`' })
        {
            string instructionText = instruction + " <<<word /dest" + newline;
            string text = $"# escape={escapeChar}{newline}" + instructionText + "FROM scratch" + newline;
            foreach (Dockerfile dockerfile in ParseBoth(text))
            {
                Assert.Equal(2, dockerfile.Items.OfType<Instruction>().Count());
                Instruction transfer = dockerfile.Items.OfType<Instruction>().First();
                Assert.Empty(transfer.Tokens.OfType<HeredocMarkerToken>());
                Assert.Equal(instructionText, transfer.ToString());
                Assert.Single(dockerfile.Items.OfType<FromInstruction>());
            }
        }
    }

    [Theory]
    [InlineData("<<<word")]
    [InlineData("<<<EOF")]
    [InlineData("<<<<word")]
    [InlineData("<<<'word'")]
    [InlineData("<<<\"word\"")]
    [InlineData("cat <<<word")]
    [InlineData("<<<-word")]
    public void HereStringsDoNotConsumeFollowingInstructions(string command)
    {
        foreach (string newline in new[] { "\n", "\r\n" })
        foreach (char escapeChar in new[] { '\\', '`' })
        {
            string runText = "RUN " + command + newline;
            string text = $"# escape={escapeChar}{newline}" + runText + "FROM scratch" + newline;
            foreach (Dockerfile dockerfile in ParseBoth(text))
            {
                Assert.Equal(2, dockerfile.Items.OfType<Instruction>().Count());
                RunInstruction run = Assert.Single(dockerfile.Items.OfType<RunInstruction>());
                Assert.Empty(run.HeredocMarkerTokens);
                Assert.IsType<ShellFormCommand>(run.Command);
                Assert.Equal(runText, run.ToString());
                Assert.Single(dockerfile.Items.OfType<FromInstruction>());
            }

            RunInstruction standalone = RunInstruction.Parse(runText, escapeChar);
            Assert.Empty(standalone.HeredocMarkerTokens);
            Assert.Equal(runText, standalone.ToString());
        }
    }

    [Theory]
    [InlineData("<<EOF <<<word", "body\nEOF\n", "EOF")]
    [InlineData("<<-EOF <<<\"word\"", "\tbody\n\tEOF\n", "EOF")]
    [InlineData("<<'EOF' <<SECOND <<<word", "one\nEOF\ntwo\nSECOND\n", "EOF", "SECOND")]
    public void HereStringsAlongsideGenuineHeredocsAreNotMarkers(
        string command, string body, params string[] delimiters)
    {
        foreach (string newline in new[] { "\n", "\r\n" })
        foreach (char escapeChar in new[] { '\\', '`' })
        {
            string runText = ("RUN " + command + "\n" + body).ReplaceLineEndings(newline);
            string text = $"# escape={escapeChar}{newline}" + runText + "FROM scratch" + newline;
            foreach (Dockerfile dockerfile in ParseBoth(text))
            {
                Assert.Equal(2, dockerfile.Items.OfType<Instruction>().Count());
                RunInstruction run = Assert.Single(dockerfile.Items.OfType<RunInstruction>());
                Assert.Equal(delimiters, run.HeredocMarkerTokens.Select(marker => marker.DelimiterName));
                Assert.Equal(delimiters.Length, run.HeredocBodyTokens.Count());
                Assert.Equal(runText, run.ToString());
                Assert.Single(dockerfile.Items.OfType<FromInstruction>());
            }

            RunInstruction standalone = RunInstruction.Parse(runText, escapeChar);
            Assert.Equal(delimiters, standalone.HeredocMarkerTokens.Select(marker => marker.DelimiterName));
            Assert.Equal(runText, standalone.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongHereStringsHaveBoundedScanningTime(bool withHeredoc)
    {
        string text = (withHeredoc ? "RUN <<EOF " : "RUN ") + new string('<', 16000) + "word\n" +
            (withHeredoc ? "body\nEOF\n" : "");
        DockerfileParser.ExtractHeredocDelimiters("RUN <<<warmup\n");
        RunInstruction.Parse("RUN <<EOF <<<warmup\nbody\nEOF\n");

        Stopwatch watch = Stopwatch.StartNew();
        List<DockerfileParser.HeredocDelimiterInfo> delimiters =
            DockerfileParser.ExtractHeredocDelimiters(text.Split('\n')[0]);
        TimeSpan framingTime = watch.Elapsed;
        Assert.Equal(withHeredoc ? 1 : 0, delimiters.Count);
        Assert.True(framingTime < TimeSpan.FromSeconds(5), $"Framing scan took {framingTime}.");

        watch.Restart();
        RunInstruction instruction = RunInstruction.Parse(text);
        TimeSpan tokenizationTime = watch.Elapsed;
        Assert.Equal(withHeredoc ? 1 : 0, instruction.HeredocMarkerTokens.Count());
        Assert.Equal(text, instruction.ToString());
        Assert.True(tokenizationTime < TimeSpan.FromSeconds(5), $"Token scan took {tokenizationTime}.");
    }

    [Fact]
    public void RejectedHereStringDoesNotHideLaterHeredoc()
    {
        List<DockerfileParser.HeredocDelimiterInfo> delimiters =
            DockerfileParser.ExtractHeredocDelimiters("RUN <<<word <<EOF");
        Assert.Equal("EOF", Assert.Single(delimiters).Delimiter);
    }

    private static Dockerfile[] ParseBoth(string text)
    {
        Dockerfile legacy = Dockerfile.Parse(text);
        Assert.Equal(text, legacy.ToString());
        DockerfileParseResult diagnostic = Dockerfile.TryParse(text);
        Assert.True(diagnostic.Success);
        Dockerfile parsed = Assert.IsType<Dockerfile>(diagnostic.Dockerfile);
        Assert.Equal(text, parsed.ToString());
        return new[] { legacy, parsed };
    }
}
