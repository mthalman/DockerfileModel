namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Captures foundational source-preservation, required-cardinality, and nested-context guarantees.
/// </summary>
public class StructuralEditingCharacterizationTests
{
    /// <summary>
    /// Establishes that adding a source changes only the source region, not destination or following construct identity.
    /// </summary>
    /// <param name="newline">The existing line separator that the edit must preserve.</param>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void AddingSourcePreservesDestinationAndFollowingText(string newline)
    {
        Dockerfile file = Dockerfile.Parse($"COPY one /out/{newline}RUN echo unchanged{newline}");
        CopyInstruction copy = Assert.IsType<CopyInstruction>(file.Items[0]);
        var destination = copy.DestinationToken;
        var following = file.Items[1];

        copy.Sources.Add("two");

        Assert.Equal($"COPY one two /out/{newline}RUN echo unchanged{newline}", file.ToString());
        Assert.Same(destination, copy.DestinationToken);
        Assert.Same(following, file.Items[1]);
    }

    /// <summary>
    /// Requires failed removal of all EXPOSE operands to preserve both the original text and token list.
    /// </summary>
    [Fact]
    public void ClearingRequiredPortsFailsWithoutMutation()
    {
        ExposeInstruction instruction = ExposeInstruction.Parse("EXPOSE 80 443");
        var tokens = instruction.Tokens.ToArray();

        Assert.Throws<InvalidOperationException>(() => instruction.Ports.Clear());

        Assert.Equal("EXPOSE 80 443", instruction.ToString());
        Assert.Equal(tokens, instruction.Tokens);
    }

    /// <summary>
    /// Ensures projected mount adoption validates nested escape context without changing either the owner or input.
    /// </summary>
    [Fact]
    public void MountProjectionCannotHideAnIncompatibleNestedEscapeContext()
    {
        RunInstruction run = RunInstruction.Parse("RUN echo ok", '`');
        Mount mount = Mount.Parse("target=/cache");
        var command = run.Command;

        Assert.Throws<InvalidOperationException>(() => run.Mounts.Add(mount));

        Assert.Empty(run.Mounts);
        Assert.Same(command, run.Command);
        Assert.Equal("RUN echo ok", run.ToString());
        Assert.Equal("target=/cache", mount.ToString());
    }
}
