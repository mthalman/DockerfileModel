using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Valleysoft.DockerfileModel
{
    internal class ProjectedItemList<TSource, TProjection> : IList<TProjection>
    {
        private readonly IEnumerable<TSource> wrappedItems;
        private readonly Func<TSource, TProjection> getValue;
        private readonly Action<TSource, TProjection> setValue;

        public ProjectedItemList(
            IEnumerable<TSource> wrappedItems,
            Func<TSource, TProjection> getValue,
            Action<TSource, TProjection> setValue)
        {
            this.wrappedItems = wrappedItems;
            this.getValue = getValue;
            this.setValue = setValue;
        }

        public TProjection this[int index]
        {
            get => getValue(wrappedItems.ElementAt(index));
            set => setValue(wrappedItems.ElementAt(index), value);
        }

        public int Count => wrappedItems.Count();

        public bool IsReadOnly => false;

        public void Add(TProjection item)
        {
            ThrowAddRemoveNotSupported();
        }

        public void Clear()
        {
            ThrowAddRemoveNotSupported();
        }

        public bool Contains(TProjection item) => GetItems().Contains(item);

        public void CopyTo(TProjection[] array, int arrayIndex)
        {
            GetItems()
                .ToList()
                .CopyTo(array, arrayIndex);
        }

        public IEnumerator<TProjection> GetEnumerator() => GetItems().GetEnumerator();

        public int IndexOf(TProjection item) => GetItems().ToList().IndexOf(item);

        public void Insert(int index, TProjection item)
        {
            ThrowAddRemoveNotSupported();
        }

        public bool Remove(TProjection item)
        {
            ThrowAddRemoveNotSupported();
            return false;
        }

        public void RemoveAt(int index)
        {
            ThrowAddRemoveNotSupported();
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        private IEnumerable<TProjection> GetItems() =>
            wrappedItems
                .Select(wrappedItem => getValue(wrappedItem));

        private void ThrowAddRemoveNotSupported()
        {
            throw new NotSupportedException("Items may not be added or removed from the list.");
        }
    }
}
