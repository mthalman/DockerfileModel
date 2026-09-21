namespace Valleysoft.DockerfileModel;

/// <summary>Specifies how a collection edit handles incidental formatting and comments in its selected region.</summary>
/// <remarks>
/// The policy does not apply to unrelated syntax or to explicitly selected payload, such as a
/// standalone comment or a heredoc body. Neither policy permits an invalid resulting instruction.
/// </remarks>
public enum TriviaDisposition
{
    /// <summary>
    /// Retains incidental trivia at valid syntax boundaries, or rejects the edit without mutation
    /// when that trivia cannot be preserved safely.
    /// </summary>
    Preserve = 0,

    /// <summary>
    /// Permits removal of incidental trivia belonging to the selected region while leaving
    /// unrelated syntax intact.
    /// </summary>
    Discard = 1
}
