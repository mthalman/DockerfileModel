using System.Threading;

namespace Valleysoft.DockerfileModel.Tokens;

public abstract class PrimitiveToken : Token, IValueToken
{
    private string value;
    private ValueMutationScope[]? valueObservers;

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
            ResolutionGuard.BeforeMutation(this);
            bool changed = this.value != value;
            this.value = value;
            if (changed && Volatile.Read(ref valueObservers) is { } observers)
            {
                foreach (ValueMutationScope observer in observers) observer.RecordMutation();
            }
        }
    }

    protected override string GetUnderlyingValue(TokenStringOptions options) => Value;

    private void Subscribe(ValueMutationScope scope)
    {
        ValueMutationScope[]? previous;
        ValueMutationScope[] updated;
        do
        {
            previous = Volatile.Read(ref valueObservers);
            int count = previous?.Length ?? 0;
            updated = new ValueMutationScope[count + 1];
            if (previous is not null) Array.Copy(previous, updated, count);
            updated[count] = scope;
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref valueObservers, updated, previous), previous));
    }

    private void Unsubscribe(ValueMutationScope scope)
    {
        while (Volatile.Read(ref valueObservers) is { } previous)
        {
            int index = Array.IndexOf(previous, scope);
            if (index < 0) return;
            ValueMutationScope[]? updated = null;
            if (previous.Length > 1)
            {
                updated = new ValueMutationScope[previous.Length - 1];
                Array.Copy(previous, 0, updated, 0, index);
                Array.Copy(previous, index + 1, updated, index, previous.Length - index - 1);
            }
            if (ReferenceEquals(Interlocked.CompareExchange(ref valueObservers, updated, previous), previous)) return;
        }
    }

    // Token subscriptions also observe worker writes that do not inherit an execution context.
    internal sealed class ValueMutationScope : IDisposable
    {
        private readonly object sync = new();
        private Dictionary<PrimitiveToken, int>? tokens;
        private bool active = true;
        private long version;

        public long Version => Volatile.Read(ref version);

        public void Track(PrimitiveToken token)
        {
            lock (sync)
            {
                if (!active) return;
                tokens ??= new(ReferenceComparer<PrimitiveToken>.Instance);
                tokens.TryGetValue(token, out int count);
                tokens[token] = count + 1;
                if (count == 0) token.Subscribe(this);
            }
        }

        public void Untrack(PrimitiveToken token)
        {
            lock (sync)
            {
                if (tokens is null || !tokens.TryGetValue(token, out int count)) return;
                if (count > 1)
                {
                    tokens[token] = count - 1;
                }
                else
                {
                    tokens.Remove(token);
                    token.Unsubscribe(this);
                }
            }
        }

        public void RecordMutation()
        {
            if (Volatile.Read(ref active)) Interlocked.Increment(ref version);
        }

        public void Dispose()
        {
            lock (sync)
            {
                Volatile.Write(ref active, false);
                if (tokens is not null)
                {
                    foreach (PrimitiveToken token in tokens.Keys) token.Unsubscribe(this);
                }
                tokens = null;
            }
        }
    }
}
