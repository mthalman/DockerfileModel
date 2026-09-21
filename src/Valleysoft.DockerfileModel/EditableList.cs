using System.Collections;

namespace Valleysoft.DockerfileModel;

/// <summary>
/// Provides a live, syntax-aware view of a model's collection elements.
/// </summary>
/// <typeparam name="T">The semantic value, token, or model type exposed by the collection.</typeparam>
/// <remarks>
/// Obtain instances from model properties. Writes validate the proposed structural edit before
/// changing the owner; required operands, incompatible parsing contexts, repeated tokens within
/// the edited tree, or unpreservable trivia can prevent an edit despite <see cref="IsReadOnly"/> being false.
/// Standard list writes use <see cref="TriviaDisposition.Preserve"/>.
/// Semantic views compare values, while token and model-object views compare identity.
/// Typed insertions adopt supplied objects; semantic insertions encode values in the owner's grammar.
/// Preserved trivia can remain shared with removed or replaced objects; owner-local ownership
/// checks do not guarantee those outgoing objects can be mutated or reused independently.
/// Unsupported effective serialization overrides and custom <see cref="Tokens.IQuotableToken"/>
/// implementations are rejected; subclasses inheriting supported built-in implementations remain eligible.
/// Write admission precedes snapshot-based index and membership checks, without imposing that
/// eligibility requirement on ordinary reads.
/// Validation neither repairs nor certifies preexisting inconsistencies between live token roles
/// and serialized interpretation introduced by scalar setters or low-level token mutation.
/// </remarks>
public class EditableList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly IEditableListAdapter<T> adapter;

    /// <summary>Binds operations to an owner's adapter rather than allocating a separate element store.</summary>
    /// <param name="adapter">The adapter supplying live reads, comparison rules, and atomic edits.</param>
    internal EditableList(IEditableListAdapter<T> adapter)
    {
        this.adapter = adapter;
    }

    /// <summary>Forwards owner eligibility checks without evaluating a snapshot or projected value.</summary>
    /// <remarks>Projection adapters use this path before their own facade performs write bookkeeping.</remarks>
    internal void ValidateWrite() => adapter.ValidateWrite();

    /// <summary>
    /// Gets an element or replaces it while preserving incidental trivia.
    /// </summary>
    /// <param name="index">The zero-based index of an existing element.</param>
    /// <value>The element at the selected index.</value>
    /// <remarks>Use <see cref="Replace"/> to specify a different trivia policy.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The index does not identify an existing element.</exception>
    public T this[int index]
    {
        get => adapter.Snapshot()[index];
        set => Replace(index, value);
    }

    /// <summary>Gets the current number of elements in the owner's collection.</summary>
    public int Count => adapter.Snapshot().Count;

    /// <summary>Gets whether the collection prohibits all writes.</summary>
    /// <value>Always <see langword="false"/>; individual edits remain subject to syntax validation.</value>
    public bool IsReadOnly => false;

    /// <summary>Appends an element and inserts any required syntax separators.</summary>
    /// <param name="item">The element to add.</param>
    /// <exception cref="InvalidOperationException">The element cannot be added without violating the owner's editing rules.</exception>
    public void Add(T item)
    {
        ValidateWrite();
        Insert(Count, item);
    }

    /// <summary>Inserts an element while preserving existing trivia at the insertion boundary.</summary>
    /// <param name="index">The zero-based insertion index, from zero through <see cref="Count"/>.</param>
    /// <param name="item">The element to insert.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the insertion range.</exception>
    /// <exception cref="InvalidOperationException">The element cannot be inserted without violating the owner's editing rules.</exception>
    public void Insert(int index, T item)
    {
        ValidateWrite();
        ValidateIndex(index, allowEnd: true);
        adapter.Insert(index, item);
    }

    /// <summary>Replaces an element using the specified policy for its incidental trivia.</summary>
    /// <param name="index">The zero-based index of an existing element.</param>
    /// <param name="item">The replacement element.</param>
    /// <param name="trivia">How to handle incidental trivia in the replaced region.</param>
    /// <remarks>
    /// Document-item replacement can promote embedded comments after the replacement, increasing
    /// the collection count. The replacement remains at <paramref name="index"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The index or trivia policy is invalid.</exception>
    /// <exception cref="InvalidOperationException">The replacement cannot satisfy the owner's syntax, ownership, or trivia rules.</exception>
    public void Replace(int index, T item, TriviaDisposition trivia = TriviaDisposition.Preserve)
    {
        ValidateWrite();
        ValidateIndex(index);
        EditValidation.ValidateTrivia(trivia);
        adapter.Replace(index, item, trivia);
    }

    /// <summary>Replaces a uniquely matching element without requiring its index.</summary>
    /// <param name="item">The existing element to match using this collection's comparison rules.</param>
    /// <param name="replacement">The replacement element.</param>
    /// <param name="trivia">How to handle incidental trivia in the replaced region.</param>
    /// <exception cref="ArgumentException">No element matches <paramref name="item"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The trivia policy is invalid.</exception>
    /// <exception cref="InvalidOperationException">The match is ambiguous or the replacement cannot be applied safely.</exception>
    public void ReplaceItem(T item, T replacement, TriviaDisposition trivia = TriviaDisposition.Preserve)
    {
        ValidateWrite();
        EditValidation.ValidateTrivia(trivia);
        Replace(UniqueIndex(item, nameof(item)), replacement, trivia);
    }

    /// <summary>Removes the first matching element using the specified trivia policy.</summary>
    /// <param name="item">The element to match using this collection's comparison rules.</param>
    /// <param name="trivia">How to handle incidental trivia in the removed region; defaults to preservation.</param>
    /// <returns><see langword="true"/> if an element was removed; <see langword="false"/> if no match exists.</returns>
    /// <remarks>Unlike <see cref="ReplaceItem"/>, duplicate matches are allowed; only the first is removed.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The trivia policy is invalid.</exception>
    /// <exception cref="InvalidOperationException">The removal cannot satisfy the owner's editing rules.</exception>
    public bool Remove(T item, TriviaDisposition trivia = TriviaDisposition.Preserve)
    {
        ValidateWrite();
        EditValidation.ValidateTrivia(trivia);
        int index = IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        adapter.Remove(new[] { index }, trivia);
        return true;
    }

    /// <summary>Removes the element at an index using the specified trivia policy.</summary>
    /// <param name="index">The zero-based index of the element to remove.</param>
    /// <param name="trivia">How to handle incidental trivia in the removed region; defaults to preservation.</param>
    /// <remarks>
    /// Removing a document instruction can promote embedded comments or retained formatting to
    /// separate items. Removal therefore need not decrease <see cref="Count"/> by exactly one.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The index or trivia policy is invalid.</exception>
    /// <exception cref="InvalidOperationException">The removal cannot satisfy the owner's editing rules.</exception>
    public void RemoveAt(int index, TriviaDisposition trivia = TriviaDisposition.Preserve)
    {
        ValidateWrite();
        ValidateIndex(index);
        EditValidation.ValidateTrivia(trivia);
        adapter.Remove(new[] { index }, trivia);
    }

    /// <summary>Removes all elements as one operation using the specified trivia policy.</summary>
    /// <param name="trivia">How to handle incidental trivia owned by the selected elements; defaults to preservation.</param>
    /// <remarks>
    /// A successful clear leaves <see cref="Count"/> equal to zero. Preserving document clears
    /// reject embedded trivia or a byte-order mark that would require leftover items.
    /// Discarding trivia does not permit an instruction to lose required operands.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The trivia policy is invalid.</exception>
    /// <exception cref="InvalidOperationException">The collection cannot become empty under the selected policy.</exception>
    public void Clear(TriviaDisposition trivia = TriviaDisposition.Preserve)
    {
        ValidateWrite();
        EditValidation.ValidateTrivia(trivia);
        int count = Count;
        if (count != 0)
        {
            if (adapter is IEditableListClearAdapter clearAdapter)
            {
                clearAdapter.Clear(trivia);
            }
            else
            {
                adapter.Remove(Enumerable.Range(0, count).ToArray(), trivia);
            }
        }
    }

    /// <summary>Moves an existing element, preserving its identity and owned trivia.</summary>
    /// <param name="oldIndex">The element's current zero-based index.</param>
    /// <param name="newIndex">Its zero-based index in the final collection, not an insertion index before removal.</param>
    /// <remarks>Both indices must be less than <see cref="Count"/>. Equal valid indices are a no-op.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">Either index does not identify an existing collection position.</exception>
    /// <exception cref="InvalidOperationException">The move cannot preserve the owner's valid syntax.</exception>
    public void Move(int oldIndex, int newIndex)
    {
        ValidateWrite();
        ValidateIndex(oldIndex, parameterName: nameof(oldIndex));
        ValidateIndex(newIndex, parameterName: nameof(newIndex));
        if (oldIndex != newIndex)
        {
            adapter.Move(oldIndex, newIndex);
        }
    }

    /// <summary>Determines whether an element matches using this collection's comparison rules.</summary>
    /// <param name="item">The element to find.</param>
    /// <returns><see langword="true"/> if a match exists; otherwise, <see langword="false"/>.</returns>
    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <summary>Finds the first match using this collection's comparison rules.</summary>
    /// <param name="item">The element to find.</param>
    /// <returns>The zero-based index of the first match, or -1 if no match exists.</returns>
    public int IndexOf(T item)
    {
        IReadOnlyList<T> items = adapter.Snapshot();
        for (int i = 0; i < items.Count; i++)
        {
            if (adapter.Comparer.Equals(items[i], item))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Copies a snapshot of the current elements into an array without cloning the elements.</summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based starting index in the destination array.</param>
    /// <exception cref="ArgumentNullException">The destination array is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The destination index is negative.</exception>
    /// <exception cref="ArgumentException">The destination array does not have enough space.</exception>
    public void CopyTo(T[] array, int arrayIndex) => adapter.Snapshot().ToArray().CopyTo(array, arrayIndex);

    /// <summary>Returns an enumerator over the elements present at the time of this call.</summary>
    /// <returns>An enumerator whose sequence is unaffected by subsequent collection edits.</returns>
    /// <remarks>Elements are not cloned; referenced model objects may still be modified.</remarks>
    public IEnumerator<T> GetEnumerator() => adapter.Snapshot().GetEnumerator();

    bool ICollection<T>.Remove(T item) => Remove(item);

    void IList<T>.RemoveAt(int index) => RemoveAt(index);

    void ICollection<T>.Clear() => Clear();

    /// <inheritdoc cref="GetEnumerator"/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int UniqueIndex(T item, string parameterName)
    {
        IReadOnlyList<T> items = adapter.Snapshot();
        int result = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (!adapter.Comparer.Equals(items[i], item))
            {
                continue;
            }
            if (result >= 0)
            {
                throw new InvalidOperationException("The selected value is ambiguous. Select an index instead.");
            }
            result = i;
        }
        if (result < 0)
        {
            throw new ArgumentException("The selected item does not belong to this collection.", parameterName);
        }
        return result;
    }

    private void ValidateIndex(int index, bool allowEnd = false, string parameterName = "index")
    {
        int count = Count;
        if (index < 0 || index > count || (!allowEnd && index == count))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
