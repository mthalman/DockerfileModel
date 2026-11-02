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

    internal void AddRaw(DockerfileConstruct item) => RawItems.Add(item);

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
    /// Global ARGs supply FROM values; each stage starts a fresh local ARG scope. Stage declarations without
    /// defaults can import global values, otherwise they retain an existing local binding.
    /// Explicit empty assignments are set; null overrides are unset. Defaults contribute shell-decoded
    /// contents, excluding syntax quotes and continuation/comment trivia. Runtime command expansion is not performed.
    /// Consumer-defined aggregate tokens retain their public variable-resolution behavior and call context.
    /// Default references resolve
    /// right-to-left; custom callbacks can edit pending syntax but do not rewrite already-resolved output.
    /// Pending consumer serializers are rechecked after custom callbacks or later consumer serialization;
    /// edits to earlier syntax are honored without re-reading consumed output. Inline substitutions are visible to later callbacks as references
    /// are consumed; moved references retain their resolved syntax and semantic contents.
    /// To serialize the entire updated document, enable <see cref="ResolutionOptions.UpdateInline"/> and then call <see cref="ToString"/>.
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
                resolvedValue = stage.FromInstruction.ResolveVariables(escapeChar, globalArgs, options);
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
                    resolvedValue = instruction.ResolveVariables(escapeChar, stageArgs, options);
                }
            }
        }

        return resolvedValue ?? String.Empty;
    }

    private static string ResolveArgInstruction(ArgInstruction instruction, char escapeChar,
        Dictionary<string, string?> stageArgs, IDictionary<string, string?> globalArgs,
        IDictionary<string, string?> variableOverrides, ResolutionOptions options)
    {
        ArgInstruction resolvedInstruction = options.UpdateInline
            ? instruction
            : ArgInstruction.Parse(instruction.ToString(), escapeChar);

        ArgDeclaration[] args = instruction.ArgTokens.ToArray();
        ArgDeclaration[] resolvedArgs = resolvedInstruction == instruction
            ? args : resolvedInstruction.ArgTokens.ToArray();
        for (int i = 0; i < args.Length; i++)
        {
            ArgDeclaration arg = args[i];
            if (variableOverrides.TryGetValue(arg.Name, out string? overrideArgValue))
            {
                SetArgBinding(stageArgs, arg.Name, overrideArgValue);
            }
            else if (arg.HasAssignmentOperator)
            {
                stageArgs[arg.Name] = ResolveArgDefault(arg, escapeChar, stageArgs, options,
                    resolvedArgs[i]);
            }
            else if (globalArgs.TryGetValue(arg.Name, out string? globalArg) && globalArg is not null)
            {
                stageArgs[arg.Name] = globalArg;
            }
        }

        return options.FormatValue(escapeChar, resolvedInstruction.ToString());
    }

    private static Dictionary<string, string?> GetGlobalArgs(StagesView stagesView, char escapeChar, IDictionary<string, string?> variables,
        ResolutionOptions options)
    {
        Dictionary<string, string?> globalArgs = new();
        foreach (ArgDeclaration arg in stagesView.GlobalArgs.SelectMany(inst => inst.ArgTokens))
        {
            if (variables.TryGetValue(arg.Name, out string? overridenValue))
            {
                SetArgBinding(globalArgs, arg.Name, overridenValue);
            }
            else if (arg.HasAssignmentOperator)
            {
                globalArgs[arg.Name] = ResolveArgDefault(arg, escapeChar, globalArgs, options);
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
        IDictionary<string, string?> bindings, ResolutionOptions options, ArgDeclaration? syntaxArg = null)
    {
        if (arg.ValueToken is not LiteralToken valueToken)
        {
            return String.Empty;
        }

        bool resolveSyntax = options.UpdateInline || syntaxArg is not null && syntaxArg != arg;
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
        void CaptureSyntaxBindings()
        {
            if (syntaxArg is not null && syntaxArg != arg && ReferenceEquals(syntaxBindings, bindings))
            {
                syntaxBindings = new Dictionary<string, string?>(bindings);
            }
        }
        string value = ResolveArgValue(valueToken, escapeChar, bindings, options,
            recordResolution: options.UpdateInline ? RecordResolution : null,
            onBeforeCustomResolution: CaptureSyntaxBindings) ?? String.Empty;

        if (resolutions is not null)
        {
            LiteralToken syntaxValue = syntaxArg?.ValueToken ?? valueToken;
            if (syntaxValue != valueToken)
            {
                // Reparsed copies have distinct token owners and need their own occurrence snapshots.
                ResolveArgValue(syntaxValue, escapeChar, syntaxBindings, options,
                    recordResolution: RecordResolution);
            }
            if (!syntaxValue.UsesCustomResolution)
            {
                syntaxValue.ResolveVariables(escapeChar, bindings, new ResolutionOptions
                {
                    UpdateInline = true,
                    RemoveEscapeCharacters = options.RemoveEscapeCharacters,
                    TokenValueResolver = (owner, index, token) =>
                        resolutions.TryGetValue(owner, out var positions) &&
                        positions.TryGetValue(index, out var occurrences) && occurrences.Count > 0 &&
                        ReferenceEquals(occurrences.Peek().Source, token) ? occurrences.Pop() : null
                });
            }
        }

        return value;
    }

    private static string? ResolveArgValue(Token token, char escapeChar,
        IDictionary<string, string?> bindings, ResolutionOptions options,
        Action<AggregateToken, int, TokenResolution>? recordResolution = null,
        Action? onCustomResolution = null, Action? onBeforeCustomResolution = null,
        Action<string?>? recordSyntax = null)
    {
        int customResolutionVersion = 0;
        void RecordCustomResolution()
        {
            customResolutionVersion++;
            onCustomResolution?.Invoke();
        }

        string? resolvedSyntax = null;
        string? resolvedRootValue = null;
        bool resolvedRoot = false;
        string? ResolveToken(AggregateToken aggregate)
        {
            if (aggregate.UsesCustomResolution)
            {
                onBeforeCustomResolution?.Invoke();
            }
            string? modifierSyntax = null;
            bool selectedModifier = false;
            string? ResolveModifier(LiteralToken modifier)
            {
                selectedModifier = true;
                return ResolveArgValue(modifier, escapeChar, bindings, options,
                    recordResolution, RecordCustomResolution, onBeforeCustomResolution,
                    recordResolution is null && recordSyntax is null ? null : syntax => modifierSyntax = syntax);
            }
            ResolutionOptions tokenOptions = aggregate.UsesCustomResolution ? options : new ResolutionOptions
            {
                UpdateInline = options.UpdateInline,
                RemoveEscapeCharacters = options.RemoveEscapeCharacters,
                ModifierValueResolver = ResolveModifier
            };
            string? value;
            using (aggregate.UsesCustomResolution
                ? options.UseModifierValueResolver(escapeChar, bindings, ResolveModifier) : null)
            {
                value = aggregate.ResolveVariables(escapeChar, bindings, tokenOptions);
            }
            if (aggregate.UsesCustomResolution)
            {
                RecordCustomResolution();
            }
            resolvedSyntax = !aggregate.UsesCustomResolution && selectedModifier &&
                (recordResolution is not null || recordSyntax is not null)
                ? options.FormatValue(escapeChar, modifierSyntax ?? String.Empty) : value;
            if (ReferenceEquals(aggregate, token))
            {
                resolvedRootValue = value;
                resolvedRoot = true;
            }
            return value;
        }

        string result = AnalysisExpressionEvaluator.ResolveVariables(token, escapeChar, ResolveToken,
            resolveAggregate: ResolveToken, getCustomResolutionVersion: () => customResolutionVersion,
            getResolvedSyntax: () => resolvedSyntax, syntaxOptions: options,
            recordTokenResolution: recordResolution, recordSyntax: recordSyntax,
            onConsumerSerialization: RecordCustomResolution);
        return resolvedRoot ? resolvedRootValue : result;
    }
}
