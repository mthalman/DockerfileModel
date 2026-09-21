namespace Valleysoft.DockerfileModel;

/// <summary>Connects an editable collection to its owner's syntax and validation rules.</summary>
/// <typeparam name="T">The token, model object, or semantic value exposed by the collection.</typeparam>
/// <remarks>
/// The collection facade requests write admission before reading projected values or matching
/// elements, then validates indices and trivia policies. Adapters validate ownership,
/// escape context, cardinality, and grammar before publishing an edit; a rejected edit must not
/// leave a partially changed token tree. Implementations need not reconstruct unaffected elements.
/// </remarks>
internal interface IEditableListAdapter<T>
{
    /// <summary>Rejects ineligible owner implementations before a write evaluates snapshots, values, or equality.</summary>
    /// <remarks>
    /// Inspect owner metadata and token references without serializing values. Projections forward
    /// this call to their backing owner without invoking their value selector. Ordinary reads do
    /// not request write admission; edit-specific validation still follows this preflight.
    /// </remarks>
    void ValidateWrite();

    /// <summary>Captures the current elements in collection order without cloning their model objects.</summary>
    /// <returns>A snapshot of membership; subsequent calls reflect edits to the owner.</returns>
    IReadOnlyList<T> Snapshot();
    /// <summary>Defines membership and anchor equality for this view.</summary>
    /// <remarks>Typed views use object identity; semantic projections use their declared value equality.</remarks>
    IEqualityComparer<T> Comparer { get; }
    /// <summary>Inserts an element while repairing only the syntax needed at its new boundary.</summary>
    /// <param name="index">The insertion position in the current snapshot, including its end.</param>
    /// <param name="item">The element to adopt or encode according to the view's ownership contract.</param>
    void Insert(int index, T item);
    /// <summary>Replaces one selected payload using the specified trivia policy.</summary>
    /// <param name="index">The selected element's position in the current snapshot.</param>
    /// <param name="item">The replacement element or semantic value.</param>
    /// <param name="trivia">Whether incidental trivia must survive the replacement.</param>
    void Replace(int index, T item, TriviaDisposition trivia);
    /// <summary>Removes a batch against one pre-edit snapshot, validating the final state before mutation.</summary>
    /// <param name="indices">Distinct valid positions in the same current snapshot, not successively shifted indices.</param>
    /// <param name="trivia">Whether incidental trivia must survive removal of the selected payloads.</param>
    /// <remarks>
    /// The facade uses this operation for a single removal and, unless overridden by
    /// <see cref="IEditableListClearAdapter"/>, a nonempty collection's clear. Validate minimum
    /// cardinality for the entire batch rather than committing a sequence of single removals.
    /// </remarks>
    void Remove(IReadOnlyList<int> indices, TriviaDisposition trivia);
    /// <summary>Reorders an existing element without replacing its identity.</summary>
    /// <param name="oldIndex">The element's position before the move.</param>
    /// <param name="newIndex">Its position in the final collection, after removal and reinsertion.</param>
    void Move(int oldIndex, int newIndex);
}

/// <summary>Provides a clear operation when removing all selected payloads might retain collection elements.</summary>
/// <remarks>
/// Successful clear must leave the collection empty, unlike a preserving removal that may
/// retain standalone trivia. The facade validates the policy but does not dispatch to this
/// adapter when the collection is already empty.
/// </remarks>
internal interface IEditableListClearAdapter
{
    /// <summary>Clears the collection as one edit, or rejects a policy that cannot leave it empty.</summary>
    /// <param name="trivia">Whether embedded trivia must be retained rather than discarded.</param>
    void Clear(TriviaDisposition trivia);
}
