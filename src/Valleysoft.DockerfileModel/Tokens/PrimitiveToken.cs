namespace Valleysoft.DockerfileModel.Tokens;

public abstract class PrimitiveToken : Token, IValueToken
{
    private string value;

    public PrimitiveToken(string value)
    {
        Requires.NotNull(value, nameof(value));
        this.value = value;
    }

    public string Value
    {
        get => value;
        set
        {
            Requires.NotNull(value, nameof(value));
            this.value = value;
        }
    }

    protected override string GetUnderlyingValue(TokenStringOptions options) => Value;
}
