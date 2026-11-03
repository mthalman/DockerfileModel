using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Valleysoft.DockerfileModel;

/// <summary>Controls variable substitution and the formatting of returned resolved text.</summary>
/// <remarks>Consumer-defined token resolvers receive this options instance, including during document ARG evaluation.</remarks>
public class ResolutionOptions
{
    internal Func<Tokens.LiteralToken, string?>? ModifierValueResolver { get; set; }

    internal Func<Tokens.LiteralToken, string?>? GetModifierValueResolver(Tokens.VariableRefToken variable, char escapeChar,
        IDictionary<string, string?> variables)
    {
        if (ModifierValueResolver is { } explicitResolver)
        {
            return explicitResolver;
        }
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

    private sealed class ModifierResolutionScope : IDisposable
    {
        public static readonly AsyncLocal<ModifierResolutionScope?> Current = new();

        private Tokens.AggregateToken? owner;
        private ResolutionOptions? options;
        private IDictionary<string, string?>? variables;
        private readonly char escapeChar;
        private Func<Tokens.LiteralToken, string?>? resolver;

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
            Func<Tokens.LiteralToken, string?>? activeResolver = Volatile.Read(ref resolver);
            Tokens.AggregateToken? activeOwner = owner;
            return activeResolver is not null && ReferenceEquals(options, requestedOptions) &&
                escapeChar == requestedEscapeChar && ReferenceEquals(variables, requestedVariables) &&
                activeOwner is not null && ContainsVariable(activeOwner, variable)
                ? activeResolver : null;
        }

        private static bool ContainsVariable(Tokens.AggregateToken root, Tokens.VariableRefToken variable)
        {
            if (ReferenceEquals(root, variable)) return true;
            HashSet<Tokens.AggregateToken> visited = new(ReferenceComparer<Tokens.AggregateToken>.Instance) { root };
            Stack<Tokens.AggregateToken> pending = new();
            pending.Push(root);
            while (pending.Count > 0)
            {
                foreach (Tokens.Token child in pending.Pop().Tokens)
                {
                    if (ReferenceEquals(child, variable)) return true;
                    if (child is Tokens.AggregateToken aggregate && visited.Add(aggregate))
                    {
                        pending.Push(aggregate);
                    }
                }
            }
            return false;
        }

        public void Dispose()
        {
            // Child execution contexts share this scope object after inheriting the AsyncLocal chain.
            Interlocked.Exchange(ref resolver, null);
            owner = null;
            options = null;
            variables = null;
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
