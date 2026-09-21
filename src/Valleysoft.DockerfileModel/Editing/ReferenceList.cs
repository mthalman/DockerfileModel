namespace Valleysoft.DockerfileModel;

/// <summary>Selects exact model objects without invoking consumer-defined value equality.</summary>
/// <remarks>Semantic projections retain their declared comparers; these helpers are for owned token and model identities.</remarks>
internal static class ReferenceList
{
    /// <summary>Finds the exact object rather than an equal-valued neighbor.</summary>
    /// <typeparam name="T">The reference type stored by the list.</typeparam>
    /// <param name="items">The current ordered token or model references.</param>
    /// <param name="item">The exact reference to locate.</param>
    /// <returns>The matching index, or -1 if that reference is absent.</returns>
    internal static int IndexOf<T>(IReadOnlyList<T> items, T item) where T : class
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], item))
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>Checks ownership membership without allowing an equal-valued foreign object to match.</summary>
    /// <typeparam name="T">The reference type being traversed.</typeparam>
    /// <param name="items">The existing model objects.</param>
    /// <param name="item">The exact object whose membership is required.</param>
    /// <returns>True only when the same reference occurs in the sequence.</returns>
    internal static bool Contains<T>(IEnumerable<T> items, T item) where T : class =>
        items.Any(candidate => ReferenceEquals(candidate, item));

    /// <summary>Removes the exact selected reference without invoking the list's default equality comparer.</summary>
    /// <typeparam name="T">The reference type stored by the list.</typeparam>
    /// <param name="items">The mutable list containing the selected object.</param>
    /// <param name="item">The exact reference to remove.</param>
    /// <returns>True if the reference was present and removed; otherwise false.</returns>
    internal static bool Remove<T>(List<T> items, T item) where T : class
    {
        int index = IndexOf(items, item);
        if (index < 0)
        {
            return false;
        }
        items.RemoveAt(index);
        return true;
    }
}
