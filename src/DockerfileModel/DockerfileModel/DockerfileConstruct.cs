using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    public abstract class DockerfileConstruct : AggregateToken
    {
        protected DockerfileConstruct(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        protected DockerfileConstruct(string text, Parser<IEnumerable<Token?>> parser)
            : base(text, parser)
        {
        }

        public abstract ConstructType Type { get; }
    }
}
