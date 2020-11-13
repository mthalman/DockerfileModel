using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    public abstract class Command : AggregateToken
    {
        protected Command(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public abstract CommandType CommandType { get; }
    }
}
