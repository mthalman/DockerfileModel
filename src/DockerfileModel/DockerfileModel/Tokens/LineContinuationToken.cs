using System.Collections.Generic;

namespace DockerfileModel.Tokens
{
    public class LineContinuationToken : AggregateToken
    {
        public LineContinuationToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }
    }
}
