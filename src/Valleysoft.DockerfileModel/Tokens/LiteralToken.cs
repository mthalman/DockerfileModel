namespace Valleysoft.DockerfileModel.Tokens;

/// <summary>A literal with optional quote syntax, continuations, and recognized variable-reference children.</summary>
public class LiteralToken : AggregateToken, IQuotableValueToken
{
    // Resolution is synchronous; scoped thread-local context avoids cloning consumer-owned options.
    [ThreadStatic]
    private static List<(LiteralToken Token, bool Json)>? resolutionContexts;

    private readonly bool canContainVariables;
    private readonly char escapeChar;
    private readonly bool preserveRawValue;
    private TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)>? wordParser;
    private char? quoteChar;
    private Dictionary<Token, int>? resolvedJsonLiterals;
    private List<Token>.Enumerator resolvedJsonVersion;

    /// <summary>Parses literal syntax using the supplied escape character and variable-recognition policy.</summary>
    /// <remarks>The escape context and variable policy are retained for later value updates.</remarks>
    public LiteralToken(string value, bool canContainVariables = false, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(value, canContainVariables, escapeChar), canContainVariables, escapeChar)
    {
    }

    private LiteralToken((IEnumerable<Token> Tokens, char? QuoteChar) tokensInfo, bool canContainVariables, char escapeChar)
        : this(tokensInfo.Tokens, canContainVariables, escapeChar)
    {
        QuoteChar = tokensInfo.QuoteChar;
    }

    internal LiteralToken(IEnumerable<Token> tokens, bool canContainVariables, char escapeChar, bool preserveRawValue = false,
        TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)>? wordParser = null, bool useJsonStringSyntax = false)
        : base(tokens)
    {
        this.canContainVariables = canContainVariables;
        this.escapeChar = escapeChar;
        EditingEscapeChar = escapeChar;
        this.preserveRawValue = preserveRawValue;
        this.wordParser = wordParser;
        UseJsonStringSyntax = useJsonStringSyntax;
    }

    internal bool UseJsonStringSyntax { get; set; }
    internal bool HasWordContext => wordParser is not null;

    /// <summary>Gets text without quote wrappers, continuations, comments, or newlines; sets the literal's inner contents.</summary>
    /// <remarks>
    /// Setting preserves an existing quote wrapper and, except for raw-value literals,
    /// reparses the contents with the retained context.
    /// For an unwrapped parsed or structurally adopted word, assigning its current value preserves fragment syntax;
    /// replacements receive a quote wrapper when required to retain one operand and its contents.
    /// The value is not a general JSON-unescaped or shell-expanded string. Use <c>ToString()</c> for complete syntax.
    /// </remarks>
    public string Value
    {
        get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
        set
        {
            Guard.NotNull(value, nameof(value));
            ResolutionGuard.BeforeMutation(this);
            if (wordParser is not null && QuoteChar is null && value == Value)
            {
                return;
            }
            Token[] contents = GetInnerTokens(value).ToArray();
            if (wordParser is not null && QuoteChar is null)
            {
                string expectedValue = new LiteralToken(contents, canContainVariables, escapeChar).Value;
                foreach (char? quote in new char?[] { null, '"', '\'' })
                {
                    string candidate = $"{quote}{string.Concat(contents.Select(token => token.ToString()))}{quote}";
                    Result<(IEnumerable<Token> Tokens, char? QuoteChar)> parsed = wordParser.TryParse(candidate);
                    if (parsed.HasValue &&
                        new LiteralToken(parsed.Value.Tokens, canContainVariables, escapeChar).Value == expectedValue)
                    {
                        ReplaceWithTokens(contents);
                        OnValueReplaced();
                        QuoteChar = quote;
                        return;
                    }
                }
                throw new ArgumentException("The value cannot be serialized as one operand without changing its contents.", nameof(value));
            }
            ReplaceWithTokens(contents);
            OnValueReplaced();
        }
    }

    internal void RetainWordContext(LiteralToken parsed) => wordParser = parsed.wordParser;

    /// <summary>Gets or sets the surrounding quote character, or null for no quote wrapper.</summary>
    /// <remarks>
    /// Changing a parsed or adopted mixed word's quote unwraps its fragment quotes while retaining variable-reference children,
    /// adding braces where a removed wrapper delimited a variable name.
    /// Assigning the existing quote preserves the original fragment syntax.
    /// Single quotes suppress variable resolution outside an enclosing double-quoted context.
    /// Reference children are retained so later quote changes can re-enable resolution without reparsing the value.
    /// </remarks>
    public virtual char? QuoteChar
    {
        get => quoteChar;
        set
        {
            ResolutionGuard.BeforeMutation(this);
            if (HasWordContext && quoteChar != value && Tokens.OfType<LiteralToken>().Any())
            {
                ReplaceWithTokens(StringParsers.UnwrapLiteralFragments(Tokens,
                    aggregate => aggregate.TokenList, variable => variable.TokenList));
            }
            quoteChar = value;
        }
    }

    internal bool ResolveAsJsonString { get; set; }
    internal bool CanContainVariables => canContainVariables;

    internal void OnValueReplaced()
    {
        resolvedJsonLiterals = null;
        resolvedJsonVersion = default;
    }

    // Bound delegates identify overrides without trim-unsafe member lookup.
    internal bool HasCustomResolution => GetType() != typeof(LiteralToken) &&
        ((Func<char, IDictionary<string, string?>?, ResolutionOptions?, string?>)ResolveVariables)
            .Method.DeclaringType != typeof(LiteralToken);
    internal bool HasDefaultRendering =>
        HasDefaultAggregateRendering;

    internal string? ResolveVariablesInContext(bool json, char escapeChar,
        IDictionary<string, string?>? variables, ResolutionOptions? options)
    {
        List<(LiteralToken Token, bool Json)> contexts = resolutionContexts ??= new();
        contexts.Add((this, json));
        try
        {
            return ResolutionGuard.Resolve(this, escapeChar, variables, options);
        }
        finally
        {
            contexts.RemoveAt(contexts.Count - 1);
        }
    }

    public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
        ResolutionOptions? options = null)
    {
        using ResolutionGuard.Scope scope = ResolutionGuard.Enter(this);
        bool json = ResolveAsJsonString;
        if (resolutionContexts is { } contexts)
        {
            for (int index = contexts.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(contexts[index].Token, this))
                {
                    json = contexts[index].Json;
                    break;
                }
            }
        }
        List<(LiteralToken Token, bool Json)> activeContexts = resolutionContexts ??= new();
        activeContexts.Add((this, json));
        try
        {
            return ResolveVariablesCore(json, escapeChar, variables, options);
        }
        finally
        {
            activeContexts.RemoveAt(activeContexts.Count - 1);
        }
    }

    private string? ResolveVariablesCore(bool json, char escapeChar,
        IDictionary<string, string?>? variables, ResolutionOptions? options)
    {
        if (!json || QuoteChar != '"')
        {
            return base.ResolveVariables(escapeChar, variables, options);
        }
        if (!canContainVariables && options?.RemoveEscapeCharacters != true)
        {
            return JsonStringVariableResolver.RenderToken(this);
        }
        string physical = JsonStringVariableResolver.RenderToken(this);
        PruneResolvedJsonLiterals();
        if (IsPlainJsonString(physical, escapeChar) &&
            !HasCustomDescendant())
        {
            return physical;
        }
        List<int> offsets = new();
        string logical = ConstructReader.FoldHeader(physical, 0, escapeChar, out int consumed, offsets);
        // Legacy constructors accept non-JSON strings; keep their token-based resolution.
        if (consumed != physical.Length || !JsonStringValidation.IsValidLogical(logical))
        {
            return base.ResolveVariables(escapeChar, variables, options);
        }
        options ??= new ResolutionOptions();
        variables ??= new Dictionary<string, string?>();
        return JsonStringVariableResolver.Resolve(this, physical, logical, offsets, escapeChar, variables, options,
            expandVariables: canContainVariables);
    }

    internal bool HasCustomDescendant()
    {
        Stack<AggregateToken>? pending = null;
        IEnumerable<Token> tokens = Tokens;
        while (true)
        {
            foreach (Token token in tokens)
            {
                if (token is VariableRefToken { HasCustomResolution: true } or
                    LiteralToken { HasCustomResolution: true } ||
                    token is AggregateToken { HasCustomVariableResolution: true } and not LiteralToken and not VariableRefToken)
                {
                    return true;
                }
                if (token is LiteralToken { CanContainVariables: true, HasDefaultRendering: true } or
                    VariableRefToken { HasDefaultRendering: true } ||
                    token is AggregateToken { HasDefaultAggregateRendering: true } and not LiteralToken and not VariableRefToken)
                {
                    pending ??= new();
                    pending.Push((AggregateToken)token);
                }
            }
            if (pending is null || pending.Count == 0)
            {
                return false;
            }
            tokens = pending.Pop().Tokens;
        }
    }

    private static bool IsPlainJsonString(string physical, char escapeChar)
    {
        if (physical.Length < 2 || physical[0] != '"' || physical[physical.Length - 1] != '"')
        {
            return false;
        }
        for (int index = 1; index < physical.Length - 1; index++)
        {
            char current = physical[index];
            if (current < ' ' || current == escapeChar || current is '\'' or '"' or '\\' ||
                VariableParsers.IsVariableReferenceStart(physical, index))
            {
                return false;
            }
        }
        return true;
    }

    internal bool IsResolvedJsonLiteral(Token token) => resolvedJsonLiterals?.ContainsKey(token) == true;

    internal void PruneResolvedJsonLiterals()
    {
        if (resolvedJsonLiterals is null)
        {
            return;
        }
        RecordResolvedJsonLiterals(Array.Empty<Token>());
    }

    internal void RecordResolvedJsonLiterals(IEnumerable<Token> literals)
    {
        // Primitive edits must not reactivate dollars or quotes inserted by an earlier substitution.
        HashSet<Token> additions = new(literals, ReferenceComparer<Token>.Instance);
        Dictionary<Token, int> retained = new(ReferenceComparer<Token>.Instance);
        foreach (Token child in Tokens)
        {
            if (additions.Contains(child) || IsResolvedJsonLiteral(child))
            {
                retained.TryGetValue(child, out int count);
                retained[child] = count + 1;
            }
        }
        resolvedJsonLiterals = retained.Count == 0 ? null : retained;
        resolvedJsonVersion = ResolutionChildren.GetEnumerator();
    }

    internal void RecordResolvedJsonLiteral(Token token)
    {
        resolvedJsonLiterals ??= new(ReferenceComparer<Token>.Instance);
        resolvedJsonLiterals[token] = 1;
        resolvedJsonVersion = ResolutionChildren.GetEnumerator();
    }

    internal void BeforeResolvedJsonChildEdit()
    {
        if (resolvedJsonLiterals is null)
        {
            return;
        }
        try
        {
            resolvedJsonVersion.MoveNext();
        }
        catch (InvalidOperationException)
        {
            // Protected raw-list edits bypass occurrence counters; reconcile only when their version changed.
            PruneResolvedJsonLiterals();
        }
    }

    internal void ResolvedJsonChildInserted(Token token)
    {
        if (resolvedJsonLiterals?.TryGetValue(token, out int count) == true)
        {
            resolvedJsonLiterals[token] = count + 1;
        }
        resolvedJsonVersion = ResolutionChildren.GetEnumerator();
    }

    internal void ResolvedJsonChildRemoved(Token token)
    {
        if (resolvedJsonLiterals?.TryGetValue(token, out int count) == true)
        {
            if (count > 1)
            {
                resolvedJsonLiterals[token] = count - 1;
            }
            else
            {
                resolvedJsonLiterals.Remove(token);
                if (resolvedJsonLiterals.Count == 0)
                {
                    resolvedJsonLiterals = null;
                }
            }
        }
        resolvedJsonVersion = ResolutionChildren.GetEnumerator();
    }

    protected virtual IEnumerable<Token> GetInnerTokens(string value)
    {
        if (preserveRawValue)
        {
            return new Token[] { new StringToken(value) };
        }
        if (ResolveAsJsonString && canContainVariables &&
            JsonStringValidation.IsValid($"\"{value}\"", escapeChar))
        {
            LiteralToken raw = new(new Token[] { new StringToken(value) }, canContainVariables: false, escapeChar)
            {
                QuoteChar = '"'
            };
            return JsonStringVariableResolver.Tokenize(raw, escapeChar).Tokens;
        }
        if (UseJsonStringSyntax)
        {
            IEnumerable<Token> tokens = CommandParsers.JsonStringTokens(escapeChar, canContainVariables)
                .Try().Many().Flatten().AtEnd().Parse(value);
            return TokenHelper.CollapseTokens(tokens,
                token => token is StringToken || token.GetType() == typeof(WhitespaceToken),
                text => new StringToken(text));
        }
        return GetTokens(value, canContainVariables, escapeChar).Tokens;
    }

    private static (IEnumerable<Token> Tokens, char? QuoteChar) GetTokens(string value, bool canContainVariables, char escapeChar)
    {
        Guard.NotNull(value, nameof(value));

        if (value == string.Empty)
        {
            return (new Token[] { new StringToken(value) }, null);
        }

        TextParser<(IEnumerable<Token> Tokens, char? QuoteChar)> parser;
        if (canContainVariables)
        {
            parser = LiteralWithVariablesTokens(escapeChar, whitespaceMode: WhitespaceMode.Allowed);
        }
        else
        {
            parser = WrappedInOptionalQuotesLiteralStringWithSpaces(escapeChar, excludeVariableRefChars: false);
        }

        return parser.Parse(value);
    }
}
