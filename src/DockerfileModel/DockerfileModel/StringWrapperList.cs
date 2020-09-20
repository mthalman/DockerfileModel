using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DockerfileModel
{
    internal class StringWrapperList<T> : IList<string>
    {
        private readonly IEnumerable<T> wrappedItems;
        private readonly Func<T, string> getString;
        private readonly Action<T, string> setString;

        public StringWrapperList(
            IEnumerable<T> wrappedItems,
            Func<T, string> getString,
            Action<T, string> setString)
        {
            this.wrappedItems = wrappedItems;
            this.getString = getString;
            this.setString = setString;
        }

        public string this[int index]
        {
            get => getString(wrappedItems.ElementAt(index));
            set => setString(wrappedItems.ElementAt(index), value);
        }

        public int Count => wrappedItems.Count();

        public bool IsReadOnly => false;

        public void Add(string item)
        {
            ThrowAddRemoveNotSupported();
        }

        public void Clear()
        {
            ThrowAddRemoveNotSupported();
        }

        public bool Contains(string item) => GetStrings().Contains(item);

        public void CopyTo(string[] array, int arrayIndex)
        {
            GetStrings()
                .ToList()
                .CopyTo(array, arrayIndex);
        }

        public IEnumerator<string> GetEnumerator() => GetStrings().GetEnumerator();

        public int IndexOf(string item) => GetStrings().ToList().IndexOf(item);

        public void Insert(int index, string item)
        {
            ThrowAddRemoveNotSupported();
        }

        public bool Remove(string item)
        {
            ThrowAddRemoveNotSupported();
            return false;
        }

        public void RemoveAt(int index)
        {
            ThrowAddRemoveNotSupported();
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        private IEnumerable<string> GetStrings() =>
            wrappedItems
                .Select(wrappedItem => getString(wrappedItem));

        private void ThrowAddRemoveNotSupported()
        {
            throw new NotSupportedException("Items may not be added or removed from the list.");
        }
    }
}
