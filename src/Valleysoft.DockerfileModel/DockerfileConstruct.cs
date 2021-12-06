using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public abstract class DockerfileConstruct : AggregateToken
{
    protected DockerfileConstruct(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public abstract ConstructType Type { get; }
}
