using System.Text;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>A mutable, token-preserving Dockerfile document.</summary>
/// <remarks>
/// Parsing and serializing unchanged content preserves its text, including whitespace and comments.
/// Edits change the shared model; constructs and tokens are not detached value objects.
/// </remarks>
public class Dockerfile : IConstructContainer
{
    /// <summary>The backslash escape character used when no effective escape directive is present.</summary>
    public const char DefaultEscapeChar = '\\';

    /// <summary>Creates an empty document without inserting any text.</summary>
    public Dockerfile() : this(Enumerable.Empty<DockerfileConstruct>())
    {
    }

    /// <summary>Creates a document from the supplied constructs in enumeration order.</summary>
    /// <param name="items">Constructs whose references are copied, not cloned. No separators are inserted.</param>
    public Dockerfile(IEnumerable<DockerfileConstruct> items)
    {
        Guard.NotNull(items, nameof(items));
        RawItems = items.ToList();
        Items = new EditableList<DockerfileConstruct>(new DocumentListAdapter(this));
    }

    /// <summary>Gets the live, syntax-aware editable view of this document's constructs.</summary>
    /// <remarks>
    /// Enumeration captures membership but retains shared construct objects. Insertion does not apply
    /// <see cref="DockerfileBuilder"/>'s automatic trailing-newline policy.
    /// See <see cref="EditableList{T}"/> for editing and trivia rules.
    /// </remarks>
    public EditableList<DockerfileConstruct> Items { get; }

    internal List<DockerfileConstruct> RawItems { get; }

    internal void AddRaw(DockerfileConstruct item)
    {
        ResolutionGuard.BeforeMutation(this);
        RawItems.Add(item);
    }

    IEnumerable<DockerfileConstruct> IConstructContainer.Items => Items;

    /// <summary>Gets the effective escape character from the current directive header, or <see cref="DefaultEscapeChar"/>.</summary>
    /// <remarks>Standalone instructions and tokens do not automatically inherit this value; supply it when constructing compatible content.</remarks>
    public char EscapeChar => DirectiveHeader.FromItems(Items).EscapeChar;

    /// <summary>Identifies the current header's frontend declaration without resolving tags or checking features.</summary>
    public DockerfileFrontendMetadata Frontend =>
        DockerfileFrontendMetadata.FromValue(DirectiveHeader.FromItems(Items).Syntax);

    /// <summary>Parses a complete Dockerfile strictly, preserving its original text.</summary>
    /// <param name="text">Dockerfile source, including any desired line endings.</param>
    /// <returns>A mutable document containing the parsed constructs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ParseException">The source cannot be parsed as a supported Dockerfile.</exception>
    /// <remarks>Use <c>TryParse</c> with explicit recovery options to retain malformed or unknown constructs.</remarks>
    public static Dockerfile Parse(string text)
    {
        Guard.NotNull(text, nameof(text));
        return DockerfileParser.ParseContent(text);
    }

