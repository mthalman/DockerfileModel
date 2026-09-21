using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public abstract class Command : AggregateToken
{
    protected Command(IEnumerable<Token> tokens) : this(tokens, Dockerfile.DefaultEscapeChar)
    {
    }

    protected Command(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
    {
        EditingEscapeChar = escapeChar;
    }

    public abstract CommandType CommandType { get; }
}
