using System.Text;
using System.Threading;

namespace Valleysoft.DockerfileModel;

/// <summary>Controls variable substitution and the formatting of returned resolved text.</summary>
/// <remarks>Consumer-defined token resolvers receive this options instance, including during document ARG evaluation.</remarks>
public class ResolutionOptions
{
    internal Func<Tokens.VariableRefToken, (string Value, char? QuoteChar, bool Escaped)?>? SemanticValueResolver { get; set; }

    private Func<Tokens.LiteralToken, string>? modifierValueResolver;

    internal Func<Tokens.LiteralToken, string>? ModifierValueResolver
    {
        get
        {
            for (ModifierResolutionScope? scope = ModifierResolutionScope.Current.Value;
                scope is not null; scope = scope.Parent)
            {
                if (ReferenceEquals(scope.Options, this))
                {
                    return scope.Resolver;
                }
            }
            return modifierValueResolver;
        }
        set => modifierValueResolver = value;
    }

    internal Func<Tokens.AggregateToken, string?>? CustomValueResolver { get; set; }

    internal Func<Tokens.VariableRefToken, (bool Exists, string? Value)>? VariableBindingResolver { get; set; }

    internal Func<Tokens.AggregateToken, int, Tokens.AggregateToken, TokenResolution?>? TokenValueResolver { get; set; }

    internal IDisposable UseModifierValueResolver(Func<Tokens.LiteralToken, string> resolver) =>
        new ModifierResolutionScope(this, resolver);

    private sealed class ModifierResolutionScope : IDisposable
    {
        public static readonly AsyncLocal<ModifierResolutionScope?> Current = new();

        public ModifierResolutionScope(ResolutionOptions options, Func<Tokens.LiteralToken, string> resolver)
        {
            Options = options;
            Resolver = resolver;
            Parent = Current.Value;
            Current.Value = this;
        }

        public ResolutionOptions Options { get; }
        public Func<Tokens.LiteralToken, string> Resolver { get; }
        public ModifierResolutionScope? Parent { get; }

        public void Dispose() => Current.Value = Parent;
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

    internal string FormatValue(char escapeChar, string value)
    {
        if (RemoveEscapeCharacters)
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
        (string Value, char? QuoteChar, bool Escaped)? semanticValue)
    {
        Source = source;
        SyntaxValue = syntaxValue;
        SemanticValue = semanticValue;
    }

    public Tokens.AggregateToken Source { get; }
    public string? SyntaxValue { get; }
    public (string Value, char? QuoteChar, bool Escaped)? SemanticValue { get; }
}
