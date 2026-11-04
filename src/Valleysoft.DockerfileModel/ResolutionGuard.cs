using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

internal static class ResolutionGuard
{
    internal const string StructuralMutationMessage =
        "Source tokens cannot be changed by a callback during variable resolution.";

    [ThreadStatic]
    private static State? current;

    internal static Scope Enter(AggregateToken source)
    {
        State state = GetState(out bool outermost);
        int roots = state.Roots.Count;
        int documents = state.Documents.Count;
        state.Roots.Add(source);
        return new Scope(state, roots, documents, outermost);
    }

    internal static Scope Enter(Dockerfile source)
    {
        State state = GetState(out bool outermost);
        int roots = state.Roots.Count;
        int documents = state.Documents.Count;
        state.Documents.Add(source);
        state.Roots.AddRange(source.RawItems);
        return new Scope(state, roots, documents, outermost);
    }

    private static State GetState(out bool outermost)
    {
        outermost = current is null;
        State state = current ??= new State();
        if (state.CallbackDepth > 0)
        {
            state.Verify();
        }
        return state;
    }

    internal static CallbackScope Callback()
    {
        if (current is not State state)
        {
            return default;
        }
        state.Verify();
        state.Capture();
        state.CaptureQuotes();
        state.CallbackDepth++;
        return new CallbackScope(state);
    }

    internal static void VerifyUnchanged() => current?.Verify();

    internal static void BeforeMutation(Token owner)
    {
        if (current is State state && state.CallbackDepth > 0 && state.IsProtected(owner))
        {
            throw new InvalidOperationException(StructuralMutationMessage);
        }
    }

    internal static void BeforeMutation(Dockerfile owner)
    {
        if (current is State state && state.CallbackDepth > 0 &&
            state.Documents.Any(document => ReferenceEquals(document, owner)))
        {
            throw new InvalidOperationException(StructuralMutationMessage);
        }
    }

    internal static void BeforePublication(AggregateToken owner)
    {
        current?.Capture();
        if (current?.Snapshots?.TryGetValue(owner, out Snapshot? snapshot) == true)
        {
            snapshot.Verify();
        }
    }

    internal static void BeforeChildrenPublication(AggregateToken owner)
    {
        BeforePublication(owner);
        if (current?.Snapshots?.TryGetValue(owner, out Snapshot? snapshot) == true)
        {
            snapshot.RetirePrimitives(owner);
        }
    }

    internal static void BeforeChildPublication(AggregateToken owner, int index)
    {
        BeforePublication(owner);
        if (current?.Snapshots?.TryGetValue(owner, out Snapshot? snapshot) == true)
        {
            snapshot.RetirePrimitive(owner.ResolutionChildren[index]);
        }
    }

    internal static void ChildPublished(AggregateToken owner, int index, Token? replacement)
    {
        if (current is not State state || state.Snapshots is null ||
            !state.Snapshots.TryGetValue(owner, out Snapshot? snapshot))
        {
            return;
        }
        snapshot.Reset(owner);
        if (replacement is AggregateToken aggregate)
        {
            state.Capture(aggregate, snapshot.Root);
        }
        else if (replacement is not null)
        {
            snapshot.CaptureChildQuote(replacement);
            state.RegisterChecks(snapshot);
        }
    }

    internal static void ChildrenPublished(AggregateToken owner)
    {
        if (current?.Snapshots?.TryGetValue(owner, out Snapshot? snapshot) == true)
        {
            snapshot.Reset(owner);
            foreach (Token child in owner.ResolutionChildren)
            {
                if (child is AggregateToken aggregate)
                {
                    current.Capture(aggregate, snapshot.Root);
                }
                else
                {
                    snapshot.CaptureChildQuote(child);
                }
            }
            current.RegisterChecks(snapshot);
        }
    }

    internal static string? Resolve(AggregateToken token, char escapeChar,
        IDictionary<string, string?>? variables, ResolutionOptions? options)
    {
        if (!token.HasConsumerVariableResolution)
        {
            return token.ResolveVariables(escapeChar, variables, options);
        }
        using Scope scope = Enter(token);
        using CallbackScope callback = Callback();
        return token.ResolveVariables(escapeChar, variables, options);
    }

