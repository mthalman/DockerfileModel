using FsCheck;
using FsCheck.Fluent;
using Valleysoft.DockerfileModel.TestSupport.Generators;

namespace Valleysoft.DockerfileModel.DiffTest;

/// <summary>
/// Wraps the shared FsCheck generators (DockerfileArbitraries) to produce
/// deterministic <see cref="DiffCase"/> values for differential testing,
/// distributed evenly across instruction and targeted edge-case generators,
/// then shuffled using the supplied seed. Each case includes the metadata
/// required to regenerate its input.
/// </summary>
public static class InputGenerator
{
    private const int SampleSize = 50;

    private sealed record GeneratorSpec(string Name, string InstructionType, Gen<string> Generator);

    public static List<DiffCase> Generate(int count, int seed = 42)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        IReadOnlyList<GeneratorSpec> generators = GetGenerators();
        int perGenerator = count / generators.Count;
        int remainder = count % generators.Count;
        List<DiffCase> inputs = new(count);

        for (int generatorIndex = 0; generatorIndex < generators.Count; generatorIndex++)
        {
            GeneratorSpec spec = generators[generatorIndex];
            int generatorCount = perGenerator + (generatorIndex < remainder ? 1 : 0);

            for (int caseIndex = 0; caseIndex < generatorCount; caseIndex++)
            {
                inputs.Add(GenerateCase(spec, generatorIndex, caseIndex, seed));
            }
        }

        Random shuffle = new(seed);
        for (int i = inputs.Count - 1; i > 0; i--)
        {
            int j = shuffle.Next(i + 1);
            (inputs[i], inputs[j]) = (inputs[j], inputs[i]);
        }

