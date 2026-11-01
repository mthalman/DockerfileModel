using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Stages child-list and quote changes without changing their live token owners.</summary>
/// <remarks>
/// Staging copies lists, not tokens: callers must not mutate the token objects directly while
/// preparing an edit. Validate the rendered grammar, staged ownership, and expected semantics
/// before calling <see cref="Commit"/>. The plan enforces supported serialization and quote
/// implementations, but does not validate grammar or provide rollback.
/// </remarks>
internal sealed class TokenEditPlan
{
    private readonly Dictionary<AggregateToken, List<Token>> replacements =
        new(ReferenceComparer<AggregateToken>.Instance);
    private readonly Dictionary<IQuotableToken, char?> quotes =
        new(ReferenceComparer<IQuotableToken>.Instance);

    /// <summary>Gets the writable staged children, copying the live child list on first use.</summary>
    /// <param name="owner">The aggregate whose children are being edited, identified by reference.</param>
    /// <returns>The same staged list on repeated calls for this owner.</returns>
    public List<Token> Edit(AggregateToken owner)
    {
        EditValidation.ValidateSupportedToken(owner);
        if (!replacements.TryGetValue(owner, out List<Token>? tokens))
        {
            tokens = owner.Tokens.ToList();
            replacements.Add(owner, tokens);
        }
        return tokens;
    }

    /// <summary>Stages a quote change without invoking the live token's setter.</summary>
    /// <param name="token">The token whose surrounding quotes will change.</param>
    /// <param name="quote">The new quote character, or <see langword="null"/> to remove wrapping quotes.</param>
    public void SetQuote(IQuotableToken token, char? quote)
    {
        EditValidation.ValidateSupportedQuote(token);
        quotes[token] = quote;
    }

    /// <summary>Reads staged children when present, otherwise the owner's live children.</summary>
    /// <param name="owner">The aggregate being inspected.</param>
    /// <returns>A read-only view, not a defensive snapshot.</returns>
    /// <remarks>Use this view rather than the live tree when validating a prospective edit's descendants.</remarks>
    public IReadOnlyList<Token> Tokens(AggregateToken owner) =>
        replacements.TryGetValue(owner, out List<Token>? tokens) ? tokens : owner.TokenList;

    /// <summary>Serializes a token using staged children and quotes, including aggregate-specific wrapping syntax.</summary>
    /// <param name="token">The root of the prospective subtree to render.</param>
    /// <returns>The text that the staged subtree would serialize after commit.</returns>
    /// <remarks>Detects cycles during traversal; shared-node ownership still requires separate validation.</remarks>
    public string Render(Token token) => CreateRenderer()(token);

    /// <summary>Shares final quote normalization across related previews without retaining it on the mutable plan.</summary>
    /// <remarks>Use only while the staged lists, quote proposals, and live token objects remain unchanged.</remarks>
    internal Func<Token, string> CreateRenderer()
    {
        Dictionary<AggregateToken, List<Token>> effective = EffectiveReplacements();
        return token => Render(token, new HashSet<Token>(ReferenceComparer<Token>.Instance), effective);
    }

    private Dictionary<AggregateToken, List<Token>> EffectiveReplacements()
    {
        Dictionary<AggregateToken, List<Token>> effective = new(replacements, ReferenceComparer<AggregateToken>.Instance);
        IReadOnlyList<Token> Read(AggregateToken owner) =>
            effective.TryGetValue(owner, out List<Token>? children) ? children : owner.TokenList;
        foreach (KeyValuePair<IQuotableToken, char?> change in quotes)
        {
            if (change.Key is LiteralToken literal && change.Value != literal.QuoteChar &&
                Read(literal).OfType<LiteralToken>().Any())
            {
                effective[literal] = StringParsers.UnwrapLiteralFragments(Read(literal), Read, variable =>
                {
                    List<Token> children = Read(variable).ToList();
                    effective[variable] = children;
                    return children;
                });
            }
        }
        return effective;
    }

    private string Render(Token token, HashSet<Token> path, Dictionary<AggregateToken, List<Token>> effective)
    {
        EditValidation.ValidateSupportedToken(token);
        if (!path.Add(token))
        {
            throw new InvalidOperationException("A cyclic token tree cannot be edited.");
        }

        string text;
        if (token is AggregateToken aggregate)
        {
            IEnumerable<Token> children = effective.TryGetValue(aggregate, out List<Token>? edited)
                ? edited : aggregate.Tokens;
            text = string.Concat(children.Select(child => Render(child, path, effective)));
            if (token is VariableRefToken)
            {
                text = "$" + text;
            }
            if (token is IQuotableToken quotable)
            {
                char? quote = quotes.TryGetValue(quotable, out char? changed) ? changed : quotable.QuoteChar;
                text = $"{quote}{text}{quote}";
            }
        }
        else
        {
            text = token.ToString();
        }
        path.Remove(token);
        return text;
    }

    /// <summary>Publishes the staged children and quotes to their existing owner objects.</summary>
    /// <remarks>
    /// Call only after all edit-specific validation succeeds. Supported implementations are checked
    /// across staged subtrees and quote targets before reserving child-list capacity or publishing
    /// any changes. This method does not parse the result or provide rollback.
    /// </remarks>
    public void Commit()
    {
        Dictionary<AggregateToken, List<Token>> effective = EffectiveReplacements();
        ValidatePublication(effective);
        foreach (KeyValuePair<AggregateToken, List<Token>> change in effective)
        {
            if (change.Key.TokenList.Capacity < change.Value.Count)
            {
                change.Key.TokenList.Capacity = change.Value.Count;
            }
        }
        foreach (KeyValuePair<AggregateToken, List<Token>> change in effective)
        {
            change.Key.TokenList.Clear();
            change.Key.TokenList.AddRange(change.Value);
        }
        foreach (KeyValuePair<IQuotableToken, char?> change in quotes)
        {
            change.Key.QuoteChar = change.Value;
        }
    }

    /// <summary>Preflights every staged subtree and quote target without invoking consumer-defined serialization or setters.</summary>
    /// <remarks>
    /// Staged descendants, rather than live child lists, determine what will be adopted. Repeated
    /// visits across overlapping edit roots are harmless; ownership and cycle checks belong to
    /// the caller's validation and rendering.
    /// </remarks>
    private void ValidatePublication(Dictionary<AggregateToken, List<Token>> effective)
    {
        HashSet<Token> visited = new(ReferenceComparer<Token>.Instance);
        Stack<Token> pending = new(effective.Keys);
        while (pending.Count > 0)
        {
            Token token = pending.Pop();
            if (!visited.Add(token))
            {
                continue;
            }
            EditValidation.ValidateSupportedToken(token);
            if (token is AggregateToken aggregate)
            {
                foreach (Token child in effective.TryGetValue(aggregate, out List<Token>? children) ? children : aggregate.TokenList)
                {
                    pending.Push(child);
                }
            }
        }
        foreach (IQuotableToken token in quotes.Keys)
        {
            EditValidation.ValidateSupportedQuote(token);
        }
    }
}
