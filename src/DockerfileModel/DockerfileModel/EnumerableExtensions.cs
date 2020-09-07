using System;
using System.Collections.Generic;
using System.Linq;

namespace DockerfileModel
{
    public static class EnumerableExtensions
    {
        public static IEnumerable<T> Append<T>(this IEnumerable<T> enumerable, T item)
        {
            foreach (var existingItem in enumerable)
            {
                yield return existingItem;
            }

            yield return item;
        }

        public static IEnumerable<T> WhereHistory<T>(this IEnumerable<T> enumerable, Func<IEnumerable<T>, T, bool> predicate)
        {
            if (!enumerable.Any())
            {
                yield break;
            }

            List<T> previousItems = new List<T>();

            foreach (T item in enumerable)
            {
                if (predicate(previousItems, item))
                {
                    yield return item;
                }

                previousItems.Add(item);
            }
        }
    }
}
