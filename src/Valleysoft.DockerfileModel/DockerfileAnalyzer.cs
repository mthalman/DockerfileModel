using System.Globalization;
using System.Text;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

internal sealed class DockerfileAnalyzer
{
    private const string UnmappedInheritedFlagsMessage =
        "Inherited ONBUILD flags cannot be mapped to original operand tokens after reparsing.";

    private static readonly HashSet<string> AutomaticArgs = new(StringComparer.Ordinal)
    {
        "BUILDPLATFORM", "BUILDOS", "BUILDARCH", "BUILDVARIANT",
        "TARGETPLATFORM", "TARGETOS", "TARGETARCH", "TARGETVARIANT"
    };
    private static readonly HashSet<string> MountTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bind", "cache", "tmpfs", "secret", "ssh"
    };

    private readonly Dockerfile dockerfile;
    private readonly Dictionary<string, string?> overrides;
    private readonly Dictionary<string, AnalysisValue> globals = new(StringComparer.Ordinal);
    private readonly List<AnalyzedStage> stages;
    private readonly Dictionary<string, AnalyzedStage> namedStages = new(StringComparer.Ordinal);
    private readonly List<DockerfileReference> references = new();
    private readonly List<AnalysisDiagnostic> diagnostics = new();
    private readonly Dictionary<AnalyzedStage, StageEnvironment> environments = new();
    private readonly Dictionary<AnalyzedStage, AnalyzedStage> baseStages = new();
    private readonly Dictionary<AnalyzedStage, IReadOnlyList<OnBuildInstruction>> triggers = new();

    public DockerfileAnalyzer(Dockerfile dockerfile, IDictionary<string, string?>? argOverrides)
    {
        this.dockerfile = dockerfile;
        overrides = argOverrides is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(argOverrides, StringComparer.Ordinal);
        stages = new StagesView(dockerfile).Stages.Select((stage, index) => new AnalyzedStage(stage, index)).ToList();
    }

    public DockerfileAnalysis Analyze()
    {
        ReadPreamble();
        RegisterStages();
        foreach (AnalyzedStage stage in stages)
        {
            StageEnvironment environment = baseStages.TryGetValue(stage, out AnalyzedStage? parent)
                ? environments[parent].Clone() : new StageEnvironment();
            environments.Add(stage, environment);

            if (parent is not null)
            {
                foreach (OnBuildInstruction trigger in triggers[parent])
                {
                    ProcessInstruction(trigger.Instruction, parent, stage, environment, trigger);
                }
            }

            List<OnBuildInstruction> declaredTriggers = new();
            foreach (Instruction instruction in stage.Source.Items.OfType<Instruction>())
            {
                if (instruction is OnBuildInstruction onBuild)
                {
                    declaredTriggers.Add(onBuild);
                    AddDeferredReferences(onBuild, stage);
                }
                else
                {
                    ProcessInstruction(instruction, stage, stage, environment);
                }
            }
            triggers.Add(stage, declaredTriggers.AsReadOnly());
        }
        DiagnoseCycles();
        return new DockerfileAnalysis(stages, OrderReferences(), OrderDiagnostics());
    }

    private void ReadPreamble()
    {
        foreach (string name in AutomaticArgs)
        {
            globals[name] = overrides.TryGetValue(name, out string? value)
                ? FromOverride(value) : AnalysisValue.Unknown(name);
        }
        foreach (DockerfileConstruct item in dockerfile.Items.TakeWhile(item => item is not FromInstruction))
        {
            if (item is ArgInstruction arg)
            {
                foreach (ArgDeclaration declaration in arg.ArgTokens)
                {
                    globals[declaration.Name] = ResolveArg(declaration, globals);
                }
            }
            else if (item is Instruction instruction)
            {
                diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticCode.InstructionBeforeFrom,
                    AnalysisDiagnosticSeverity.Error, "Only ARG instructions are permitted before the first FROM.",
                    instruction));
            }
        }
    }

    private void RegisterStages()
    {
        foreach (AnalyzedStage stage in stages)
        {
            AnalysisValue image = Evaluate(stage.FromInstruction.ImageNameToken, globals);
            DockerfileReference reference = Bind(DockerfileReferenceKind.BaseStage,
                stage.FromInstruction.ImageNameToken, image, stage.FromInstruction, stage, stage);
            if (reference.TargetStage is not null)
            {
                baseStages.Add(stage, reference.TargetStage);
            }
            if (stage.Name is not null)
            {
                namedStages[stage.Name.ToLowerInvariant()] = stage;
            }
        }

        foreach (IGrouping<string, AnalyzedStage> group in stages.Where(stage => stage.Name is not null)
            .GroupBy(stage => stage.Name!, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            int[] indices = group.Select(stage => stage.Index).ToArray();
            foreach (AnalyzedStage stage in group)
            {
                diagnostics.Add(new AnalysisDiagnostic(AnalysisDiagnosticCode.DuplicateStageName,
                    AnalysisDiagnosticSeverity.Warning, $"Stage name '{stage.Name}' is declared more than once.",
                    stage.FromInstruction, stage.FromInstruction.StageNameToken, stage, indices));
            }
        }
    }

    private void ProcessInstruction(Instruction instruction, AnalyzedStage declaringStage,
        AnalyzedStage sourceStage, StageEnvironment environment, OnBuildInstruction? onBuild = null)
    {
        // BuildKit reparses stored triggers without the declaring Dockerfile's directives.
        // Only builder-flag parsing changes; expression evaluation keeps the build's escape character.
        char flagEscapeChar = onBuild is null ? dockerfile.EscapeChar : Dockerfile.DefaultEscapeChar;
        if (onBuild is not null && instruction is CopyInstruction or RunInstruction &&
            HasUnmappedInheritedFlags(instruction))
        {
            AddUnmappedInheritedReference(instruction, declaringStage, sourceStage, onBuild);
            return;
        }
        if (instruction is ArgInstruction arg)
        {
            foreach (ArgDeclaration declaration in arg.ArgTokens)
            {
                AnalysisValue value;
                if (overrides.TryGetValue(declaration.Name, out string? overridden))
                {
                    value = FromOverride(overridden);
                }
                else if (declaration.Value is null && globals.TryGetValue(declaration.Name, out AnalysisValue? global) &&
                    (!global.IsKnown || global.IsSet))
                {
                    value = global;
                }
                else if (declaration.Value is null && environment.Args.TryGetValue(declaration.Name, out AnalysisValue? inherited))
                {
                    value = inherited;
                }
                else
                {
                    value = ResolveArg(declaration, environment.Values);
                }
                environment.Args[declaration.Name] = value;
            }
        }
        else if (instruction is EnvInstruction env)
        {
            Dictionary<string, AnalysisValue> before = environment.Values;
            foreach (KeyValueToken<Variable, LiteralToken> variable in env.VariableTokens)
            {
                environment.Env[variable.Key] = variable.ValueToken is null
                    ? AnalysisValue.Known("") : Evaluate(variable.ValueToken, before);
            }
        }
        else if (instruction is CopyInstruction copy && copy.FromStageNameToken is not null)
        {
            Bind(DockerfileReferenceKind.CopySource, copy.FromStageNameToken,
                Selector(copy.FromStageNameToken, isMount: false, flagEscapeChar), copy, declaringStage, sourceStage, onBuild);
        }
        else if (instruction is RunInstruction run)
        {
            foreach (Mount mount in run.Mounts)
            {
                if (HasUnmappedMountFields(mount, flagEscapeChar))
                {
                    AddUnmappedMountReference(mount, run, declaringStage, sourceStage, onBuild);
                    continue;
                }
                LiteralToken[] sources = mount.Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>()
                    .Where(entry => string.Equals(DecodeBuilderToken(entry.KeyToken, flagEscapeChar), "from", StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.ValueToken).OfType<LiteralToken>().ToArray();
                if (sources.Length == 0)
                {
                    continue;
                }
                // BuildKit rejects variable-bearing selectors while reading entries, even
                // if a later from entry would otherwise replace the offending value.
                foreach (LiteralToken overridden in sources.Take(sources.Length - 1))
                {
                    AnalysisValue previous = Selector(overridden, isMount: true, flagEscapeChar);
                    if (!previous.IsKnown)
                    {
                        Bind(DockerfileReferenceKind.MountSource, overridden, previous,
                            run, declaringStage, sourceStage, onBuild);
                    }
                }
                LiteralToken source = sources[sources.Length - 1];
                AnalysisValue value = Selector(source, isMount: true, flagEscapeChar);
                KeyValueToken<KeywordToken, LiteralToken>? type = MountEntry(mount, "type", flagEscapeChar);
                AnalysisValue mountType = type?.ValueToken is LiteralToken typeToken
                    ? EvaluateMountType(typeToken, environment.Values, flagEscapeChar) : AnalysisValue.Known("bind");
                if (value.IsKnown && !mountType.IsKnown)
                {
                    value = mountType;
                }
                else if (value.IsKnown && (!MountTypes.Contains(mountType.Value ?? "") ||
                    !string.IsNullOrEmpty(value.Value) &&
                    string.Equals(mountType.Value, "secret", StringComparison.OrdinalIgnoreCase)))
                {
                    DockerfileReference invalid = AddReference(DockerfileReferenceKind.MountSource,
                        DockerfileReferenceClassification.Invalid, source, value.Value,
                        run, declaringStage, sourceStage, null, onBuild);
                    AddDiagnostic(invalid, AnalysisDiagnosticCode.InvalidMountSource,
                        MountTypes.Contains(mountType.Value ?? "")
                            ? "Mount type 'secret' does not permit a nonempty from entry."
                            : $"Mount type '{mountType.Value}' is not supported.");
                    continue;
                }
                Bind(DockerfileReferenceKind.MountSource, source, value, run, declaringStage, sourceStage, onBuild);
            }
        }
    }

    private void AddDeferredReferences(OnBuildInstruction onBuild, AnalyzedStage stage)
    {
        if (onBuild.Instruction is CopyInstruction or RunInstruction && HasUnmappedInheritedFlags(onBuild.Instruction))
        {
            AddUnmappedInheritedReference(onBuild.Instruction, stage, null, onBuild);
            return;
        }
        if (onBuild.Instruction is CopyInstruction copy && copy.FromStageNameToken is LiteralToken source)
        {
            AddReference(DockerfileReferenceKind.CopySource, DockerfileReferenceClassification.Deferred,
                source, null, copy, stage, null, null, onBuild);
        }
        else if (onBuild.Instruction is RunInstruction run)
        {
            foreach (Mount mount in run.Mounts)
            {
                if (HasUnmappedMountFields(mount, Dockerfile.DefaultEscapeChar))
                {
                    AddUnmappedMountReference(mount, run, stage, null, onBuild);
                }
                else if (MountEntry(mount, "from", Dockerfile.DefaultEscapeChar)?.ValueToken is LiteralToken from)
                {
                    AddReference(DockerfileReferenceKind.MountSource, DockerfileReferenceClassification.Deferred,
                        from, null, run, stage, null, null, onBuild);
                }
            }
        }
    }

    private DockerfileReference Bind(DockerfileReferenceKind kind, LiteralToken operand,
        AnalysisValue value, Instruction instruction, AnalyzedStage declaringStage, AnalyzedStage sourceStage,
        OnBuildInstruction? onBuild = null)
    {
        if (!value.IsKnown || !value.IsSet)
        {
            AnalysisDiagnosticCode code = value.DiagnosticCode ?? AnalysisDiagnosticCode.UnresolvedVariable;
            bool invalid = code == AnalysisDiagnosticCode.UnsupportedVariableExpansion ||
                code == AnalysisDiagnosticCode.InvalidMountSource;
            DockerfileReference unresolved = AddReference(kind,
                invalid ? DockerfileReferenceClassification.Invalid : DockerfileReferenceClassification.Unresolved,
                operand, null, instruction, declaringStage, sourceStage, null, onBuild);
            AddDiagnostic(unresolved, code, value.Message ?? "The reference requires an unresolved variable.",
                variableNames: value.VariableNames);
            return unresolved;
        }

        string resolved = value.Value!;
        AnalyzedStage? target = null;
        if (kind == DockerfileReferenceKind.CopySource &&
            IsInteger(resolved) && long.TryParse(resolved, NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out long index))
        {
            if (index < 0 || index >= stages.Count)
            {
                DockerfileReference invalid = AddReference(kind, DockerfileReferenceClassification.Invalid,
                    operand, resolved, instruction, declaringStage, sourceStage, null, onBuild);
                AddDiagnostic(invalid, AnalysisDiagnosticCode.InvalidStageIndex,
                    $"Stage index '{resolved}' is outside the declared stage range.");
                return invalid;
            }
            target = stages[(int)index];
        }
        else
        {
            // BuildKit normalizes aliases, but only COPY/mount lookups normalize the operand.
            string lookup = kind == DockerfileReferenceKind.BaseStage ? resolved : resolved.ToLowerInvariant();
            namedStages.TryGetValue(lookup, out target);
        }

        DockerfileReferenceClassification classification = target is not null
            ? DockerfileReferenceClassification.Stage
            : resolved == "scratch" ? DockerfileReferenceClassification.Scratch
            : resolved.Length == 0 && kind != DockerfileReferenceKind.BaseStage
                ? DockerfileReferenceClassification.BuildContext
            : ImageReferenceSyntax.IsValid(resolved)
                ? DockerfileReferenceClassification.ExternalImage : DockerfileReferenceClassification.Invalid;
        DockerfileReference reference = AddReference(kind, classification,
            operand, resolved, instruction, declaringStage, sourceStage, target, onBuild);
        if (classification == DockerfileReferenceClassification.Invalid)
        {
            AddDiagnostic(reference, AnalysisDiagnosticCode.InvalidReference,
                $"'{resolved}' is not a valid stage or image reference.");
        }
        else if (target is not null && target.Index > sourceStage.Index)
        {
            AddDiagnostic(reference, AnalysisDiagnosticCode.ForwardStageReference,
                $"Stage '{resolved}' must be defined before the stage using it.", new[] { target.Index });
        }
        return reference;
    }

    private DockerfileReference AddReference(DockerfileReferenceKind kind,
        DockerfileReferenceClassification classification, Token operand, string? value,
        Instruction instruction, AnalyzedStage declaringStage, AnalyzedStage? sourceStage,
        AnalyzedStage? target, OnBuildInstruction? onBuild)
    {
        DockerfileReferencePhase phase = onBuild is null ? DockerfileReferencePhase.Immediate
            : sourceStage is null ? DockerfileReferencePhase.DeferredOnBuild : DockerfileReferencePhase.InheritedOnBuild;
        DockerfileReference reference = new(kind, classification, phase, value, instruction, operand,
            declaringStage, sourceStage, target, onBuild);
        references.Add(reference);
        return reference;
    }

    private void AddDiagnostic(DockerfileReference reference, AnalysisDiagnosticCode code,
        string message, IEnumerable<int>? relatedStageIndices = null, IEnumerable<string>? variableNames = null)
    {
        AnalysisDiagnostic diagnostic = new(code, AnalysisDiagnosticSeverity.Error, message,
            reference.Instruction, reference.OperandToken, reference.SourceStage ?? reference.DeclaringStage,
            relatedStageIndices, variableNames);
        reference.AddDiagnostic(diagnostic);
        diagnostics.Add(diagnostic);
    }

    private AnalysisValue Selector(LiteralToken operand, bool isMount, char flagEscapeChar)
    {
        string literal = DecodeBuilderToken(operand, flagEscapeChar);
        if (isMount)
        {
            int dollar = literal.IndexOf('$');
            if (dollar >= 0 && dollar < literal.Length - 1)
            {
                return AnalysisValue.Failure(AnalysisDiagnosticCode.UnsupportedVariableExpansion,
                    "Mount from does not support variable expansion. Use an ARG-expanded FROM stage alias instead.");
            }
            return AnalysisValue.Known(literal);
        }

        // COPY flags are literal tokens. Use a detached variable-aware token only for
        // the empty-environment expansion guard, never to substitute caller ARG values.
        LiteralToken expression;
        try
        {
            expression = new LiteralToken(literal, canContainVariables: true, dockerfile.EscapeChar);
        }
        catch (ParseException)
        {
            return AnalysisValue.Failure(AnalysisDiagnosticCode.UnsupportedVariableExpansion,
                "The COPY source contains an unsupported variable expression.");
        }
        AnalysisValue expanded = Evaluate(expression, new Dictionary<string, AnalysisValue>());
        if (!expanded.IsKnown || expanded.Value != literal)
        {
            return AnalysisValue.Failure(AnalysisDiagnosticCode.UnsupportedVariableExpansion,
                "COPY --from does not support variable expansion. Use an ARG-expanded FROM stage alias instead.",
                expanded.VariableNames);
        }
        return AnalysisValue.Known(literal);
    }

    private static string BuilderTokenText(Token token) =>
        token.ToString(new TokenStringOptions(
            excludeLineContinuations: true, excludeQuotes: false, excludeComments: true, excludeNewLines: true));

    private bool HasUnmappedInheritedFlags(Instruction instruction)
    {
        if (dockerfile.EscapeChar == Dockerfile.DefaultEscapeChar)
        {
            return false;
        }

        string[] originalFlags = BuilderFlags(instruction).Select(BuilderTokenText).ToArray();
        string text = instruction.ToString(new TokenStringOptions(
            excludeLineContinuations: true, excludeQuotes: false, excludeComments: true, excludeNewLines: false));
        try
        {
            // A detached parse can discover flags the original escape context left in the command.
            // Do not bind those flags to unrelated original tokens.
            Instruction reparsed = Instruction.CreateInstruction(text, Dockerfile.DefaultEscapeChar);
            return !originalFlags.SequenceEqual(BuilderFlags(reparsed).Select(BuilderTokenText));
        }
        catch (ParseException)
        {
            return true;
        }
    }

    private static IEnumerable<Token> BuilderFlags(Instruction instruction) =>
        instruction.Tokens.Where(token => token is MountFlag or KeyValueToken<KeywordToken, LiteralToken>);

    private void AddUnmappedInheritedReference(Instruction instruction, AnalyzedStage declaringStage,
        AnalyzedStage? sourceStage, OnBuildInstruction onBuild)
    {
        DockerfileReference reference = AddReference(
            instruction is CopyInstruction ? DockerfileReferenceKind.CopySource : DockerfileReferenceKind.MountSource,
            sourceStage is null ? DockerfileReferenceClassification.Deferred : DockerfileReferenceClassification.Unresolved,
            instruction, null, instruction, declaringStage, sourceStage, null, onBuild);
        if (sourceStage is not null)
        {
            AddDiagnostic(reference, AnalysisDiagnosticCode.UnsupportedEvaluation, UnmappedInheritedFlagsMessage);
        }
    }

    private static string DecodeBuilderToken(Token operand, char escapeChar)
    {
        string text = BuilderTokenText(operand);
        StringBuilder result = new();
        char? quote = null;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            // Builder flags remove escapes even inside single quotes; this is deliberately
            // different from shell expression evaluation, which happens after extraction.
            if (ch == escapeChar)
            {
                if (i + 1 < text.Length)
                {
                    result.Append(text[++i]);
                }
            }
            else if (quote == ch)
            {
                quote = null;
            }
            else if (quote is null && (ch == '\'' || ch == '"'))
            {
                quote = ch;
            }
            else
            {
                result.Append(ch);
            }
        }
        return result.ToString();
    }

    private AnalysisValue EvaluateMountType(LiteralToken operand, IReadOnlyDictionary<string, AnalysisValue> variables,
        char flagEscapeChar)
    {
        try
        {
            LiteralToken decoded = new(DecodeBuilderToken(operand, flagEscapeChar), canContainVariables: true, dockerfile.EscapeChar);
            return Evaluate(decoded, variables);
        }
        catch (ParseException)
        {
            return AnalysisValue.Failure(AnalysisDiagnosticCode.UnsupportedEvaluation,
                "The decoded mount type cannot be evaluated with the current token model.");
        }
    }

    private static bool HasUnmappedMountFields(Mount mount, char flagEscapeChar)
    {
        foreach (Token entry in mount.Tokens)
        {
            if (entry is KeyValueToken<KeywordToken, LiteralToken> pair)
            {
                if (DecodeBuilderToken(pair.KeyToken, flagEscapeChar).IndexOfAny(new[] { ',', '"', '=' }) >= 0 ||
                    pair.ValueToken is not null && DecodeBuilderToken(pair.ValueToken, flagEscapeChar).IndexOfAny(new[] { ',', '"' }) >= 0)
                {
                    return true;
                }
            }
            else if (entry is KeywordToken && DecodeBuilderToken(entry, flagEscapeChar).IndexOfAny(new[] { ',', '"', '=' }) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private void AddUnmappedMountReference(Mount mount, RunInstruction instruction, AnalyzedStage declaringStage,
        AnalyzedStage? sourceStage, OnBuildInstruction? onBuild)
    {
        // Builder decoding precedes CSV parsing. Escaped commas can introduce a different
        // from field inside a model value, so individual token bindings would be misleading.
        DockerfileReference reference = AddReference(DockerfileReferenceKind.MountSource,
            sourceStage is null ? DockerfileReferenceClassification.Deferred : DockerfileReferenceClassification.Unresolved,
            mount, null, instruction, declaringStage, sourceStage, null, onBuild);
        if (sourceStage is not null)
        {
            AddDiagnostic(reference, AnalysisDiagnosticCode.UnsupportedEvaluation,
                "Builder decoding can change the mount's CSV field boundaries. Its source cannot be mapped to an original operand token.");
        }
    }

    private AnalysisValue Evaluate(Token token, IReadOnlyDictionary<string, AnalysisValue> variables) =>
        AnalysisExpressionEvaluator.Evaluate(token, variables, dockerfile.EscapeChar);

    private AnalysisValue ResolveArg(ArgDeclaration declaration, IReadOnlyDictionary<string, AnalysisValue> variables)
    {
        if (overrides.TryGetValue(declaration.Name, out string? value))
        {
            return FromOverride(value);
        }
        if (declaration.ValueToken is not null)
        {
            return Evaluate(declaration.ValueToken, variables);
        }
        if (declaration.Value is not null)
        {
            return AnalysisValue.Known(declaration.Value);
        }
        return variables.TryGetValue(declaration.Name, out AnalysisValue? existing)
            ? existing : AnalysisValue.Unset();
    }

    private static AnalysisValue FromOverride(string? value) =>
        value is null ? AnalysisValue.Unset() : AnalysisValue.Known(value);

    private static KeyValueToken<KeywordToken, LiteralToken>? MountEntry(Mount mount, string key, char flagEscapeChar) =>
        mount.Tokens.OfType<KeyValueToken<KeywordToken, LiteralToken>>()
            .LastOrDefault(entry => string.Equals(DecodeBuilderToken(entry.KeyToken, flagEscapeChar), key, StringComparison.OrdinalIgnoreCase));

    private static bool IsInteger(string value)
    {
        int start = value.Length > 0 && (value[0] == '+' || value[0] == '-') ? 1 : 0;
        return value.Length > start && value.Skip(start).All(ch => ch >= '0' && ch <= '9');
    }

    private IEnumerable<DockerfileReference> OrderReferences() =>
        references.OrderBy(reference => (reference.SourceStage ?? reference.DeclaringStage).Index);

    private IEnumerable<AnalysisDiagnostic> OrderDiagnostics() =>
        diagnostics.OrderBy(diagnostic => diagnostic.Stage?.Index ?? -1);

    private void DiagnoseCycles()
    {
        Dictionary<AnalyzedStage, List<DockerfileReference>> outgoing = stages
            .ToDictionary(stage => stage, _ => new List<DockerfileReference>());
        Dictionary<AnalyzedStage, List<AnalyzedStage>> incoming = stages
            .ToDictionary(stage => stage, _ => new List<AnalyzedStage>());
        foreach (DockerfileReference reference in references.Where(reference => reference.TargetStage is not null))
        {
            outgoing[reference.SourceStage!].Add(reference);
            incoming[reference.TargetStage!].Add(reference.SourceStage!);
        }

        // Strongly connected components identify every cyclic edge in linear time. Both
        // traversals are iterative to avoid stack overflows on generated stage chains.
        HashSet<AnalyzedStage> visited = new();
        List<AnalyzedStage> finished = new();
        foreach (AnalyzedStage stage in stages)
        {
            Stack<(AnalyzedStage Stage, bool Finish)> pending = new();
            pending.Push((stage, false));
            while (pending.Count > 0)
            {
                (AnalyzedStage current, bool finish) = pending.Pop();
                if (finish)
                {
                    finished.Add(current);
                    continue;
                }
                if (!visited.Add(current))
                {
                    continue;
                }
                pending.Push((current, true));
                foreach (DockerfileReference dependency in outgoing[current])
                {
                    pending.Push((dependency.TargetStage!, false));
                }
            }
        }

        Dictionary<AnalyzedStage, int> componentOf = new();
        List<int[]> components = new();
        for (int i = finished.Count - 1; i >= 0; i--)
        {
            if (componentOf.ContainsKey(finished[i]))
            {
                continue;
            }
            List<int> members = new();
            Stack<AnalyzedStage> pending = new();
            pending.Push(finished[i]);
            while (pending.Count > 0)
            {
                AnalyzedStage current = pending.Pop();
                if (componentOf.ContainsKey(current))
                {
                    continue;
                }
                componentOf.Add(current, components.Count);
                members.Add(current.Index);
                foreach (AnalyzedStage dependent in incoming[current])
                {
                    pending.Push(dependent);
                }
            }
            components.Add(members.OrderBy(index => index).ToArray());
        }

        foreach (DockerfileReference edge in references.Where(reference => reference.TargetStage is not null))
        {
            int component = componentOf[edge.SourceStage!];
            if (component == componentOf[edge.TargetStage!] &&
                (components[component].Length > 1 || edge.SourceStage == edge.TargetStage))
            {
                AddDiagnostic(edge, AnalysisDiagnosticCode.CircularStageDependency,
                    "This reference participates in a circular stage dependency.", components[component]);
            }
        }
    }

    private sealed class StageEnvironment
    {
        public Dictionary<string, AnalysisValue> Args { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, AnalysisValue> Env { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, AnalysisValue> Values
        {
            get
            {
                Dictionary<string, AnalysisValue> result = new(Args, StringComparer.Ordinal);
                foreach (KeyValuePair<string, AnalysisValue> variable in Env)
                {
                    result[variable.Key] = variable.Value;
                }
                return result;
            }
        }

        public StageEnvironment Clone()
        {
            StageEnvironment result = new();
            foreach (KeyValuePair<string, AnalysisValue> variable in Args)
            {
                result.Args.Add(variable.Key, variable.Value);
            }
            foreach (KeyValuePair<string, AnalysisValue> variable in Env)
            {
                result.Env.Add(variable.Key, variable.Value);
            }
            return result;
        }
    }
}
