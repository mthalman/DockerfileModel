using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Valleysoft.DockerfileModel;

/// <summary>Controls variable substitution and the formatting of returned resolved text.</summary>
/// <remarks>Consumer-defined token resolvers receive this options instance, including during document ARG evaluation.</remarks>
public class ResolutionOptions
{
    internal Func<Tokens.LiteralToken, string?>? GetModifierValueResolver(Tokens.VariableRefToken variable, char escapeChar,
        IDictionary<string, string?> variables)
    {
        for (ModifierResolutionScope? scope = ModifierResolutionScope.Current.Value;
            scope is not null; scope = scope.Parent)
        {
            if (scope.GetResolver(variable, this, escapeChar, variables) is { } resolver)
            {
                return resolver;
            }
        }
        return null;
    }

    internal Func<Tokens.AggregateToken, int, Tokens.AggregateToken, TokenResolution?>? TokenValueResolver { get; set; }

    internal IDisposable UseModifierValueResolver(Tokens.AggregateToken owner, char escapeChar,
        IDictionary<string, string?> variables,
        Func<Tokens.LiteralToken, string?> resolver) =>
        new ModifierResolutionScope(owner, this, escapeChar, variables, resolver);

    internal ModifierResolutionTraversal? TrackModifierResolutionTraversal(Tokens.AggregateToken owner,
        char escapeChar, IDictionary<string, string?> variables)
    {
        for (ModifierResolutionScope? scope = ModifierResolutionScope.Current.Value;
            scope is not null; scope = scope.Parent)
        {
            if (scope.GetContextResolver(this, escapeChar, variables) is not null)
            {
                return new(owner, this, escapeChar, variables);
            }
        }
        return null;
    }

    internal sealed class ModifierResolutionTraversal : IDisposable
    {
        private static readonly AsyncLocal<ModifierResolutionTraversal?> Current = new();
        private readonly ModifierResolutionTraversal? parent;
        private Tokens.AggregateToken? owner;
        private ResolutionOptions? options;
        private IDictionary<string, string?>? variables;
        private readonly char escapeChar;

        public ModifierResolutionTraversal(Tokens.AggregateToken owner, ResolutionOptions options,
            char escapeChar, IDictionary<string, string?> variables)
        {
            this.owner = owner;
            this.options = options;
            this.escapeChar = escapeChar;
            this.variables = variables;
            parent = Current.Value;
            Current.Value = this;
        }

        public int Index { get; set; } = -1;

        public static bool ContainsCurrentReference(Tokens.AggregateToken root, Tokens.VariableRefToken variable,
            ResolutionOptions options, char escapeChar, IDictionary<string, string?> variables)
        {
            // Library inline publication can detach or replace a resolved reference.
            Tokens.Token child = variable;
            for (ModifierResolutionTraversal? traversal = Current.Value;
                traversal is not null; traversal = traversal.parent)
            {
                Tokens.AggregateToken? owner = traversal.owner;
                if (owner is null || !ReferenceEquals(traversal.options, options) ||
                    traversal.escapeChar != escapeChar || !ReferenceEquals(traversal.variables, variables) ||
                    traversal.Index < 0 || traversal.Index >= owner.TokenList.Count ||
                    !ReferenceEquals(owner.TokenList[traversal.Index], child))
                {
                    return false;
                }
                if (ReferenceEquals(owner, root))
                {
                    return true;
                }
                child = owner;
            }
            return false;
        }

        public void Dispose()
        {
            owner = null;
            options = null;
            variables = null;
            Current.Value = parent;
        }
    }

    private sealed class ModifierResolutionScope : IDisposable
    {
        public static readonly AsyncLocal<ModifierResolutionScope?> Current = new();

        private Tokens.AggregateToken? owner;
        private ResolutionOptions? options;
        private IDictionary<string, string?>? variables;
        private readonly char escapeChar;
        private Func<Tokens.LiteralToken, string?>? resolver;
        private readonly object ownershipLock = new();
        private Dictionary<Tokens.VariableRefToken, OwnershipPath>? ownershipPaths;
        private HashSet<Tokens.AggregateToken>? ownershipSearchVisited;
        private List<(Tokens.AggregateToken Owner, int NextIndex)>? ownershipSearchPath;

