using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public abstract class Mount : AggregateToken
{
    protected Mount(IEnumerable<Token> tokens) : base(tokens)
    {
    }

    public string Type
    {
        get => TypeToken.Value;
        set
        {
            Requires.NotNullOrEmpty(value, nameof(value));
            TypeToken.ValueToken.Value = value;
        }
    }

    public KeyValueToken<KeywordToken, LiteralToken> TypeToken
    {
        get => Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>().First();
        set
        {
            Requires.NotNull(value, nameof(value));
            SetToken(TypeToken, value);
        }
    }
}
