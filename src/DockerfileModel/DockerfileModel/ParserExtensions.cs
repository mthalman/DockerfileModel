using System.Collections.Generic;
using Sprache;

namespace DockerfileModel
{
    public static class ParserExtensions
    {
        public static Parser<IEnumerable<T>> AsEnumerable<T>(this Parser<T> parser) =>
            from item in parser
            select new T[] { item };
    }
}
