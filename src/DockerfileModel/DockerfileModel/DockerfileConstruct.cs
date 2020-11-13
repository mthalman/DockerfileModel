using System.Collections.Generic;
using DockerfileModel.Tokens;

namespace DockerfileModel
{
    public abstract class DockerfileConstruct : AggregateToken
    {
        protected DockerfileConstruct(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public abstract ConstructType Type { get; }
    }
}
