using Valleysoft.DockerfileModel;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.TestSupport;

public static class InstructionSerializer
{
    public static string ParseCSharp(string instructionType, string input, char escapeChar = '\\')
    {
        Token token = instructionType.ToUpperInvariant() switch
        {
            "FROM" => FromInstruction.Parse(input, escapeChar),
            "ARG" => ArgInstruction.Parse(input, escapeChar),
            "RUN" => RunInstruction.Parse(input, escapeChar),
            "CMD" => CmdInstruction.Parse(input, escapeChar),
            "ENTRYPOINT" => EntrypointInstruction.Parse(input, escapeChar),
            "COPY" => CopyInstruction.Parse(input, escapeChar),
            "ADD" => AddInstruction.Parse(input, escapeChar),
            "ENV" => EnvInstruction.Parse(input, escapeChar),
            "EXPOSE" => ExposeInstruction.Parse(input, escapeChar),
            "VOLUME" => VolumeInstruction.Parse(input, escapeChar),
            "USER" => UserInstruction.Parse(input, escapeChar),
            "WORKDIR" => WorkdirInstruction.Parse(input, escapeChar),
            "LABEL" => LabelInstruction.Parse(input, escapeChar),
            "STOPSIGNAL" => StopSignalInstruction.Parse(input, escapeChar),
            "HEALTHCHECK" => HealthCheckInstruction.Parse(input, escapeChar),
            "SHELL" => ShellInstruction.Parse(input, escapeChar),
            "MAINTAINER" => MaintainerInstruction.Parse(input, escapeChar),
            "ONBUILD" => OnBuildInstruction.Parse(input, escapeChar),
            _ => throw new ArgumentException($"Unsupported instruction type: {instructionType}")
        };

        return TokenJsonSerializer.Serialize(token);
    }
}
