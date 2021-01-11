using System.Collections.Generic;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;

namespace Valleysoft.DockerfileModel
{
    public abstract class Command : AggregateToken
    {
        protected Command(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public abstract CommandType CommandType { get; }
    }
}
