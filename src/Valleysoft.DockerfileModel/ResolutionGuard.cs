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
        state.AddRoot(source);
        return new Scope(state, roots, documents, outermost);
    }

    internal static Scope Enter(Dockerfile source)
    {
        State state = GetState(out bool outermost);
        int roots = state.Roots.Count;
        int documents = state.Documents.Count;
        state.Documents.Add(source);
        foreach (DockerfileConstruct item in source.RawItems)
        {
            state.AddRoot(item);
        }
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
        if (current is State state && !state.HasPrimitiveIndex &&
            state.Snapshots?.TryGetValue(owner, out Snapshot? snapshot) == true)
        {
            foreach (Token child in owner.ResolutionChildren)
            {
                snapshot.RetirePrimitive(child);
            }
        }
    }

    internal static void BeforeChildPublication(AggregateToken owner, int index)
    {
        BeforePublication(owner);
        if (current is State state && !state.HasPrimitiveIndex &&
            state.Snapshots?.TryGetValue(owner, out Snapshot? snapshot) == true)
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
            state.CapturePrimitive(snapshot, replacement);
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
                    current.CapturePrimitive(snapshot, child);
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
        private HashSet<Token>? quotedChildren;
        internal List<Token>? RetiredPrimitives { get; private set; }

        internal void RetirePrimitive(Token child)
        {
            if (child is PrimitiveToken)
            {
                (RetiredPrimitives ??= new()).Add(child);
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
                !(quotedChildren ??= new(ReferenceComparer<Token>.Instance)).Add(child))
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
        private readonly Dictionary<AggregateToken, int> rootReferences =
            new(ReferenceComparer<AggregateToken>.Instance);
        private Dictionary<Token, int>? primitiveReferences;
        private Dictionary<AggregateToken, HashSet<Token>>? rootPrimitives;
        internal bool HasPrimitiveIndex => primitiveReferences is not null;
        private readonly HashSet<Snapshot> boundarySnapshots = new();
        private int capturedRoots;
        internal int CallbackDepth { get; set; }

        internal void AddRoot(AggregateToken source)
        {
            Roots.Add(source);
            rootReferences.TryGetValue(source, out int references);
            rootReferences[source] = references + 1;
        }

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
            if (primitiveReferences is null)
            {
                // Options-only callbacks should not pay for a primitive ownership index.
                primitiveReferences = new(ReferenceComparer<Token>.Instance);
                rootPrimitives = new(ReferenceComparer<AggregateToken>.Instance);
                foreach (KeyValuePair<AggregateToken, Snapshot> entry in Snapshots)
                {
                    foreach (Token child in entry.Key.ResolutionChildren)
                    {
                        if (child is not AggregateToken)
                        {
                            CapturePrimitive(entry.Value, child);
                        }
                    }
                    if (entry.Value.RetiredPrimitives is not null)
                    {
                        foreach (Token child in entry.Value.RetiredPrimitives)
                        {
                            CapturePrimitive(entry.Value, child);
                        }
                    }
                }
            }
            return primitiveReferences.ContainsKey(token);
        }

        internal void CapturePrimitive(Snapshot snapshot, Token child)
        {
            if (primitiveReferences is null)
            {
                return;
            }
            if (!rootPrimitives!.TryGetValue(snapshot.Root, out HashSet<Token>? primitives))
            {
                primitives = new(ReferenceComparer<Token>.Instance);
                rootPrimitives.Add(snapshot.Root, primitives);
            }
            if (primitives.Add(child))
            {
                primitiveReferences.TryGetValue(child, out int references);
                primitiveReferences[child] = references + 1;
            }
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
                        CapturePrimitive(snapshot, child);
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
                int references = rootReferences[root] - 1;
                if (references > 0)
                {
                    rootReferences[root] = references;
                    continue;
                }
                rootReferences.Remove(root);
                if (rootMembers.TryGetValue(root, out List<AggregateToken>? members))
                {
                    foreach (AggregateToken member in members)
                    {
                        boundarySnapshots.Remove(Snapshots![member]);
                        Snapshots!.Remove(member);
                    }
                    rootMembers.Remove(root);
                }
                if (rootPrimitives?.TryGetValue(root, out HashSet<Token>? primitives) == true)
                {
                    foreach (Token primitive in primitives)
                    {
                        int owners = primitiveReferences![primitive] - 1;
                        if (owners == 0)
                        {
                            primitiveReferences.Remove(primitive);
                        }
                        else
                        {
                            primitiveReferences[primitive] = owners;
                        }
                    }
                    rootPrimitives.Remove(root);
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
