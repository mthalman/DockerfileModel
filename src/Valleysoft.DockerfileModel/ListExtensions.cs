using System;
using System.Collections.Generic;

namespace Valleysoft.DockerfileModel
{
    internal static class ListExtensions
    {
        /// <summary>
        /// Removes the set of items contained between, and including, the two specified items.
        /// </summary>
        public static void RemoveRange<T>(this List<T> list, T item1, T item2)
        {
            int index1 = list.IndexOf(item1);
            int index2 = list.IndexOf(item2);
            int lowerIndex = Math.Min(index1, index2);
            int upperIndex = Math.Max(index1, index2);

            list.RemoveRange(lowerIndex, upperIndex - lowerIndex + 1);
        }
    }
}
