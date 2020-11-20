using System;
using System.Collections.Generic;
using System.Linq;

namespace DockerfileModel
{
    internal static class EnumerableExtensions
    {
        /// <summary>
        /// Returns the items that precede <paramref name="item"/>.
        /// </summary>
        /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
        /// <param name="enumerable">The source of the elements to filter.</param>
        /// <param name="item">The item upon which the range of searched items is based.</param>
        public static IEnumerable<T> PreviousTo<T>(this IEnumerable<T> enumerable, T item)
            where T : class =>
            enumerable.TakeWhile(cur => cur != item);

        /// <summary>
        /// Returns the items that are after <paramref name="item"/>.
        /// </summary>
        /// <typeparam name="T">The type of the elements of <paramref name="enumerable"/>.</typeparam>
        /// <param name="enumerable">The source of the elements to filter.</param>
        /// <param name="item">The item upon which the range of searched items is based.</param>
        public static IEnumerable<T> After<T>(this IEnumerable<T> enumerable, T item)
            where T : class =>
            enumerable.SkipWhile(cur => cur != item);

        /// <summary>
        /// Returns the first possible item of the specified type that precedes <paramref name="item"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="enumerable"/>.</typeparam>
        /// <typeparam name="TResult">The type to filter the elements of the sequence on.</typeparam>
        /// <param name="enumerable">The source of the elements to filter.</param>
        /// <param name="item">The item upon which the range of searched items is based.</param>
        public static TResult FirstPreviousOfType<TSource, TResult>(this IEnumerable<TSource> enumerable, TSource item)
            where TSource : class =>
            enumerable.PreviousTo(item).OfType<TResult>().Last();

        public static IEnumerable<T> Flatten<T>(this IEnumerable<IEnumerable<T>> itemSets) =>
            itemSets.SelectMany(items => items);

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