        public ModifierResolutionScope(Tokens.AggregateToken owner, ResolutionOptions options, char escapeChar,
            IDictionary<string, string?> variables,
            Func<Tokens.LiteralToken, string?> resolver)
        {
            this.owner = owner;
            this.options = options;
            this.escapeChar = escapeChar;
            this.variables = variables;
            this.resolver = resolver;
            Parent = Current.Value;
            Current.Value = this;
        }

        public ModifierResolutionScope? Parent { get; }

        public Func<Tokens.LiteralToken, string?>? GetResolver(Tokens.VariableRefToken variable,
            ResolutionOptions requestedOptions,
            char requestedEscapeChar, IDictionary<string, string?> requestedVariables)
        {
            Func<Tokens.LiteralToken, string?>? activeResolver =
                GetContextResolver(requestedOptions, requestedEscapeChar, requestedVariables);
            Tokens.AggregateToken? activeOwner = owner;
            return activeResolver is not null && activeOwner is not null && (ReferenceEquals(activeOwner, variable) ||
                    ModifierResolutionTraversal.ContainsCurrentReference(activeOwner, variable, requestedOptions,
                        requestedEscapeChar, requestedVariables) ||
                    ContainsVariable(activeOwner, variable))
                ? activeResolver : null;
        }

        public Func<Tokens.LiteralToken, string?>? GetContextResolver(ResolutionOptions requestedOptions,
            char requestedEscapeChar, IDictionary<string, string?> requestedVariables)
        {
            Func<Tokens.LiteralToken, string?>? activeResolver = Volatile.Read(ref resolver);
            return activeResolver is not null && ReferenceEquals(options, requestedOptions) &&
                escapeChar == requestedEscapeChar && ReferenceEquals(variables, requestedVariables)
                ? activeResolver : null;
        }

        private bool ContainsVariable(Tokens.AggregateToken root, Tokens.VariableRefToken variable)
        {
            lock (ownershipLock)
            {
                if (!ReferenceEquals(owner, root) || resolver is null) return false;
                if (ownershipPaths is not null)
                {
                    // Library publications can remove references, but cannot introduce new members.
                    if (!ownershipPaths.TryGetValue(variable, out OwnershipPath? cached)) return false;
                    if (cached.Matches(root, variable)) return true;
                    OwnershipPath? found = FindOwnershipPath(root, variable);
                    if (found is not null)
                    {
                        ownershipPaths[variable] = found;
                        return true;
                    }
                    ownershipPaths.Remove(variable);
                    return false;
                }
                ownershipPaths = new(ReferenceComparer<Tokens.VariableRefToken>.Instance);
                HashSet<Tokens.AggregateToken> visited = new(ReferenceComparer<Tokens.AggregateToken>.Instance) { root };
                Stack<(Tokens.AggregateToken Owner, OwnershipPath? Path)> pending = new();
                pending.Push((root, null));
                while (pending.Count > 0)
                {
                    var current = pending.Pop();
                    for (int i = 0; i < current.Owner.TokenList.Count; i++)
                    {
                        if (current.Owner.TokenList[i] is not Tokens.AggregateToken aggregate) continue;
                        OwnershipPath path = new(current.Owner, i, current.Path);
                        if (aggregate is Tokens.VariableRefToken reference && !ownershipPaths.ContainsKey(reference))
                        {
                            ownershipPaths.Add(reference, path);
                        }
                        if (visited.Add(aggregate)) pending.Push((aggregate, path));
                    }
                }
                return ownershipPaths.TryGetValue(variable, out OwnershipPath? captured) &&
                    captured.Matches(root, variable);
            }
        }

