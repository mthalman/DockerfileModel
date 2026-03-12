using FsCheck;
using FsCheck.Fluent;
using Valleysoft.DockerfileModel.Tests.Generators;

namespace Valleysoft.DockerfileModel.DiffTest;

/// <summary>
/// Wraps the linked FsCheck generators (DockerfileArbitraries) to produce
/// random test inputs for differential testing. Returns a list of
/// (InstructionType, Text, EscapeChar) tuples distributed evenly across all 18
/// Dockerfile instruction types, shuffled with a fixed seed for reproducibility.
/// About 10% of inputs use backtick (`) as escape char instead of backslash (\).
/// </summary>
public static class InputGenerator
{
    private const int SampleSize = 50;

    public static List<(string InstructionType, string Text, char EscapeChar)> Generate(int count, int seed = 42)
    {
        // All instruction generators with their type labels.
        // The first 18 are the standard instruction generators; the remaining
        // are edge-case generators targeting specific bugs found via
        // differential testing.
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
            // Edge-case generators targeting specific differential test bugs
            ("RUN", DockerfileArbitraries.RunHeredocInstruction()),        // Bugs 7-11: heredoc
            ("COPY", DockerfileArbitraries.CopyHeredocInstruction()),      // Bugs 7-11: heredoc
            ("ADD", DockerfileArbitraries.AddHeredocInstruction()),         // Bugs 7-11: heredoc
            ("COPY", DockerfileArbitraries.CopyEmptyFlagInstruction()),    // Bug 12: empty flags
            ("ADD", DockerfileArbitraries.AddEmptyFlagInstruction()),      // Bug 12: empty flags
            // FromEmptyPlatformInstruction excluded: C# throws a parse error
            // on FROM --platform= (empty value), which is a known C# limitation,
            // not a Lean issue.
            ("RUN", DockerfileArbitraries.RunEmptyFlagInstruction()),      // Bug 12: empty flags
            // #259: empty exec-form arrays
            ("VOLUME", DockerfileArbitraries.VolumeEmptyExecInstruction()),  // #259
            ("COPY", DockerfileArbitraries.CopyEmptyExecInstruction()),     // #259
            ("ADD", DockerfileArbitraries.AddEmptyExecInstruction()),       // #259
            // #260: quoted file paths in COPY/ADD
            ("COPY", DockerfileArbitraries.CopyQuotedPathInstruction()),    // #260
            ("ADD", DockerfileArbitraries.AddQuotedPathInstruction()),      // #260
            // #261: variable :? modifier
            ("FROM", DockerfileArbitraries.FromErrorModifierInstruction()), // #261
            ("ARG", DockerfileArbitraries.ArgErrorModifierInstruction()),   // #261
            // #262: variable default value with slash
            ("WORKDIR", DockerfileArbitraries.WorkdirSlashDefaultInstruction()), // #262
            ("ENV", DockerfileArbitraries.EnvSlashDefaultInstruction()),    // #262
            // #263: mount value trailing whitespace
            ("RUN", DockerfileArbitraries.RunMinimalMountInstruction()),    // #263
            // #264: trailing whitespace
            ("FROM", DockerfileArbitraries.FromTrailingWhitespaceInstruction()),  // #264
            ("ENV", DockerfileArbitraries.EnvTrailingWhitespaceInstruction()),    // #264
            ("COPY", DockerfileArbitraries.CopyTrailingWhitespaceInstruction()), // #264
            // #265: hash in shell-form and values
            ("RUN", DockerfileArbitraries.RunHashInShellInstruction()),     // #265
            ("CMD", DockerfileArbitraries.CmdHashInShellInstruction()),     // #265
            ("LABEL", DockerfileArbitraries.LabelHashInValueInstruction()), // #265
            // #266: line continuation in flag values
            ("COPY", DockerfileArbitraries.CopyFlagLineContinuationInstruction()), // #266
            ("ADD", DockerfileArbitraries.AddFlagLineContinuationInstruction()),   // #266
        };

        int perType = count / generators.Length;
        int remainder = count % generators.Length;

        List<(string InstructionType, string Text, char EscapeChar)> inputs = new();
        Random escapeRng = new(seed + 1); // separate RNG for escape char selection

        for (int g = 0; g < generators.Length; g++)
        {
            int n = perType + (g < remainder ? 1 : 0);
            var samples = generators[g].Gen.Sample(SampleSize, n);
            foreach (string text in samples)
            {
                // ~10% of inputs use backtick escape char
                if (escapeRng.NextDouble() < 0.10)
                {
                    // Replace backslash continuations with backtick continuations
                    string backtickText = text.Replace("\\\n", "`\n").Replace("\\\r\n", "`\r\n");
                    inputs.Add((generators[g].Type, backtickText, '`'));
                }
                else
                {
                    inputs.Add((generators[g].Type, text, '\\'));
                }
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