        return inputs;
    }

    public static DiffCase Replay(string generatorName, int caseIndex, int seed)
    {
        IReadOnlyList<GeneratorSpec> generators = GetGenerators();
        int generatorIndex = generators
            .Select((spec, index) => (spec, index))
            .Where(item => string.Equals(item.spec.Name, generatorName, StringComparison.Ordinal))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .Single();

        if (generatorIndex < 0)
        {
            throw new ArgumentException($"Unknown generator '{generatorName}'.", nameof(generatorName));
        }

        if (caseIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(caseIndex));
        }

        return GenerateCase(generators[generatorIndex], generatorIndex, caseIndex, seed)
            with { Source = DiffCaseSource.Replay };
    }

    private static DiffCase GenerateCase(
        GeneratorSpec spec,
        int generatorIndex,
        int caseIndex,
        int seed)
    {
        ulong firstSeed = unchecked((ulong)(uint)seed);
        ulong secondSeed = CreateGamma(generatorIndex, caseIndex);
        string text = spec.Generator.Sample(
            1,
            new Rnd(firstSeed, secondSeed),
            SampleSize).Single();

        Random escapeRandom = new(unchecked(seed * 397 ^ generatorIndex * 31 ^ caseIndex));
        char escapeChar = '\\';
        if (escapeRandom.NextDouble() < 0.10)
        {
            text = text.Replace("\\\r\n", "`\r\n").Replace("\\\n", "`\n");
            escapeChar = '`';
        }

        return new DiffCase(
            $"{spec.Name}-{caseIndex}",
            DiffCaseSource.Generated,
            spec.InstructionType,
            text,
            escapeChar,
            spec.Name,
            seed,
            caseIndex);
    }

    internal static ulong CreateGamma(int generatorIndex, int caseIndex) =>
        unchecked(
            ((ulong)(uint)(generatorIndex + 1) << 33) |
            ((ulong)(uint)(caseIndex + 1) << 1) |
            1UL);

    private static IReadOnlyList<GeneratorSpec> GetGenerators() =>
        new GeneratorSpec[]
        {
            new("from", "FROM", DockerfileArbitraries.FromInstruction()),
            new("arg", "ARG", DockerfileArbitraries.ArgInstruction()),
            new("run", "RUN", DockerfileArbitraries.RunInstruction()),
            new("cmd", "CMD", DockerfileArbitraries.CmdInstruction()),
            new("entrypoint", "ENTRYPOINT", DockerfileArbitraries.EntrypointInstruction()),
            new("copy", "COPY", DockerfileArbitraries.CopyInstruction()),
            new("add", "ADD", DockerfileArbitraries.AddInstruction()),
            new("env", "ENV", DockerfileArbitraries.EnvInstruction()),
            new("expose", "EXPOSE", DockerfileArbitraries.ExposeInstruction()),
            new("volume", "VOLUME", DockerfileArbitraries.VolumeInstruction()),
            new("user", "USER", DockerfileArbitraries.UserInstruction()),
            new("workdir", "WORKDIR", DockerfileArbitraries.WorkdirInstruction()),
            new("label", "LABEL", DockerfileArbitraries.LabelInstruction()),
            new("stopsignal", "STOPSIGNAL", DockerfileArbitraries.StopSignalInstruction()),
            new("healthcheck", "HEALTHCHECK", DockerfileArbitraries.HealthCheckInstruction()),
            new("shell", "SHELL", DockerfileArbitraries.ShellInstruction()),
            new("maintainer", "MAINTAINER", DockerfileArbitraries.MaintainerInstruction()),
            new("onbuild", "ONBUILD", DockerfileArbitraries.OnBuildInstruction()),
            // Edge-case generators targeting specific differential test bugs
            new("run-heredoc", "RUN", DockerfileArbitraries.RunHeredocInstruction()),
            new("copy-heredoc", "COPY", DockerfileArbitraries.CopyHeredocInstruction()),
            new("add-heredoc", "ADD", DockerfileArbitraries.AddHeredocInstruction()),
            new("copy-empty-flag", "COPY", DockerfileArbitraries.CopyEmptyFlagInstruction()),
            new("add-empty-flag", "ADD", DockerfileArbitraries.AddEmptyFlagInstruction()),
            // FromEmptyPlatformInstruction excluded: C# throws a parse error
            // on FROM --platform= (empty value), which is a known C# limitation,
            // not a Lean issue.
            new("run-empty-flag", "RUN", DockerfileArbitraries.RunEmptyFlagInstruction()),
            new("volume-empty-exec", "VOLUME", DockerfileArbitraries.VolumeEmptyExecInstruction()),
            new("copy-empty-exec", "COPY", DockerfileArbitraries.CopyEmptyExecInstruction()),
            new("add-empty-exec", "ADD", DockerfileArbitraries.AddEmptyExecInstruction()),
            new("copy-quoted-path", "COPY", DockerfileArbitraries.CopyQuotedPathInstruction()),
            new("add-quoted-path", "ADD", DockerfileArbitraries.AddQuotedPathInstruction()),
            new("from-error-modifier", "FROM", DockerfileArbitraries.FromErrorModifierInstruction()),
            new("arg-error-modifier", "ARG", DockerfileArbitraries.ArgErrorModifierInstruction()),
            new("workdir-slash-default", "WORKDIR", DockerfileArbitraries.WorkdirSlashDefaultInstruction()),
            new("env-slash-default", "ENV", DockerfileArbitraries.EnvSlashDefaultInstruction()),
            new("run-minimal-mount", "RUN", DockerfileArbitraries.RunMinimalMountInstruction()),
            new("from-trailing-whitespace", "FROM", DockerfileArbitraries.FromTrailingWhitespaceInstruction()),
            new("env-trailing-whitespace", "ENV", DockerfileArbitraries.EnvTrailingWhitespaceInstruction()),
            new("copy-trailing-whitespace", "COPY", DockerfileArbitraries.CopyTrailingWhitespaceInstruction()),
            new("run-hash-shell", "RUN", DockerfileArbitraries.RunHashInShellInstruction()),
            new("cmd-hash-shell", "CMD", DockerfileArbitraries.CmdHashInShellInstruction()),
            new("label-hash-value", "LABEL", DockerfileArbitraries.LabelHashInValueInstruction()),
            new("copy-flag-continuation", "COPY", DockerfileArbitraries.CopyFlagLineContinuationInstruction()),
            new("add-flag-continuation", "ADD", DockerfileArbitraries.AddFlagLineContinuationInstruction()),
        };
}