    internal static bool HasCustomLookup(IDictionary<string, string?> variables) =>
        variables is not Dictionary<string, string?> dictionary ||
        variables.GetType() != typeof(Dictionary<string, string?>) ||
        !ReferenceEquals(dictionary.Comparer, EqualityComparer<string>.Default) &&
        !ReferenceEquals(dictionary.Comparer, StringComparer.Ordinal) &&
        !ReferenceEquals(dictionary.Comparer, StringComparer.OrdinalIgnoreCase);

    internal static bool TryGetValue(IDictionary<string, string?> variables, string name, out string? value)
    {
        if (!HasCustomLookup(variables))
        {
            return variables.TryGetValue(name, out value);
        }
        using CallbackScope callback = Callback();
        return variables.TryGetValue(name, out value);
    }

    internal sealed class Snapshot
    {
        internal Snapshot(AggregateToken owner, AggregateToken root)
        {
            Root = root;
            requiresRawVerification = owner.GetType().Assembly != typeof(AggregateToken).Assembly;
            structureVersion = owner.ResolutionChildren.GetEnumerator();
            if (requiresRawVerification && owner is IQuotableToken quotable)
            {
                quote = new(quotable);
            }
        }

        internal AggregateToken Root { get; }
        internal bool RequiresBoundaryVerification => requiresRawVerification || childQuotes is not null;
        private readonly bool requiresRawVerification;
        private List<Token>.Enumerator structureVersion;
        private readonly QuoteSnapshot? quote;
        private List<QuoteSnapshot>? childQuotes;
        private List<Token>? retiredPrimitives;

        internal bool ContainsPrimitive(AggregateToken owner, Token token) =>
            owner.ResolutionChildren.Any(child => ReferenceEquals(child, token)) ||
            retiredPrimitives?.Any(child => ReferenceEquals(child, token)) == true;

        internal void RetirePrimitives(AggregateToken owner)
        {
            foreach (Token child in owner.ResolutionChildren)
            {
                RetirePrimitive(child);
            }
        }

        internal void RetirePrimitive(Token child)
        {
            if (child is PrimitiveToken)
            {
                retiredPrimitives ??= new();
                retiredPrimitives.Add(child);
            }
        }

        internal void Reset(AggregateToken owner) => structureVersion = owner.ResolutionChildren.GetEnumerator();

        internal void CaptureQuote()
        {
            quote?.Capture();
            if (childQuotes is not null)
            {
                foreach (QuoteSnapshot child in childQuotes)
                {
                    child.Capture();
                }
            }
        }

        internal void CaptureChildQuote(Token child)
        {
            if (child is AggregateToken || child is not IQuotableToken quotable ||
                childQuotes?.Any(snapshot => ReferenceEquals(snapshot.Token, quotable)) == true)
            {
                return;
            }
            childQuotes ??= new();
            childQuotes.Add(new(quotable));
        }

        internal void Verify()
        {
            quote?.Verify();
            if (childQuotes is not null)
            {
                foreach (QuoteSnapshot child in childQuotes)
                {
                    child.Verify();
                }
            }
            if (!requiresRawVerification)
            {
                return;
            }
            try
            {
                structureVersion.MoveNext();
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException(StructuralMutationMessage, error);
            }
        }

        private sealed class QuoteSnapshot(IQuotableToken token)
        {
            internal IQuotableToken Token { get; } = token;
            private char? quote;
            private bool captured;

            internal void Capture()
            {
                if (!captured)
                {
                    quote = Token.QuoteChar;
                    captured = true;
                }
            }

            internal void Verify()
            {
                if (captured && Token.QuoteChar != quote)
                {
                    throw new InvalidOperationException(StructuralMutationMessage);
                }
            }
        }
    }