    /// <summary>
    /// Reports source errors without throwing. Defaults to strict, fail-fast results;
    /// recovery and unknown-instruction preservation must be explicitly enabled.
    /// Null input and invalid options remain argument errors.
    /// </summary>
    /// <param name="text">The complete source to parse.</param>
    /// <param name="options">Parsing and preservation policy, or null for strict defaults.</param>
    /// <returns>A result containing diagnostics and, when available, the parsed or recovered model.</returns>
    public static DockerfileParseResult TryParse(string text, DockerfileParseOptions? options = null)
    {
        Guard.NotNull(text, nameof(text));
        options ??= new DockerfileParseOptions();
        if (options.Mode is not DockerfileParseMode.Strict and not DockerfileParseMode.Recover)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown parse mode.");
        }
        if (options.UnknownInstructionBehavior is not UnknownInstructionBehavior.Error and not UnknownInstructionBehavior.Preserve)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown instruction behavior.");
        }
        return TolerantDockerfileParser.Parse(text, options.Mode, options.UnknownInstructionBehavior);
    }

    /// <summary>
    /// Analyzes static stage and image references without changing this model.
    /// Overrides apply to ARG declarations and automatic platform ARGs, not COPY or mount source expansion.
    /// </summary>
    /// <param name="argOverrides">Build argument values used for this analysis, or null to use declarations.</param>
    /// <returns>A semantic analysis snapshot; later edits require a new analysis.</returns>
    /// <remarks>This is static analysis, not a Docker build or a frontend feature-support check.</remarks>
    public DockerfileAnalysis Analyze(IDictionary<string, string?>? argOverrides = null) =>
        new DockerfileAnalyzer(this, argOverrides).Analyze();

    /// <summary>Resolves ARG references for one instruction in its containing stage.</summary>
    /// <typeparam name="TInstruction">The type of the instruction to resolve.</typeparam>
    /// <param name="instruction">The existing instruction object in a stage of this document, including its opening FROM.</param>
    /// <param name="argValues">Overrides for declared ARG names, not an arbitrary variable environment.</param>
    /// <param name="options">Resolution behavior; null uses non-mutating defaults.</param>
    /// <returns>The complete resolved instruction text, including its keyword and retained trailing trivia, rather than just an operand.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instruction"/> is null.</exception>
    /// <exception cref="ArgumentException">The object is not in a stage of this document; global ARGs are not valid targets.</exception>
    /// <remarks>
    /// Global ARGs supply FROM values. Earlier ARGs in the selected stage establish its local scope;
    /// a declaration without a value can import a global ARG. With <see cref="ResolutionOptions.UpdateInline"/>,
    /// resolution can also change preceding global and stage ARG value tokens, not only the target.
    /// Overrides affect resolution scope and are not written back as declaration defaults.
    /// Command instructions such as RUN, CMD and ENTRYPOINT do not expand their runtime command variables.
    /// </remarks>
    public string ResolveVariables<TInstruction>(
        TInstruction instruction,
        IDictionary<string, string?>? argValues = null,
        ResolutionOptions? options = null)
        where TInstruction : Instruction
    {
        Guard.NotNull(instruction, nameof(instruction));

        bool foundInstruction = false;

        return ResolveVariables(
            argValues,
            stagesView =>
            {
                Stage? stage = stagesView.Stages
                    .FirstOrDefault(stage => stage.FromInstruction == instruction || stage.Items.Contains(instruction));

                if (stage is null)
                {
                    throw new ArgumentException(
                        $"Instruction '{instruction}' is not contained in this Dockerfile.", nameof(instruction));
                }

                return new Stage[] { stage };
            },
            currentInstruction =>
            {
                if (foundInstruction)
                {
                    return false;
                }

                if (currentInstruction == instruction)
                {
                    foundInstruction = true;
                }

                return currentInstruction is ArgInstruction || foundInstruction;
            },
            options);
    }

    /// <summary>Processes the document's stages in order, resolving ARG references in supported instructions.</summary>
    /// <param name="variableOverrides">Overrides for declared ARG names, or null to use declaration defaults.</param>
    /// <param name="options">Resolution behavior; null leaves the model unchanged.</param>
    /// <returns>The last processed instruction's text, or an empty string if no stage instruction was processed. This is not the complete Dockerfile.</returns>
    /// <remarks>
    /// Global ARGs apply to FROM. Each stage has its own ARG scope. A declaration without a default
    /// uses a global value when available, otherwise it keeps the current local value.
    /// Empty defaults count as set; null overrides leave an ARG unset. Ordinary defaults exclude
    /// quote syntax, line continuations, and comments. Runtime command variables are not expanded.
    /// <para>
    /// Custom token resolvers receive the supplied options, and their results are treated as values.
    /// References in defaults resolve right-to-left. Callbacks can change the variable dictionary
    /// and options, but not source tokens or completed results. Inline updates happen as references
    /// resolve; enabling them later does not update earlier references.
    /// Serializers and quote getters must be side-effect-free and repeatable.
    /// Non-inline stage resolution uses serialized declarations for instruction subclasses or changed boundaries.
    /// </para>
    /// <para>
    /// To serialize the entire updated document, enable <see cref="ResolutionOptions.UpdateInline"/> and then call <see cref="ToString"/>.
    /// </para>
    /// </remarks>
    public string ResolveVariables(IDictionary<string, string?>? variableOverrides = null, ResolutionOptions? options = null) =>
        ResolveVariables(
            variableOverrides,
            stagesView => stagesView.Stages,
            instruction => true,
            options);

    /// <summary>Concatenates the current constructs without inserting or normalizing whitespace.</summary>
    /// <returns>The complete current Dockerfile text; unchanged parsed documents round-trip exactly.</returns>
    public override string ToString()
    {
        StringBuilder builder = new();

        var items = Items.ToArray();
        for (int i = 0; i < items.Length; i++)
        {
            builder.Append(items[i].ToString());
        }

        return builder.ToString();
    }

    private string ResolveVariables(
        IDictionary<string, string?>? variableOverrides,
        Func<StagesView, IEnumerable<Stage>> getStages,
        Func<Instruction, bool> processInstruction,
        ResolutionOptions? options)
    {
        using ResolutionGuard.Scope scope = ResolutionGuard.Enter(this);
        variableOverrides ??= new Dictionary<string, string?>();
        options ??= new ResolutionOptions();

        StagesView stagesView = new(this);

        char escapeChar = EscapeChar;
        Dictionary<string, string?> globalArgs = GetGlobalArgs(stagesView, escapeChar, variableOverrides, options);

        var stages = getStages(stagesView);

        string? resolvedValue = null;

        foreach (Stage stage in stages)
        {
            if (processInstruction(stage.FromInstruction))
            {
                resolvedValue = ResolutionGuard.Resolve(stage.FromInstruction, escapeChar, globalArgs, options);
            }

            Dictionary<string, string?> stageArgs = new();

            IEnumerable<Instruction> instructions = stage.Items
                .OfType<Instruction>()
                .Where(instruction => processInstruction(instruction));

            foreach (Instruction instruction in instructions)
            {
                if (instruction is ArgInstruction argInstruction)
                {
                    resolvedValue = ResolveArgInstruction(
                        argInstruction,
                        escapeChar,
                        stageArgs,
                        globalArgs,
                        variableOverrides,
                        options);
                }
                else
                {
                    resolvedValue = ResolutionGuard.Resolve(instruction, escapeChar, stageArgs, options);
                }
            }
        }

        return resolvedValue ?? String.Empty;
    }

    private static string ResolveArgInstruction(ArgInstruction instruction, char escapeChar,
        Dictionary<string, string?> stageArgs, IDictionary<string, string?> globalArgs,
        IDictionary<string, string?> variableOverrides, ResolutionOptions options)
    {
        ArgInstruction resolvedInstruction;
        ArgDeclaration[] args;
        ArgDeclaration[] resolvedArgs;
        ArgNameSnapshot[]? originalNames = null;
        if (options.UpdateInline)
        {
            resolvedInstruction = instruction;
            args = instruction.ArgTokens.ToArray();
            resolvedArgs = args;
        }
        else
        {
            args = instruction.ArgTokens.ToArray();
            string syntax = instruction.ToString();
            List<(int Start, int Length)> parsedArgs = new();
            resolvedInstruction = ArgInstruction.Parse(syntax, escapeChar, parsedArgs.Add);
            resolvedArgs = resolvedInstruction.ArgTokens.ToArray();
            bool sameLayout = instruction.GetType() == typeof(ArgInstruction) && args.Length == resolvedArgs.Length;
            for (int i = 0; sameLayout && i < args.Length; i++)
            {
                sameLayout = args[i].HasAssignmentOperator == resolvedArgs[i].HasAssignmentOperator;
            }
            for (int i = 0; sameLayout && i < args.Length; i++)
            {
                sameLayout = MatchesArgDeclarationSyntax(args[i].ToString(), syntax, parsedArgs[i],
                    i == args.Length - 1);
            }
            if (!sameLayout)
            {
                return ResolveArgInstruction(resolvedInstruction, escapeChar, stageArgs, globalArgs, variableOverrides,
                    new ResolutionOptions { UpdateInline = true, RemoveEscapeCharacters = options.RemoveEscapeCharacters });
            }
            originalNames = args.Select(arg => new ArgNameSnapshot(arg)).ToArray();
        }
        using ResolutionGuard.Scope argScope = ResolutionGuard.Enter(resolvedInstruction);
        bool hasConsumerCallbacks = false;
        Action? recordConsumerCallback = originalNames is null ? null : () => hasConsumerCallbacks = true;
        for (int i = 0; i < args.Length; i++)
        {
            ArgDeclaration arg = args[i];
            ResolutionOptions argOptions = options;
            bool customName = AnalysisExpressionEvaluator.ContainsConsumerSerialization(arg.NameToken);
            string name = arg.Name;
            if (resolvedInstruction != instruction && name != resolvedArgs[i].Name)
            {
                bool useProjection = originalNames![i].Matches(arg.NameToken);
                if (useProjection && hasConsumerCallbacks && customName)
                {
                    useProjection = arg.NameToken.ToString() == resolvedArgs[i].NameToken.ToString();
                    name = arg.Name;
                    useProjection &= originalNames[i].Matches(arg.NameToken);
                }
                if (useProjection)
                {
                    arg = resolvedArgs[i];
                    name = arg.Name;
                    argOptions = new ResolutionOptions
                    {
                        UpdateInline = true,
                        RemoveEscapeCharacters = options.RemoveEscapeCharacters
                    };
                }
            }
            hasConsumerCallbacks |= customName;
            // Preserve inline live-name timing; detached resolution keeps the name consumed before callbacks.
            if (ResolutionGuard.TryGetValue(variableOverrides, name, out string? overrideArgValue))
            {
                SetArgBinding(stageArgs, resolvedInstruction == instruction ? arg.Name : name, overrideArgValue);
            }
            else if (arg.HasAssignmentOperator)
            {
                string value = ResolveArgDefault(arg, escapeChar, stageArgs, argOptions,
                    resolvedArgs[i], recordConsumerCallback);
                stageArgs[resolvedInstruction == instruction ? arg.Name : name] = value;
            }
            else if (globalArgs.TryGetValue(resolvedInstruction == instruction ? arg.Name : name, out string? globalArg) &&
                globalArg is not null)
            {
                stageArgs[resolvedInstruction == instruction ? arg.Name : name] = globalArg;
            }
        }

        return options.FormatValue(escapeChar,
            (options.UpdateInline ? instruction : resolvedInstruction).ToString());
    }

    private static bool MatchesArgDeclarationSyntax(string original, string syntax,
        (int Start, int Length) parsed, bool lastDeclaration)
    {
        int originalLength = original.Length;
        while (originalLength > 0 && Char.IsWhiteSpace(original[originalLength - 1])) originalLength--;
        int parsedLength = parsed.Length;
        while (parsedLength > 0 && Char.IsWhiteSpace(syntax[parsed.Start + parsedLength - 1])) parsedLength--;
        // The standalone ARG parser may leave a final suffix unconsumed.
        return (lastDeclaration ? originalLength >= parsedLength : originalLength == parsedLength) &&
            String.CompareOrdinal(original, 0, syntax, parsed.Start, parsedLength) == 0;
    }

    // Stored name edits can be checked without repeating a consumer serializer.
    private sealed class ArgNameSnapshot
    {
        private readonly Variable? name;
        private readonly char? quote;
        private readonly (Token Source, string? Value, Token[]? Children)[] descendants;

        public ArgNameSnapshot(ArgDeclaration declaration)
        {
            name = declaration.Tokens.OfType<Variable>().FirstOrDefault();
            quote = name?.QuoteChar;
            List<(Token Source, string? Value, Token[]? Children)> captured = new();
            HashSet<Token> visited = new(ReferenceComparer<Token>.Instance);
            Stack<Token> pending = new();
            if (name is not null) pending.Push(name);
            while (pending.Count > 0)
            {
                Token token = pending.Pop();
                if (!visited.Add(token)) continue;
                Token[]? children = (token as AggregateToken)?.Tokens.ToArray();
                captured.Add((token, (token as PrimitiveToken)?.Value, children));
                if (children is not null)
                {
                    for (int i = children.Length - 1; i >= 0; i--) pending.Push(children[i]);
                }
            }
            descendants = captured.ToArray();
        }

        public bool Matches(Variable current)
        {
            if (!ReferenceEquals(name, current) || quote != current.QuoteChar)
            {
                return false;
            }
            foreach (var captured in descendants)
            {
                if (captured.Value != (captured.Source as PrimitiveToken)?.Value)
                {
                    return false;
                }
                if (captured.Children is { } children && captured.Source is AggregateToken aggregate)
                {
                    List<Token> live = aggregate.TokenList;
                    if (children.Length != live.Count) return false;
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (!ReferenceEquals(children[i], live[i])) return false;
                    }
                }
            }
            return true;
        }
    }

    private static Dictionary<string, string?> GetGlobalArgs(StagesView stagesView, char escapeChar, IDictionary<string, string?> variables,
        ResolutionOptions options)
    {
        Dictionary<string, string?> globalArgs = new();
        foreach (ArgDeclaration arg in stagesView.GlobalArgs.SelectMany(inst => inst.ArgTokens))
        {
            if (ResolutionGuard.TryGetValue(variables, arg.Name, out string? overridenValue))
            {
                SetArgBinding(globalArgs, arg.Name, overridenValue);
            }
            else if (arg.HasAssignmentOperator)
            {
                string value = ResolveArgDefault(arg, escapeChar, globalArgs, options);
                globalArgs[arg.Name] = value;
            }
        }

        return globalArgs;
    }

    private static void SetArgBinding(Dictionary<string, string?> bindings, string name, string? value)
    {
        if (value is null)
        {
            bindings.Remove(name);
        }
        else
        {
            bindings[name] = value;
        }
    }

    private static string ResolveArgDefault(ArgDeclaration arg, char escapeChar,
        IDictionary<string, string?> bindings, ResolutionOptions options, ArgDeclaration? syntaxArg = null,
        Action? onCustomResolution = null)
    {
        if (arg.ValueToken is not LiteralToken valueToken)
        {
            return String.Empty;
        }

        bool updateInline = options.UpdateInline;
        bool resolveSyntax = updateInline || syntaxArg is not null && syntaxArg != arg;
        Dictionary<AggregateToken, Dictionary<int, Stack<TokenResolution>>>? resolutions = resolveSyntax
            ? new(ReferenceComparer<AggregateToken>.Instance) : null;
        void RecordResolution(AggregateToken owner, int index, TokenResolution resolution)
        {
            if (!resolutions!.TryGetValue(owner, out Dictionary<int, Stack<TokenResolution>>? positions))
            {
                positions = new();
                resolutions.Add(owner, positions);
            }
            if (!positions.TryGetValue(index, out Stack<TokenResolution>? occurrences))
            {
                occurrences = new();
                positions.Add(index, occurrences);
            }
            occurrences.Push(resolution);
        }
        IDictionary<string, string?> syntaxBindings = bindings;
        string[]? syntaxVariableNames = null;
        void CaptureSyntaxBindings()
        {
            if (syntaxArg is not null && syntaxArg != arg && ReferenceEquals(syntaxBindings, bindings))
            {
                syntaxVariableNames ??= syntaxArg.ValueToken is { } syntaxValue
                    ? InstructionCollectionEditing.Descendants(syntaxValue).OfType<VariableRefToken>()
                        .Select(variable => variable.VariableName).Distinct(StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>();
                if (syntaxVariableNames.Length > 0)
                {
                    Dictionary<string, string?> captured = new(syntaxVariableNames.Length, StringComparer.Ordinal);
                    foreach (string name in syntaxVariableNames)
                    {
                        if (bindings.TryGetValue(name, out string? binding))
                        {
                            captured.Add(name, binding);
                        }
                    }
                    syntaxBindings = captured;
                }
            }
        }
        // Non-inline stage defaults leave native trivia references to their reparsed copy.
        string value = ResolveArgValue(valueToken, escapeChar, bindings, options,
            recordResolution: updateInline ? RecordResolution : null,
            onCustomResolution: onCustomResolution,
            onBeforeCustomResolution: CaptureSyntaxBindings,
            resolveTriviaVariables: syntaxArg is null || syntaxArg == arg || updateInline) ?? String.Empty;

        if (resolutions is not null && (updateInline || !options.UpdateInline))
        {
            LiteralToken syntaxValue = options.UpdateInline ? valueToken : syntaxArg?.ValueToken ?? valueToken;
            if (syntaxValue != valueToken)
            {
                if (IsNativeConstant(valueToken) && IsNativeConstant(syntaxValue))
                {
                    return value;
                }
                // Reparsed copies have distinct token owners and need their own occurrence snapshots.
                ResolveArgValue(syntaxValue, escapeChar, syntaxBindings, options,
                    recordResolution: RecordResolution, shouldRecordSyntax: static () => true);
            }
            if (!syntaxValue.UsesCustomResolution &&
                (!updateInline || options.UpdateInline || syntaxValue != valueToken))
            {
                syntaxValue.ResolveVariablesInContext(false, escapeChar, bindings, new ResolutionOptions
                {
                    UpdateInline = true,
                    RemoveEscapeCharacters = options.RemoveEscapeCharacters,
                    TokenValueResolver = (owner, index, token) =>
                    {
                        if (!resolutions.TryGetValue(owner, out var positions) ||
                            !positions.TryGetValue(index, out var occurrences) || occurrences.Count == 0 ||
                            !ReferenceEquals(occurrences.Peek().Source, token))
                        {
                            return null;
                        }
                        TokenResolution resolution = occurrences.Pop();
                        return new TokenResolution(resolution.Source, resolution.SyntaxValue, resolution.SemanticValue,
                            syntaxValue != valueToken || resolution.UpdateInline);
                    }
                });
            }
        }

        return value;
    }

    private static bool IsNativeConstant(Token token) =>
        token is LiteralToken literal && !literal.UsesCustomResolution && literal.Tokens.All(IsNativeConstant) ||
        token is PrimitiveToken and not ResolvedStringToken and not NewLineToken &&
            token.GetType().Assembly == typeof(Token).Assembly;

    private static string? ResolveArgValue(Token token, char escapeChar,
        IDictionary<string, string?> bindings, ResolutionOptions options,
        Action<AggregateToken, int, TokenResolution>? recordResolution = null,
        Action? onCustomResolution = null, Action? onBeforeCustomResolution = null,
        Action<Func<string?>>? recordSyntax = null, Func<bool>? shouldRecordSyntax = null,
        bool resolveTriviaVariables = true)
    {
        int customResolutionVersion = 0;
        bool hasResolvedReference = false;
        void RecordCustomResolution()
        {
            if (hasResolvedReference)
            {
                onBeforeCustomResolution?.Invoke();
            }
            customResolutionVersion++;
            onCustomResolution?.Invoke();
        }

        Func<string?>? resolvedSyntax = null;
        string? resolvedRootValue = null;
        bool resolvedRoot = false;
        string? ResolveToken(AggregateToken aggregate)
        {
            bool hasConsumerSerialization = aggregate.UsesCustomResolution ||
                aggregate is VariableRefToken serializedVariable && serializedVariable.HasModifierTokens &&
                VariableRefToken.IsPatternModifier(serializedVariable.Modifier) &&
                AnalysisExpressionEvaluator.ContainsConsumerSerialization(aggregate);
            if (hasConsumerSerialization)
            {
                onBeforeCustomResolution?.Invoke();
            }
            Func<string?>? modifierSyntax = null;
            bool selectedModifier = false;
            bool ShouldRecordModifierSyntax() => options.UpdateInline ||
                (shouldRecordSyntax?.Invoke() ?? recordSyntax is not null);
            string? ResolveModifier(LiteralToken modifier)
            {
                selectedModifier = true;
                return ResolveArgValue(modifier, escapeChar, bindings, options,
                    recordResolution, RecordCustomResolution, onBeforeCustomResolution,
                    syntax => modifierSyntax = syntax, shouldRecordSyntax ?? ShouldRecordModifierSyntax,
                    resolveTriviaVariables);
            }
            string? value;
            using (aggregate.UsesCustomResolution ||
                aggregate is VariableRefToken variable && variable.HasModifierTokens
                ? options.UseModifierValueResolver(aggregate, escapeChar, bindings, ResolveModifier) : null)
            {
                value = ResolutionGuard.Resolve(aggregate, escapeChar, bindings, options);
            }
            if (hasConsumerSerialization)
            {
                RecordCustomResolution();
            }
            bool removeEscapes = options.RemoveEscapeCharacters;
            resolvedSyntax = !aggregate.UsesCustomResolution && selectedModifier
                ? () => ResolutionOptions.FormatValue(escapeChar, modifierSyntax?.Invoke() ?? String.Empty, removeEscapes)
                : null;
            if (ReferenceEquals(aggregate, token))
            {
                resolvedRootValue = value;
                resolvedRoot = true;
            }
            hasResolvedReference = true;
            return value;
        }

        string result = AnalysisExpressionEvaluator.ResolveVariables(token, escapeChar, ResolveToken,
            resolveAggregate: ResolveToken, getCustomResolutionVersion: () => customResolutionVersion,
            getResolvedSyntaxFactory: () => resolvedSyntax, syntaxOptions: options,
            recordTokenResolution: recordResolution, recordSyntax: recordSyntax,
            onConsumerSerialization: RecordCustomResolution, shouldRecordSyntax: shouldRecordSyntax,
            resolveTriviaVariables: resolveTriviaVariables);
        return resolvedRoot ? resolvedRootValue : result;
    }
}
