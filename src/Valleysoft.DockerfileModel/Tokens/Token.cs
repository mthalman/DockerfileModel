using System.Threading;

namespace Valleysoft.DockerfileModel.Tokens;

/// <summary>A syntax element whose serialized text can include formatting absent from its semantic value.</summary>
public abstract class Token
{
    /// <summary>Serializes all of the token's current syntax, including quotes, comments, and continuations.</summary>
    public sealed override string ToString()
    {
        return ToString(new TokenStringOptions());
    }

    /// <summary>Serializes the token with selected syntax omitted.</summary>
    /// <param name="options">Filtering options; the token itself is not changed.</param>
    /// <returns>Filtered text, which need not be valid Dockerfile syntax or an exact round trip.</returns>
    public string ToString(TokenStringOptions options)
    {
        Guard.NotNull(options, nameof(options));

        string value = GetUnderlyingValue(options);

        if (!options.ExcludeQuotes && this is IQuotableToken quotableToken)
        {
            value = $"{quotableToken.QuoteChar}{value}{quotableToken.QuoteChar}";
        }

        SerializationObserver.Notify(this, value);
        return value;
    }

    // Consumers can copy the public filters, so observation follows the call scope instead.
    internal static IDisposable ObserveSerialization(Action<Token, string> observer) =>
        new SerializationObserver(observer);

    protected abstract string GetUnderlyingValue(TokenStringOptions options);

    private sealed class SerializationObserver : IDisposable
    {
        private static readonly AsyncLocal<SerializationObserver?> Current = new();
        private readonly SerializationObserver? parent;
        private Action<Token, string>? observer;

        public SerializationObserver(Action<Token, string> observer)
        {
            this.observer = observer;
            parent = Current.Value;
            Current.Value = this;
        }

        public static void Notify(Token token, string value)
        {
            for (SerializationObserver? scope = Current.Value; scope is not null; scope = scope.parent)
            {
                if (Volatile.Read(ref scope.observer) is { } observer)
                {
                    observer(token, value);
                    return;
                }
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref observer, null);
            Current.Value = parent;
        }
    }
}