    internal sealed class State
    {
        internal List<AggregateToken> Roots { get; } = new();
        internal List<Dockerfile> Documents { get; } = new();
        internal Dictionary<AggregateToken, Snapshot>? Snapshots { get; private set; }
        private readonly Dictionary<AggregateToken, List<AggregateToken>> rootMembers =
            new(ReferenceComparer<AggregateToken>.Instance);
        private readonly HashSet<Snapshot> boundarySnapshots = new();
        private int capturedRoots;
        internal int CallbackDepth { get; set; }

        internal bool IsProtected(Token token)
        {
            if (Snapshots is null)
            {
                return false;
            }
            if (token is AggregateToken aggregate)
            {
                return Snapshots.ContainsKey(aggregate);
            }
            foreach (KeyValuePair<AggregateToken, Snapshot> entry in Snapshots)
            {
                if (entry.Value.ContainsPrimitive(entry.Key, token))
                {
                    return true;
                }
            }
            return false;
        }

        internal void Capture()
        {
            while (capturedRoots < Roots.Count)
            {
                AggregateToken root = Roots[capturedRoots++];
                Capture(root, root);
            }
        }

        internal void Capture(AggregateToken source, AggregateToken root)
        {
            if (IsProtected(source))
            {
                return;
            }
            Snapshots ??= new(ReferenceComparer<AggregateToken>.Instance);
            if (!rootMembers.TryGetValue(root, out List<AggregateToken>? members))
            {
                members = new();
                rootMembers.Add(root, members);
            }
            Stack<AggregateToken> pending = new();
            pending.Push(source);
            while (pending.Count > 0)
            {
                AggregateToken owner = pending.Pop();
                if (IsProtected(owner))
                {
                    continue;
                }
                Snapshot snapshot = new(owner, root);
                Snapshots[owner] = snapshot;
                members.Add(owner);
                foreach (Token child in owner.ResolutionChildren)
                {
                    if (child is AggregateToken aggregate)
                    {
                        pending.Push(aggregate);
                    }
                    else
                    {
                        snapshot.CaptureChildQuote(child);
                    }
                }
                RegisterChecks(snapshot);
            }
        }

        internal void Verify()
        {
            foreach (Snapshot snapshot in boundarySnapshots)
            {
                snapshot.Verify();
            }
        }

        internal void CaptureQuotes()
        {
            foreach (Snapshot snapshot in boundarySnapshots)
            {
                snapshot.CaptureQuote();
            }
        }

        internal void RegisterChecks(Snapshot snapshot)
        {
            if (snapshot.RequiresBoundaryVerification)
            {
                boundarySnapshots.Add(snapshot);
                if (CallbackDepth > 0)
                {
                    snapshot.CaptureQuote();
                }
            }
        }

        internal void ReleaseRoots(int roots, int documentCount)
        {
            for (int index = Roots.Count - 1; index >= roots; index--)
            {
                AggregateToken root = Roots[index];
                Roots.RemoveAt(index);
                if (rootMembers.TryGetValue(root, out List<AggregateToken>? members) &&
                    !Roots.Any(candidate => ReferenceEquals(candidate, root)))
                {
                    foreach (AggregateToken member in members)
                    {
                        boundarySnapshots.Remove(Snapshots![member]);
                        Snapshots!.Remove(member);
                    }
                    rootMembers.Remove(root);
                }
            }
            capturedRoots = Math.Min(capturedRoots, Roots.Count);
            Documents.RemoveRange(documentCount, Documents.Count - documentCount);
        }
    }

    internal readonly struct Scope(State state, int roots, int documents, bool outermost) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                if (outermost)
                {
                    state.Verify();
                }
            }
            finally
            {
                state.ReleaseRoots(roots, documents);
                if (outermost)
                {
                    current = null;
                }
            }
        }
    }

    internal readonly struct CallbackScope(State? state) : IDisposable
    {
        public void Dispose()
        {
            if (state is null)
            {
                return;
            }
            try
            {
                state.Verify();
            }
            finally
            {
                state.CallbackDepth--;
            }
        }
    }
}