        private OwnershipPath? FindOwnershipPath(Tokens.AggregateToken root, Tokens.VariableRefToken variable)
        {
            ownershipSearchVisited ??= new(ReferenceComparer<Tokens.AggregateToken>.Instance);
            ownershipSearchPath ??= new();
            ownershipSearchVisited.Add(root);
            ownershipSearchPath.Add((root, 0));
            try
            {
                while (ownershipSearchPath.Count > 0)
                {
                    int last = ownershipSearchPath.Count - 1;
                    var current = ownershipSearchPath[last];
                    if (current.NextIndex >= current.Owner.TokenList.Count)
                    {
                        ownershipSearchPath.RemoveAt(last);
                        continue;
                    }
                    ownershipSearchPath[last] = (current.Owner, current.NextIndex + 1);
                    if (current.Owner.TokenList[current.NextIndex] is not Tokens.AggregateToken aggregate) continue;
                    if (ReferenceEquals(aggregate, variable))
                    {
                        OwnershipPath? found = null;
                        foreach (var step in ownershipSearchPath)
                            found = new(step.Owner, step.NextIndex - 1, found);
                        return found;
                    }
                    if (ownershipSearchVisited.Add(aggregate)) ownershipSearchPath.Add((aggregate, 0));
                }
                return null;
            }
            finally
            {
                ownershipSearchVisited.Clear();
                ownershipSearchPath.Clear();
            }
        }

        private sealed class OwnershipPath
        {
            private readonly Tokens.AggregateToken owner;
            private readonly int index;
            private readonly OwnershipPath? parent;

            public OwnershipPath(Tokens.AggregateToken owner, int index, OwnershipPath? parent)
            {
                this.owner = owner;
                this.index = index;
                this.parent = parent;
            }

            public bool Matches(Tokens.AggregateToken root, Tokens.VariableRefToken variable)
            {
                Tokens.Token child = variable;
                for (OwnershipPath? path = this; path is not null; path = path.parent)
                {
                    if (path.index >= path.owner.TokenList.Count ||
                        !ReferenceEquals(path.owner.TokenList[path.index], child)) return false;
                    child = path.owner;
                }
                return ReferenceEquals(child, root);
            }
        }

        public void Dispose()
        {
            // Child execution contexts share this scope object after inheriting the AsyncLocal chain.
            lock (ownershipLock)
            {
                Interlocked.Exchange(ref resolver, null);
                ownershipPaths = null;
                ownershipSearchVisited = null;
                ownershipSearchPath = null;
                owner = null;
                options = null;
                variables = null;
            }
            Current.Value = Parent;
        }
    }

    /// <summary>Gets or sets whether resolved variable-reference tokens are replaced in the model. Defaults to false.</summary>
    /// <remarks>
    /// Document resolution may also process preceding ARG declarations. This does not write supplied overrides
    /// into declaration defaults or expand runtime commands.
    /// </remarks>
    public bool UpdateInline { get; set; }

    /// <summary>Gets or sets whether escape characters are removed from returned resolved text. Defaults to false.</summary>
    /// <remarks>
    /// A doubled escape character becomes one; other escape characters are omitted. This formatting option
    /// alone does not mutate tokens and is not a general shell or JSON unescaper.
    /// </remarks>
    public bool RemoveEscapeCharacters { get; set; }

    internal bool? JsonStringResolution { get; private set; }

    internal ResolutionOptions WithJsonStringResolution(bool enabled)
    {
        if (JsonStringResolution == enabled)
        {
            return this;
        }
        ResolutionOptions copy = (ResolutionOptions)MemberwiseClone();
        copy.JsonStringResolution = enabled;
        return copy;
    }

    internal string FormatValue(char escapeChar, string value) =>
        FormatValue(escapeChar, value, RemoveEscapeCharacters);

    internal static string FormatValue(char escapeChar, string value, bool removeEscapeCharacters)
    {
        if (removeEscapeCharacters)
        {
            StringBuilder builder = new();
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == escapeChar)
                {
                    if (i < value.Length - 1 && value[i + 1] == escapeChar)
                    {
                        builder.Append(escapeChar);
                        i++;
                    }
                    continue;
                }
                else
                {
                    builder.Append(value[i]);
                }
            }

            return builder.ToString();
        }

        return value;
    }
}

internal readonly struct TokenResolution
{
    public TokenResolution(Tokens.AggregateToken source, string? syntaxValue,
        (string Value, char? QuoteChar, bool Escaped)? semanticValue, bool updateInline = true)
    {
        Source = source;
        SyntaxValue = syntaxValue;
        SemanticValue = semanticValue;
        UpdateInline = updateInline;
    }

    public Tokens.AggregateToken Source { get; }
    public string? SyntaxValue { get; }
    public (string Value, char? QuoteChar, bool Escaped)? SemanticValue { get; }
    public bool UpdateInline { get; }
}
