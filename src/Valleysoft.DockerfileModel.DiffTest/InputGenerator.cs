using FsCheck;
using FsCheck.Fluent;
using Valleysoft.DockerfileModel.Tests.Generators;

namespace Valleysoft.DockerfileModel.DiffTest;

/// <summary>
/// Wraps the linked FsCheck generators (DockerfileArbitraries) to produce
/// random test inputs for differential testing. Returns a list of
/// (InstructionType, Text) tuples distributed evenly across all 18
/// Dockerfile instruction types, shuffled with a fixed seed for reproducibility.
/// </summary>
public static class InputGenerator
{
    private const int SampleSize = 50;

    public static List<(string InstructionType, string Text)> Generate(int count, int seed = 42)
    {
        // All instruction generators with their type labels
        var generators = new (string Type, Gen<string> Gen)[]
        {
            ("FROM", DockerfileArbitraries.FromInstruction()),
            ("ARG", DockerfileArbitraries.ArgInstruction()),
            ("RUN", DockerfileArbitraries.RunInstruction()),
            ("CMD", DockerfileArbitraries.CmdInstruction()),
            ("ENTRYPOINT", DockerfileArbitraries.EntrypointInstruction()),
            ("COPY", DockerfileArbitraries.CopyInstruction()),
            ("ADD", DockerfileArbitraries.AddInstruction()),
            ("ENV", DockerfileArbitraries.EnvInstruction()),
            ("EXPOSE", DockerfileArbitraries.ExposeInstruction()),
            ("VOLUME", DockerfileArbitraries.VolumeInstruction()),
            ("USER", DockerfileArbitraries.UserInstruction()),
            ("WORKDIR", DockerfileArbitraries.WorkdirInstruction()),
            ("LABEL", DockerfileArbitraries.LabelInstruction()),
            ("STOPSIGNAL", DockerfileArbitraries.StopSignalInstruction()),
            ("HEALTHCHECK", DockerfileArbitraries.HealthCheckInstruction()),
            ("SHELL", DockerfileArbitraries.ShellInstruction()),
            ("MAINTAINER", DockerfileArbitraries.MaintainerInstruction()),
            ("ONBUILD", DockerfileArbitraries.OnBuildInstruction()),
        };

        int perType = count / generators.Length;
        int remainder = count % generators.Length;

        List<(string InstructionType, string Text)> inputs = new();

        for (int g = 0; g < generators.Length; g++)
        {
            int n = perType + (g < remainder ? 1 : 0);
            var samples = generators[g].Gen.Sample(SampleSize, n);
            foreach (string text in samples)
            {
                inputs.Add((generators[g].Type, text));
            }
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
