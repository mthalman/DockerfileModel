using System.Collections.Generic;

namespace DockerfileModel.Tokens
{
    public class LineContinuationToken : AggregateToken
    {
        internal LineContinuationToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }
    }
}
