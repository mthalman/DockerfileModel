namespace Valleysoft.DockerfileModel;

internal class ProjectedItemList<TSource, TProjection> : EditableList<TProjection>
{
    public ProjectedItemList(EditableList<TSource> source, Func<TSource, TProjection> getValue,
        Func<TProjection, TSource> create, IEqualityComparer<TProjection>? comparer = null,
        Action<int, TProjection, TriviaDisposition>? replace = null)
        : base(new ProjectionAdapter<TSource, TProjection>(source, getValue, create, comparer, replace))
    {
    }
}

internal sealed class ProjectionAdapter<TSource, TProjection> : IEditableListAdapter<TProjection>
{
    private readonly EditableList<TSource> source;
    private readonly Func<TSource, TProjection> getValue;
    private readonly Func<TProjection, TSource> create;
    private readonly Action<int, TProjection, TriviaDisposition>? replace;

    internal ProjectionAdapter(EditableList<TSource> source, Func<TSource, TProjection> getValue,
        Func<TProjection, TSource> create, IEqualityComparer<TProjection>? comparer,
        Action<int, TProjection, TriviaDisposition>? replace)
    {
        this.source = source;
        this.getValue = getValue;
        this.create = create;
        this.replace = replace;
        Comparer = comparer ?? EqualityComparer<TProjection>.Default;
    }

    public void ValidateWrite() => source.ValidateWrite();
    public IReadOnlyList<TProjection> Snapshot() => source.Select(getValue).ToArray();
    public IEqualityComparer<TProjection> Comparer { get; }
    public void Insert(int index, TProjection item) => source.Insert(index, create(item));

    public void Replace(int index, TProjection item, TriviaDisposition trivia)
    {
        if (replace is not null)
        {
            replace(index, item, trivia);
        }
        else
        {
            TSource current = source[index];
            source.Replace(index, ReferenceEquals(getValue(current), item) ? current : create(item), trivia);
        }
    }

    public void Remove(IReadOnlyList<int> indices, TriviaDisposition trivia)
    {
        if (indices.Count == source.Count)
        {
            source.Clear(trivia);
        }
        else if (indices.Count == 1)
        {
            source.RemoveAt(indices[0], trivia);
        }
        else
        {
            throw new InvalidOperationException("Only one item or the complete collection may be removed.");
        }
    }

    public void Move(int oldIndex, int newIndex) => source.Move(oldIndex, newIndex);
}
