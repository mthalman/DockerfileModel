namespace Valleysoft.DockerfileModel;

/// <summary>Represents a RUN instruction with a command or paired heredoc definitions.</summary>
public partial class RunInstruction
{
    private EditableList<Heredoc>? editableHeredocs;

    /// <summary>Gets the live collection of heredoc definitions in opening-marker order.</summary>
    /// <remarks>
    /// Each edit keeps a marker and its body paired, and surviving pairs retain their object identity.
    /// Adding a heredoc requires shell form. Removing the last definition restores a shell command
    /// only when command text remains; a marker-only RUN cannot be left empty.
    /// Body content is payload, not incidental trivia discarded or retained by a formatting policy.
    /// </remarks>
    public EditableList<Heredoc> Heredocs =>
        editableHeredocs ??= new(new HeredocListAdapter(this, escapeChar));
}
