namespace Valleysoft.DockerfileModel;

internal static class ParserExtensions
{
    public static TextParser<IEnumerable<T>> AsEnumerable<T>(this TextParser<T> parser) =>
        from item in parser
        select (IEnumerable<T>)new T[] { item };

    public static TextParser<IEnumerable<T>> FilterNulls<T>(this TextParser<IEnumerable<T?>> parser) =>
        from item in parser
        where item is not null
        select item;

    public static TextParser<IEnumerable<T>> Flatten<T>(this TextParser<IEnumerable<IEnumerable<T>>> parser) =>
        from itemSets in parser
        select itemSets.Flatten();

    public static TextParser<IEnumerable<T>> Flatten<T>(this TextParser<IEnumerable<T>[]> parser) =>
        from itemSets in parser
        select itemSets.Flatten();

    public static TextParser<IEnumerable<T>> Flatten<T>(this TextParser<T[][]> parser) =>
        from itemSets in parser
        select itemSets.Cast<IEnumerable<T>>().Flatten();

    public static TextParser<T> Single<T>(this TextParser<IEnumerable<T>> parser) =>
        from items in parser
        select items.Single();

    public static TextParser<string> ConvertToString<T>(this TextParser<T> parser) =>
        from item in parser
        select item.ToString();

    public static TextParser<T> Where<T>(this TextParser<T> parser, Func<T, bool> predicate) =>
        Combinators.Where(parser, predicate, "matching value");
}
