namespace Valleysoft.DockerfileModel.Tests;

public class RecoveryBoundaryFuzzTests
{
    [Fact]
    public void CorruptedInstructionsNeverThrowOrLoseSource()
    {
        string[] names = { "FROM", "RUN", "CMD", "ENTRYPOINT", "COPY", "ADD", "ENV", "ARG",
            "LABEL", "USER", "WORKDIR", "EXPOSE", "VOLUME", "SHELL", "STOPSIGNAL", "HEALTHCHECK",
            "ONBUILD", "MAINTAINER", "FUTURE-COPY", "# escape=", "# syntax=" };
        string[] fragments = { "", " ", "\t", "\"", "'", "\\", "\\\n", "#", "# comment\n",
            "CMD", "RUN", "FROM", "--", "--mount=", "--interval=..s", "--network=host",
            "=", "${", "$x", "[", "]", "[]", "\0", "\r", "abc", "<<EOF", "<<<word",
            "<< EOF", "<<'EOF'", "<<EO\\\nF", "\nEOF\n", "\n", "\r\n", "\uD83D\uDE80" };
        Random random = new(347);
        DockerfileParseOptions options = new()
        {
            Mode = DockerfileParseMode.Recover,
            UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
        };

        for (int sample = 0; sample < 2000; sample++)
        {
            string corrupted = names[random.Next(names.Length)] + " " +
                string.Concat(Enumerable.Range(0, random.Next(1, 7)).Select(_ => fragments[random.Next(fragments.Length)]));
            string text = "FROM scratch\n" + corrupted + "\nRUN echo after\n";
            DockerfileParseResult result = Dockerfile.TryParse(text, options);
            Dockerfile model = Assert.IsType<Dockerfile>(result.Dockerfile);
            Assert.Equal(text, model.ToString());
            int offset = 0;
            foreach (DockerfileConstruct construct in model.Items)
            {
                SourceSpan span = Assert.IsType<SourceSpan>(construct.SourceSpan);
                Assert.Equal(offset, span.Start.Offset);
                Assert.Equal(construct.ToString(), text.Substring(span.Start.Offset, span.Length));
                offset = span.End.Offset;
            }
            Assert.Equal(text.Length, offset);
            foreach (DockerfileDiagnostic diagnostic in result.Diagnostics)
            {
                Assert.InRange(diagnostic.SourceSpan.Start.Offset, 0, text.Length);
                Assert.InRange(diagnostic.SourceSpan.End.Offset, diagnostic.SourceSpan.Start.Offset, text.Length);
            }
        }
    }
}
