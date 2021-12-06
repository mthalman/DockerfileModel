using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public abstract class Command : AggregateToken
{
    protected Command(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public abstract CommandType CommandType { get; }
}
