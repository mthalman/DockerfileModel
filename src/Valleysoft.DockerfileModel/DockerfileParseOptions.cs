namespace Valleysoft.DockerfileModel;

public sealed class DockerfileParseOptions
{
    public DockerfileParseMode Mode { get; set; } = DockerfileParseMode.Strict;
    public UnknownInstructionBehavior UnknownInstructionBehavior { get; set; } = UnknownInstructionBehavior.Error;
}

public enum DockerfileParseMode
{
    Strict,
    Recover
}

public enum UnknownInstructionBehavior
{
    Error,
    Preserve
}
