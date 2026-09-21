using System.Runtime.CompilerServices;

namespace Valleysoft.DockerfileModel;

/// <summary>Preserves model identity independently of any value-based equality supplied by a type.</summary>
/// <typeparam name="T">The reference type tracked by an ownership set, adapter, or edit plan.</typeparam>
internal sealed class ReferenceComparer<T> : IEqualityComparer<T>
    where T : class
{
    /// <summary>Gets the shared identity comparer for this reference type.</summary>
    public static ReferenceComparer<T> Instance { get; } = new();
    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
