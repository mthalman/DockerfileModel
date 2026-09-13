using FsCheck;
using FsCheck.Fluent;
using Valleysoft.DockerfileModel.TestSupport.Generators;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class RecoveryPropertyTests
{
    [Fact]
    public void GeneratedValidDockerfilesKeepTypedTokenTreesAndSourceSpans()
    {
        foreach (string text in DockerfileArbitraries.ValidDockerfile().Sample(30, 100))
        {
            Dockerfile legacy = Dockerfile.Parse(text);
            DockerfileParseResult result = Dockerfile.TryParse(text);
            Assert.True(result.Success, text + "\n" + string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Assert.Empty(result.Diagnostics);
            Dockerfile model = Assert.IsType<Dockerfile>(result.Dockerfile);
            SourceSpanTests.AssertPartition(text, model);
            Assert.Equal(legacy.Items.Select(item => item.GetType()), model.Items.Select(item => item.GetType()));
            foreach ((DockerfileConstruct expected, DockerfileConstruct actual) in legacy.Items.Zip(model.Items))
            {
                AssertTokenTree(expected, actual);
            }
        }
    }

    [Fact]
    public void GeneratedInstructionsSurviveInjectedRecoveryBoundaries()
    {
        Gen<string> instructions = DockerfileArbitraries.SingleBodyInstruction();
        foreach (string instruction in instructions.Sample(30, 100))
        {
            string suffix = instruction + "\nFROM scratch AS after\n";
            string text = "FROM scratch\nFUTURE-COPY a \\\n# embedded\n\n $opaque\n" +
                "FROM scratch extra tokens\nRUN <<EOF\nFROM body\nEOF\n" + suffix;
            DockerfileParseOptions options = new()
            {
                Mode = DockerfileParseMode.Recover,
                UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
            };
            DockerfileParseResult result = Dockerfile.TryParse(text, options);
            Assert.False(result.Success);
            Assert.Equal(new[] { "DFP002", "DFP001" }, result.Diagnostics.Select(d => d.Code));
            Dockerfile model = Assert.IsType<Dockerfile>(result.Dockerfile);
            SourceSpanTests.AssertPartition(text, model);
            Assert.Single(model.Items.OfType<UnknownInstruction>());
            Assert.Single(model.Items.OfType<MalformedConstruct>());
            Dockerfile standalone = Dockerfile.Parse(suffix);
            DockerfileConstruct[] recoveredSuffix = model.Items.TakeLast(standalone.Items.Count).ToArray();
            foreach ((DockerfileConstruct expected, DockerfileConstruct actual) in standalone.Items.Zip(recoveredSuffix))
            {
                AssertTokenTree(expected, actual);
            }
            DockerfileParseResult repeated = Dockerfile.TryParse(text, options);
            Assert.Equal(result.Diagnostics.Select(d => (d.Code, d.SourceSpan)),
                repeated.Diagnostics.Select(d => (d.Code, d.SourceSpan)));
        }
    }

    [Fact]
    public void GeneratedVariableModelsRemainUnchangedDuringRecoveryInspection()
    {
        foreach (string valid in DockerfileArbitraries.DockerfileWithVariables().Sample(30, 100))
        {
            string text = valid + "\nFUTURE $opaque\nFROM\nRUN echo after\n";
            DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions
            {
                Mode = DockerfileParseMode.Recover,
                UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
            });
            Dockerfile model = Assert.IsType<Dockerfile>(result.Dockerfile);
            SourceSpan?[] spans = model.Items.Select(item => item.SourceSpan).ToArray();
            Assert.Single(new StagesView(model).Stages);
            Assert.Single(model.Items.OfType<MalformedConstruct>());
            model.ResolveVariables();
            Assert.Equal(text, model.ToString());
            Assert.Equal(spans, model.Items.Select(item => item.SourceSpan));
            SourceSpanTests.AssertPartition(text, model);
        }
    }

    private static void AssertTokenTree(Token expected, Token actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.ToString(), actual.ToString());
        if (expected is AggregateToken expectedAggregate)
        {
            AggregateToken actualAggregate = Assert.IsAssignableFrom<AggregateToken>(actual);
            Token[] expectedChildren = expectedAggregate.Tokens.ToArray();
            Token[] actualChildren = actualAggregate.Tokens.ToArray();
            Assert.Equal(expectedChildren.Length, actualChildren.Length);
            string fromChildren = string.Concat(actualChildren.Select(token => token.ToString()));
            if (actual is VariableRefToken)
            {
                fromChildren = "$" + fromChildren;
            }
            if (actual is IQuotableToken quotable && quotable.QuoteChar.HasValue)
            {
                fromChildren = $"{quotable.QuoteChar}{fromChildren}{quotable.QuoteChar}";
            }
            Assert.Equal(actual.ToString(), fromChildren);
            foreach ((Token expectedChild, Token actualChild) in expectedChildren.Zip(actualChildren))
            {
                AssertTokenTree(expectedChild, actualChild);
            }
        }
    }
}
