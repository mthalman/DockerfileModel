using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Exercises deterministic edit sequences against independent list models to detect cumulative state corruption.
/// </summary>
public class StructuralEditingSequenceTests
{
    /// <summary>
    /// Checks source values, destination identity, unrelated instruction text, and reparsing after every edit.
    /// </summary>
    /// <param name="seed">The random seed making the edit sequence reproducible.</param>
    /// <param name="newline">The document's physical line separator.</param>
    /// <param name="escapeChar">The document's continuation escape character.</param>
    [Theory]
    [InlineData(1, "\n", '\\')]
    [InlineData(42, "\n", '\\')]
    [InlineData(1, "\r\n", '\\')]
    [InlineData(42, "\r\n", '\\')]
    [InlineData(1, "\n", '`')]
    [InlineData(42, "\n", '`')]
    [InlineData(1, "\r\n", '`')]
    [InlineData(42, "\r\n", '`')]
    public void SourceEditSequencesKeepSemanticValuesAndUnrelatedObjects(int seed, string newline, char escapeChar)
    {
        string header = escapeChar == '`' ? $"# escape=`{newline}" : string.Empty;
        Dockerfile file = Dockerfile.Parse($"{header}FROM alpine{newline}COPY src0 /app{newline}RUN echo unchanged{newline}");
        CopyInstruction copy = Assert.Single(file.Items.OfType<CopyInstruction>());
        FromInstruction from = Assert.Single(file.Items.OfType<FromInstruction>());
        RunInstruction run = Assert.Single(file.Items.OfType<RunInstruction>());
        LiteralToken destination = copy.DestinationToken!;
        string fromText = from.ToString();
        string runText = run.ToString();
        List<string> expected = ["src0"];
        Random random = new(seed);

        for (int step = 0; step < 60; step++)
        {
            int index = random.Next(expected.Count);
            string value = step % 2 == 0 ? $"source/{step}" : $"source path {step}";
            switch (random.Next(4))
            {
                case 0:
                    copy.Sources.Insert(index, value);
                    expected.Insert(index, value);
                    break;
                case 1:
                    copy.Sources[index] = value;
                    expected[index] = value;
                    break;
                case 2 when expected.Count > 1:
                    copy.Sources.RemoveAt(index);
                    expected.RemoveAt(index);
                    break;
                default:
                    int target = random.Next(expected.Count);
                    copy.SourceTokens.Move(index, target);
                    string moved = expected[index];
                    expected.RemoveAt(index);
                    expected.Insert(target, moved);
                    break;
            }

            Assert.Equal(expected, copy.Sources);
            Assert.Same(destination, copy.DestinationToken);
            Assert.Equal(fromText, from.ToString());
            Assert.Equal(runText, run.ToString());
            Dockerfile parsed = Dockerfile.Parse(file.ToString());
            Assert.Equal(file.ToString(), parsed.ToString());
            Assert.Equal(expected, Assert.Single(parsed.Items.OfType<CopyInstruction>()).Sources);
        }
    }

    /// <summary>
    /// Checks document ordering and object identity after each edit while keeping any active escape directive in place.
    /// </summary>
    /// <param name="seed">The random seed making the edit sequence reproducible.</param>
    /// <param name="newline">The initial document's physical line separator.</param>
    /// <param name="escapeChar">The context shared by the document and incoming instructions.</param>
    [Theory]
    [InlineData(1, "\n", '\\')]
    [InlineData(42, "\r\n", '\\')]
    [InlineData(1, "\n", '`')]
    [InlineData(42, "\r\n", '`')]
    public void DocumentEditSequencesKeepListOrderAndRoundTrip(int seed, string newline, char escapeChar)
    {
        string header = escapeChar == '`' ? $"# escape=`{newline}" : string.Empty;
        Dockerfile file = Dockerfile.Parse($"{header}FROM alpine{newline}RUN echo initial{newline}");
        List<DockerfileConstruct> expected = file.Items.ToList();
        int firstEditable = escapeChar == '`' ? 1 : 0;
        Random random = new(seed);

        for (int step = 0; step < 60; step++)
        {
            int index = random.Next(firstEditable, expected.Count + 1);
            DockerfileConstruct incoming = step % 3 == 0
                ? new Comment($" item {step}")
                : new RunInstruction($"echo {step}", escapeChar: escapeChar);
            switch (index == expected.Count ? 0 : random.Next(4))
            {
                case 0:
                    file.Items.Insert(index, incoming);
                    expected.Insert(index, incoming);
                    break;
                case 1:
                    file.Items[index] = incoming;
                    expected[index] = incoming;
                    break;
                case 2:
                    file.Items.RemoveAt(index);
                    expected.RemoveAt(index);
                    break;
                default:
                    int target = random.Next(firstEditable, expected.Count);
                    file.Items.Move(index, target);
                    DockerfileConstruct moved = expected[index];
                    expected.RemoveAt(index);
                    expected.Insert(target, moved);
                    break;
            }

            Assert.Equal(expected.Count, file.Items.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Same(expected[i], file.Items[i]);
            }
            Assert.Equal(file.ToString(), Dockerfile.Parse(file.ToString()).ToString());
        }
    }
}
