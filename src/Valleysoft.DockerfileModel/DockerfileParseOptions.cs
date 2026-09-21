namespace Valleysoft.DockerfileModel;

/// <summary>Opt-in policy for diagnostic parsing and preservation of unsupported input.</summary>
public sealed class DockerfileParseOptions
{
    /// <summary>Gets or sets failure handling; defaults to strict, fail-fast parsing.</summary>
    public DockerfileParseMode Mode { get; set; } = DockerfileParseMode.Strict;
    /// <summary>Gets or sets how unknown instruction names are handled, independently of recovery mode. Defaults to error.</summary>
    public UnknownInstructionBehavior UnknownInstructionBehavior { get; set; } = UnknownInstructionBehavior.Error;
}

/// <summary>Controls whether parsing stops on an error or retains recoverable source regions.</summary>
public enum DockerfileParseMode
{
    /// <summary>Stops on the first error and returns no document.</summary>
    Strict,
    /// <summary>Retains malformed constructs and continues when a safe recovery boundary is available.</summary>
    Recover
}

/// <summary>Controls whether unrecognized instruction names are rejected or retained as opaque syntax.</summary>
public enum UnknownInstructionBehavior
{
    /// <summary>Reports unknown instruction names as errors.</summary>
    Error,
    /// <summary>Preserves unknown instructions without claiming support for their syntax or semantics.</summary>
    Preserve
}
