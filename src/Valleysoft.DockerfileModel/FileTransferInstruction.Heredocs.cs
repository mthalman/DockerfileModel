namespace Valleysoft.DockerfileModel;

/// <summary>Provides the shared model for COPY and ADD instructions, including ordinary and heredoc sources.</summary>
public abstract partial class FileTransferInstruction
{
    private EditableList<Heredoc>? editableHeredocs;

    /// <summary>Gets the live collection of paired heredoc sources in marker order.</summary>
    /// <remarks>
    /// Edits insert, replace, move, or remove each opening marker and its corresponding body together,
    /// without including ordinary sources or the destination in this collection.
    /// Removing the final heredoc requires another source to remain. Insertion does not implicitly
    /// convert a JSON-form instruction, and ambiguous opaque source mappings are rejected.
    /// </remarks>
    public EditableList<Heredoc> Heredocs =>
        editableHeredocs ??= new(new HeredocListAdapter(this, EscapeChar));
}
