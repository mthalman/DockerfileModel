using System.Collections.Generic;
using Sprache;

namespace DockerfileModel
{
    public abstract class DockerfileLine : AggregateToken
    {
        protected DockerfileLine(string text, Parser<IEnumerable<Token?>> parser)
            : base(text, parser)
        {
        }

        protected DockerfileLine(string text, Parser<Token> parser)
            : base(text, parser)
        {
        }

        public abstract LineType Type { get; }
    }
}
