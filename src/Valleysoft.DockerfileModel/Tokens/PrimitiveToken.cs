using System.Threading;

namespace Valleysoft.DockerfileModel.Tokens;

public abstract class PrimitiveToken : Token, IValueToken
{
    private static long valueVersion;
    private string value;

    internal static long ValueVersion => Volatile.Read(ref valueVersion);

    public PrimitiveToken(string value)
    {
        Guard.NotNull(value, nameof(value));
        this.value = value;
    }

    public string Value
    {
        get => value;
        set
        {
            Guard.NotNull(value, nameof(value));
            this.value = value;
            Interlocked.Increment(ref valueVersion);
        }
    }

    protected override string GetUnderlyingValue(TokenStringOptions options) => Value;
}
