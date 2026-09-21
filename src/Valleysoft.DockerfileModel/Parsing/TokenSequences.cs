using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class TokenSequences
{
    /// <summary>
    /// Filters out null items from an enumerable.
    /// </summary>
    /// <typeparam name="T">Type of items contained in the enumerable.</typeparam>
    /// <param name="items">The enumerable to filter nulls from.</param>
    /// <returns>An enumerable with no null items.</returns>
    internal static IEnumerable<T> FilterNulls<T>(IEnumerable<T?>? items) where T : class
    {
        if (items is null)
        {
            yield break;
        }

        foreach (T? item in items.Where(item => item != null))
        {
            yield return item!;
        }
    }

    /// <summary>
    /// Concatenates sets of tokens into a single set, removing any nulls.
    /// </summary>
    /// <param name="tokens">Sets of tokens.</param>
    /// <returns>Concatenation of all tokens.</returns>
    internal static IEnumerable<Token> ConcatTokens(params Token?[]? tokens) =>
        FilterNulls(tokens).ToList();

    /// <summary>
    /// Concatenates sets of tokens into a single set, removing any nulls.
    /// </summary>
    /// <param name="tokens">Sets of tokens.</param>
    /// <returns>Concatenation of all tokens.</returns>
    internal static IEnumerable<Token> ConcatTokens(params IEnumerable<Token?>[] tokens) =>
        ConcatTokens(
            FilterNulls(tokens)
                .SelectMany(tokens => tokens)
                .ToArray());
}
