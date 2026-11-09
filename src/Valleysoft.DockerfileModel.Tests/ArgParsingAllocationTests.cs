namespace Valleysoft.DockerfileModel.Tests;

[CollectionDefinition(nameof(ArgParsingAllocationTests), DisableParallelization = true)]
public sealed class ArgParsingAllocationCollection;

[Collection(nameof(ArgParsingAllocationTests))]
public class ArgParsingAllocationTests
{
    [Theory]
    [InlineData(8192)]
    [InlineData(32768)]
    public void DeclarationBoundaryCaptureDoesNotCopySourceText(int length)
    {
        string syntax = "ARG X=" + new string('x', length);
        int captures = 0;
        void Parse(bool capture)
        {
            if (capture) ArgInstruction.Parse(syntax, '\\', _ => captures++);
            else ArgInstruction.Parse(syntax);
        }
        long Measure(bool capture)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Parse(capture);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Parse(false);
        Parse(true);
        captures = 0;

        long normal = Measure(false);
        long observed = Measure(true);

        Assert.Equal(1, captures);
        Assert.InRange(observed, 0, normal + 4096);
    }
}
