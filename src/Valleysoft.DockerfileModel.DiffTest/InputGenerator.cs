using FsCheck;
using FsCheck.Fluent;
using Valleysoft.DockerfileModel.Tests.Generators;

namespace Valleysoft.DockerfileModel.DiffTest;

/// <summary>
/// Wraps the linked FsCheck generators (DockerfileArbitraries) to produce
/// random test inputs for differential testing. Returns a list of
/// (InstructionType, Text) tuples split evenly between FROM and ARG,
/// shuffled with a fixed seed for reproducibility.
/// </summary>
public static class InputGenerator
{
    private const int SampleSize = 50;

    public static List<(string InstructionType, string Text)> Generate(int count, int seed = 42)
    {
        int fromCount = count / 2;
        int argCount = count - fromCount;

        List<(string InstructionType, string Text)> inputs = new();

        // Generate FROM instructions using FsCheck 3.x Sample(size, count) extension
        Gen<string> fromGen = DockerfileArbitraries.FromInstruction();
        IReadOnlyList<string> fromInputs = fromGen.Sample(SampleSize, fromCount);
        foreach (string text in fromInputs)
        {
            inputs.Add(("FROM", text));
        }

        // Generate ARG instructions
        Gen<string> argGen = DockerfileArbitraries.ArgInstruction();
        IReadOnlyList<string> argInputs = argGen.Sample(SampleSize, argCount);
        foreach (string text in argInputs)
        {
            inputs.Add(("ARG", text));
        }

        // Shuffle with fixed seed for reproducibility
        Random rng = new(seed);
        for (int i = inputs.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (inputs[i], inputs[j]) = (inputs[j], inputs[i]);
        }

        return inputs;
    }
}
