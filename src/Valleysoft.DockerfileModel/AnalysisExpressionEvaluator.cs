using System.Runtime.ExceptionServices;
using System.Text;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

internal sealed class AnalysisValue
{
    private AnalysisValue(bool isKnown, bool isSet, string? value,
        IEnumerable<string>? variableNames = null, AnalysisDiagnosticCode? diagnosticCode = null, string? message = null)
    {
        IsKnown = isKnown;
        IsSet = isSet;
        Value = value;
        VariableNames = Array.AsReadOnly((variableNames ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.Ordinal).ToArray());
        DiagnosticCode = diagnosticCode;
        Message = message;
    }

    public bool IsKnown { get; }

    public bool IsSet { get; }

    public string? Value { get; }

    public IReadOnlyList<string> VariableNames { get; }

    public AnalysisDiagnosticCode? DiagnosticCode { get; }

    public string? Message { get; }

    public static AnalysisValue Known(string value)
    {
        Guard.NotNull(value, nameof(value));
        return new AnalysisValue(isKnown: true, isSet: true, value);
    }

    public static AnalysisValue Unset() => new(isKnown: true, isSet: false, value: null);

    public static AnalysisValue Unknown(string variableName)
    {
        Guard.NotNullOrEmpty(variableName, nameof(variableName));
        return Failure(AnalysisDiagnosticCode.UnresolvedVariable,
            $"The value of variable '{variableName}' is unresolved.", new[] { variableName });
    }

    public static AnalysisValue Failure(AnalysisDiagnosticCode code, string message, IEnumerable<string>? variables = null)
    {
        Guard.NotNull(message, nameof(message));
        return new AnalysisValue(isKnown: false, isSet: false, value: null, variables, code, message);
    }
}

internal static class AnalysisExpressionEvaluator
{
    public static AnalysisValue Evaluate(Token token, IReadOnlyDictionary<string, AnalysisValue> variables, char escapeChar)
    {
        Guard.NotNull(token, nameof(token));
        Guard.NotNull(variables, nameof(variables));
        Evaluation evaluation = new(variables, escapeChar);
        evaluation.Visit(token);
        return evaluation.GetResult();
    }

    internal static string ResolveVariables(Token token, char escapeChar,
        Func<VariableRefToken, string?> resolveVariable,
        Action<VariableRefToken, (string Value, char? QuoteChar, bool Escaped)>? recordResolution = null,
        Func<LiteralToken, string>? resolveLiteral = null, Func<int>? getCustomResolutionVersion = null,
        Func<string?>? getResolvedSyntax = null, ResolutionOptions? syntaxOptions = null,
        Action<AggregateToken, int, TokenResolution>? recordTokenResolution = null,
        Action<string?>? recordSyntax = null, Func<AggregateToken, string?>? resolveAggregate = null)
    {
        Dictionary<string, AnalysisValue> variables = new();
        ResolutionSnapshot snapshot = new(token, resolveLiteral is not null || resolveAggregate is not null);
        List<ResolutionSnapshot> references = new();
        string CollectReference(ResolutionSnapshot reference)
        {
            references.Add(reference);
            return String.Empty;
        }
        void Scan()
        {
            references.Clear();
            Evaluation scan = new(variables, escapeChar, collectText: false,
                resolveSnapshot: CollectReference);
            snapshot.Visit(scan);
        }
        Scan();

        // Quote state flows forward, but public resolution gives rightmost references error precedence.
        for (int i = references.Count - 1; i >= 0; i--)
        {
            ResolutionSnapshot reference = references[i];
            reference.SerializationFailure?.Throw();
            int version = getCustomResolutionVersion?.Invoke() ?? 0;
            if (reference.IsConsumerLeaf)
            {
                bool changed = reference.RefreshSerialization(version);
                if (changed)
                {
                    Scan();
                    i = references.Count;
                }
                continue;
            }
            reference.ResolvedValue = reference.Source is VariableRefToken variable
                ? resolveVariable(variable)
                : resolveAggregate is not null ? resolveAggregate((AggregateToken)reference.Source)
                : resolveLiteral!((LiteralToken)reference.Source);
            reference.ResolvedSyntaxValue = getResolvedSyntax is null
                ? reference.ResolvedValue : getResolvedSyntax();
            reference.IsResolved = true;
            if (getCustomResolutionVersion is not null && version != getCustomResolutionVersion() &&
                reference.PendingSyntaxChanged())
            {
                // Reverse traversal has already consumed the suffix; only pending syntax can be refreshed.
                reference.RefreshPendingSyntax(getCustomResolutionVersion());
                Scan();
                i = references.Count;
            }
        }

        Evaluation evaluation = new(variables, escapeChar, recordResolution: recordResolution);
        snapshot.Visit(evaluation);
        AnalysisValue result = evaluation.GetResult();
        if (!result.IsKnown)
        {
            throw new InvalidOperationException(result.Message);
        }
        if (recordTokenResolution is not null)
        {
            snapshot.RecordTokenResolutions(escapeChar, syntaxOptions!, recordTokenResolution);
        }
        if (recordSyntax is not null)
        {
            recordSyntax(snapshot.GetResolvedSyntax(escapeChar, syntaxOptions!));
        }
        return result.Value!;
    }

    private sealed class ResolutionSnapshot
    {
        private readonly bool resolveCustomLiterals;
        private readonly string? primitiveValue;
        private char? capturedQuote;
        private readonly ResolvedStringToken? resolvedString;
        private string? serializedText;
        private int serializationVersion;
        private ExceptionDispatchInfo? syntaxReadFailure;
        private ResolutionSnapshot? parent;
        private int index;
        private List<ResolutionSnapshot> children = new();

        public ResolutionSnapshot(Token source, bool resolveCustomLiterals, ResolutionSnapshot? previous = null,
            int serializationVersion = 0)
        {
            Source = source;
            this.resolveCustomLiterals = resolveCustomLiterals;
            this.serializationVersion = serializationVersion;
            if (previous?.Source != source)
            {
                previous = null;
            }
            if (previous?.syntaxReadFailure is { } failure)
            {
                SerializationFailure = failure;
                SuppressedSyntax = previous.SuppressedSyntax;
                return;
            }
            IsReference = source is VariableRefToken ||
                source is AggregateToken aggregateToken && aggregateToken.UsesCustomResolution && resolveCustomLiterals;
            if (IsReference)
            {
                return;
            }
            try
            {
                capturedQuote = (source as IQuotableToken)?.QuoteChar;
            }
            catch (Exception error) when (resolveCustomLiterals &&
                error is not OutOfMemoryException and not OperationCanceledException)
            {
                SerializationFailure = ExceptionDispatchInfo.Capture(error);
                return;
            }
            OpeningQuote = ClosingQuote = capturedQuote;
            if (source is AggregateToken aggregate)
            {
                int childIndex = 0;
                foreach (Token child in aggregate.Tokens)
                {
                    ResolutionSnapshot? previousChild = previous is not null && childIndex < previous.children.Count
                        ? previous.children[childIndex] : null;
                    AddChild(new ResolutionSnapshot(child, resolveCustomLiterals, previousChild, serializationVersion));
                    childIndex++;
                }
            }
            else
            {
                primitiveValue = (source as PrimitiveToken)?.Value;
                if (source is not LineContinuationToken and not CommentToken and not NewLineToken)
                {
                    resolvedString = (source as ResolvedStringToken)?.CreateSnapshot();
                    try
                    {
                        serializedText = source.ToString(new TokenStringOptions(excludeQuotes: true));
                    }
                    catch (Exception error) when (resolveCustomLiterals &&
                        error is not OutOfMemoryException and not OperationCanceledException)
                    {
                        // Document resolution rethrows serializer failures at their reverse-order position.
                        SerializationFailure = ExceptionDispatchInfo.Capture(error);
                    }
                }
            }
        }

        public Token Source { get; }
        public ExceptionDispatchInfo? SerializationFailure { get; }
        public bool IsReference { get; }
        public bool IsConsumerLeaf => resolveCustomLiterals && Source is not AggregateToken &&
            Source.GetType().Assembly != typeof(Token).Assembly;
        public bool SerializationConsumed { get; private set; }
        public bool IsResolved { get; set; }
        public string? ResolvedValue { get; set; }
        public string? ResolvedSyntaxValue { get; set; }
        public string? SuppressedSyntax { get; set; }
        public (char? QuoteChar, bool Escaped) Context { get; set; }
        public char? OpeningQuote { get; private set; }
        public char? ClosingQuote { get; private set; }

        private void AddChild(ResolutionSnapshot child)
        {
            child.parent = this;
            child.index = children.Count;
            children.Add(child);
        }

        private bool Matches(Token token)
        {
            if (IsReference)
            {
                if (Source != token || syntaxReadFailure is not null)
                {
                    return false;
                }
                try
                {
                    return SuppressedSyntax is null || SuppressedSyntax == token.ToString();
                }
                catch (Exception error) when (resolveCustomLiterals &&
                    error is not OutOfMemoryException and not OperationCanceledException)
                {
                    syntaxReadFailure = ExceptionDispatchInfo.Capture(error);
                    return false;
                }
            }
            if (Source != token || syntaxReadFailure is not null)
            {
                return false;
            }
            char? liveQuote;
            try
            {
                liveQuote = (token as IQuotableToken)?.QuoteChar;
            }
            catch (Exception error) when (resolveCustomLiterals &&
                error is not OutOfMemoryException and not OperationCanceledException)
            {
                syntaxReadFailure = ExceptionDispatchInfo.Capture(error);
                return false;
            }
            if (capturedQuote != liveQuote || token is PrimitiveToken primitive && primitiveValue != primitive.Value)
            {
                return false;
            }
            if (token is AggregateToken aggregate)
            {
                List<Token> liveChildren = aggregate.TokenList;
                if (children.Count != liveChildren.Count)
                {
                    return false;
                }
                for (int i = 0; i < children.Count; i++)
                {
                    if (!children[i].Matches(liveChildren[i]))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public bool PendingSyntaxChanged()
        {
            for (ResolutionSnapshot child = this; child.parent is { } owner; child = owner)
            {
                List<Token> liveChildren = ((AggregateToken)owner.Source).TokenList;
                if (owner.capturedQuote != (owner.Source as IQuotableToken)?.QuoteChar ||
                    liveChildren.Count < child.index)
                {
                    return true;
                }
                for (int i = 0; i < child.index; i++)
                {
                    if (!owner.children[i].Matches(liveChildren[i]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool RefreshSerialization(int version)
        {
            // Consumer serializers can change independently of Value; recheck only at their pending position.
            SerializationConsumed = true;
            if (serializationVersion == version)
            {
                return false;
            }
            char? quote = (Source as IQuotableToken)?.QuoteChar;
            string text = Source.ToString(new TokenStringOptions(excludeQuotes: true));
            bool changed = capturedQuote != quote || serializedText != text;
            capturedQuote = OpeningQuote = ClosingQuote = quote;
            serializedText = text;
            serializationVersion = version;
            return changed;
        }

        public void RefreshPendingSyntax(int version)
        {
            for (ResolutionSnapshot child = this; child.parent is { } owner; child = owner)
            {
                List<Token> liveChildren = ((AggregateToken)owner.Source).TokenList;
                List<ResolutionSnapshot> previous = owner.children;
                owner.children = new List<ResolutionSnapshot>();
                for (int i = 0; i < Math.Min(child.index, liveChildren.Count); i++)
                {
                    owner.AddChild(previous[i].Matches(liveChildren[i])
                        ? previous[i] : new ResolutionSnapshot(liveChildren[i], resolveCustomLiterals, previous[i], version));
                }
                for (int i = child.index; i < previous.Count; i++)
                {
                    owner.AddChild(previous[i]);
                }
                owner.capturedQuote = (owner.Source as IQuotableToken)?.QuoteChar;
                owner.OpeningQuote = owner.ClosingQuote = owner.capturedQuote;
            }
        }

        public void Visit(Evaluation evaluation) => evaluation.VisitSnapshot(this,
            children, resolvedString, serializedText);

        public void RecordTokenResolutions(char escapeChar, ResolutionOptions options,
            Action<AggregateToken, int, TokenResolution> record)
        {
            List<(AggregateToken Owner, int Index, AggregateToken Source, ResolutionSnapshot? Snapshot)> captures = new();
            Dictionary<AggregateToken, Queue<ResolutionSnapshot>>? displaced = null;
            void Collect(ResolutionSnapshot node)
            {
                if (node.IsReference || node.Source is not AggregateToken owner)
                {
                    return;
                }
                Dictionary<AggregateToken, Queue<int>> positions = new(ReferenceComparer<AggregateToken>.Instance);
                for (int liveIndex = 0; liveIndex < owner.TokenList.Count; liveIndex++)
                {
                    if (owner.TokenList[liveIndex] is not AggregateToken aggregate)
                    {
                        continue;
                    }
                    if (!positions.TryGetValue(aggregate, out Queue<int>? occurrences))
                    {
                        occurrences = new();
                        positions.Add(aggregate, occurrences);
                    }
                    occurrences.Enqueue(liveIndex);
                }
                foreach (ResolutionSnapshot child in node.children)
                {
                    if (child.Source is not AggregateToken aggregate)
                    {
                        continue;
                    }
                    if (positions.TryGetValue(aggregate, out Queue<int>? occurrences) && occurrences.Count > 0)
                    {
                        captures.Add((owner, occurrences.Dequeue(), aggregate, child));
                    }
                    else
                    {
                        displaced ??= new(ReferenceComparer<AggregateToken>.Instance);
                        if (!displaced.TryGetValue(aggregate, out Queue<ResolutionSnapshot>? moved))
                        {
                            moved = new();
                            displaced.Add(aggregate, moved);
                        }
                        moved.Enqueue(child);
                    }
                    Collect(child);
                }
                foreach (var position in positions)
                {
                    foreach (int liveIndex in position.Value)
                    {
                        captures.Add((owner, liveIndex, position.Key, null));
                    }
                }
            }
            Collect(this);
            foreach (var capture in captures)
            {
                ResolutionSnapshot? child = capture.Snapshot;
                if (child is null && displaced is not null &&
                    displaced.TryGetValue(capture.Source, out Queue<ResolutionSnapshot>? moved) && moved.Count > 0)
                {
                    child = moved.Dequeue();
                }
                if (child is null)
                {
                    continue;
                }
                (string Value, char? QuoteChar, bool Escaped)? semanticValue = capture.Source is VariableRefToken
                    ? (child.ResolvedValue ?? child.SuppressedSyntax ?? String.Empty,
                        child.Context.QuoteChar, child.Context.Escaped) : null;
                record(capture.Owner, capture.Index, new TokenResolution(capture.Source,
                    child.IsReference ? child.GetResolvedSyntax(escapeChar, options) : null, semanticValue));
            }
        }

        public string? GetResolvedSyntax(char escapeChar, ResolutionOptions options)
        {
            SerializationFailure?.Throw();
            if (IsReference)
            {
                return IsResolved ? ResolvedSyntaxValue
                    : options.FormatValue(escapeChar, SuppressedSyntax ?? Source.ToString());
            }
            if (Source is not AggregateToken)
            {
                string syntax = Source is LineContinuationToken or CommentToken or NewLineToken
                    ? Source.ToString() : $"{OpeningQuote}{serializedText}{ClosingQuote}";
                return options.FormatValue(escapeChar, syntax);
            }
            StringBuilder builder = new();
            builder.Append(OpeningQuote);
            foreach (ResolutionSnapshot child in children)
            {
                builder.Append(child.GetResolvedSyntax(escapeChar, options));
            }
            builder.Append(ClosingQuote);
            return builder.ToString();
        }
    }

    private static AnalysisValue EvaluateVariable(
        VariableRefToken token, IReadOnlyDictionary<string, AnalysisValue> variables, char escapeChar)
    {
        string name = token.VariableName;
        string? modifier = token.Modifier;
        if (modifier is not null && modifier != "-" && modifier != ":-" &&
            modifier != "+" && modifier != ":+" && modifier != "?" && modifier != ":?")
        {
            return AnalysisValue.Failure(AnalysisDiagnosticCode.UnsupportedEvaluation,
                $"Variable modifier '{modifier}' is not supported by analysis.", new[] { name });
        }

        AnalysisValue value = variables.TryGetValue(name, out AnalysisValue? binding) ? binding : AnalysisValue.Unset();
        if (!value.IsKnown)
        {
            return value;
        }

        if (modifier is null)
        {
            return value.IsSet ? value : AnalysisValue.Unknown(name);
        }

        bool isSet = value.IsSet && (modifier[0] != ':' || value.Value!.Length > 0);
        switch (modifier[modifier.Length - 1])
        {
            case '-':
                return isSet ? value : EvaluateModifierValue(token, variables, escapeChar);
            case '+':
                return isSet ? EvaluateModifierValue(token, variables, escapeChar) : AnalysisValue.Known("");
            case '?':
                if (isSet)
                {
                    return value;
                }
                AnalysisValue detail = EvaluateModifierValue(token, variables, escapeChar);
                return AnalysisValue.Failure(AnalysisDiagnosticCode.VariableSubstitutionFailed,
                    $"Variable '{name}' is not set{(modifier[0] == ':' ? " or is empty" : "")}. " +
                    $"Error detail: '{(detail.IsKnown ? detail.Value : detail.Message)}'.",
                    new[] { name }.Concat(detail.VariableNames));
            default:
                throw new InvalidOperationException("Unexpected variable modifier.");
        }
    }

    private static AnalysisValue EvaluateModifierValue(
        VariableRefToken token, IReadOnlyDictionary<string, AnalysisValue> variables, char escapeChar) =>
        token.ModifierValueToken is Token value
            ? Evaluate(value, variables, escapeChar)
            : AnalysisValue.Known("");

    private sealed class Evaluation
    {
        private readonly IReadOnlyDictionary<string, AnalysisValue> variables;
        private readonly char escapeChar;
        private readonly Action<VariableRefToken, (string Value, char? QuoteChar, bool Escaped)>? recordResolution;
        private readonly Func<ResolutionSnapshot, string>? resolveSnapshot;
        private readonly StringBuilder? text;
        private readonly List<string> unresolvedVariables = new();
        private AnalysisValue? failure;
        private char? quote;
        private bool escaped;

        public Evaluation(IReadOnlyDictionary<string, AnalysisValue> variables, char escapeChar,
            Action<VariableRefToken, (string Value, char? QuoteChar, bool Escaped)>? recordResolution = null,
            bool collectText = true,
            Func<ResolutionSnapshot, string>? resolveSnapshot = null)
        {
            this.variables = variables;
            this.escapeChar = escapeChar;
            this.recordResolution = recordResolution;
            this.resolveSnapshot = resolveSnapshot;
            text = collectText ? new StringBuilder() : null;
        }

        public void Visit(Token token)
        {
            if (token is LineContinuationToken or CommentToken or NewLineToken)
            {
                return;
            }

            if (token is ResolvedStringToken resolved && resolved.MatchesContext(quote, escaped, escapeChar))
            {
                text?.Append(resolved.SemanticValue);
                escaped = false;
                return;
            }

            if (token is VariableRefToken variable)
            {
                if (quote == '\'' || escaped)
                {
                    string syntax = variable.ToString();
                    recordResolution?.Invoke(variable, (syntax, quote, escaped));
                    AppendText(syntax);
                }
                else
                {
                    AnalysisValue value = EvaluateVariable(variable, variables, escapeChar);
                    if (value.IsKnown)
                    {
                        recordResolution?.Invoke(variable, (value.Value ?? String.Empty, quote, escaped));
                    }
                    AppendValue(value);
                }
                return;
            }

            char? wrappingQuote = (token as IQuotableToken)?.QuoteChar;
            if (wrappingQuote.HasValue)
            {
                AppendCharacter(wrappingQuote.Value);
            }

            if (token is AggregateToken aggregate)
            {
                foreach (Token child in aggregate.Tokens)
                {
                    Visit(child);
                }
            }
            else if (token is PrimitiveToken primitive)
            {
                AppendText(primitive.Value);
            }
            else
            {
                AppendValue(AnalysisValue.Failure(AnalysisDiagnosticCode.UnsupportedEvaluation,
                    $"Token type '{token.GetType().Name}' is not supported by analysis."));
            }

            if (wrappingQuote.HasValue)
            {
                AppendCharacter(wrappingQuote.Value);
            }
        }

        public void VisitSnapshot(ResolutionSnapshot snapshot, List<ResolutionSnapshot> children,
            ResolvedStringToken? resolvedString, string? serializedText)
        {
            if (snapshot.SerializationFailure is not null)
            {
                if (resolveSnapshot is not null)
                {
                    resolveSnapshot(snapshot);
                    if (snapshot.SuppressedSyntax is not null)
                    {
                        AppendText(snapshot.SuppressedSyntax);
                    }
                }
                else
                {
                    snapshot.SerializationFailure.Throw();
                }
                return;
            }
            if (snapshot.Source is LineContinuationToken or CommentToken or NewLineToken)
            {
                return;
            }
            if (resolveSnapshot is not null && snapshot.IsConsumerLeaf && !snapshot.SerializationConsumed)
            {
                resolveSnapshot(snapshot);
            }
            if (snapshot.IsReference)
            {
                if (snapshot.IsResolved)
                {
                    text?.Append(snapshot.ResolvedValue);
                    if (snapshot.Source is VariableRefToken resolvedVariable)
                    {
                        recordResolution?.Invoke(resolvedVariable,
                            (snapshot.ResolvedValue!, snapshot.Context.QuoteChar, snapshot.Context.Escaped));
                    }
                }
                else if (snapshot.Source is VariableRefToken variable &&
                    (quote == '\'' || escaped || resolveSnapshot is null && snapshot.SuppressedSyntax is not null))
                {
                    snapshot.SuppressedSyntax ??= variable.ToString();
                    if (resolveSnapshot is not null)
                    {
                        snapshot.Context = (quote, escaped);
                    }
                    recordResolution?.Invoke(variable,
                        (snapshot.SuppressedSyntax, snapshot.Context.QuoteChar, snapshot.Context.Escaped));
                    AppendText(snapshot.SuppressedSyntax);
                }
                else
                {
                    snapshot.Context = (quote, escaped);
                    AppendValue(AnalysisValue.Known(resolveSnapshot!(snapshot)));
                }
                return;
            }
            if (resolvedString is not null && resolvedString.MatchesContext(quote, escaped, escapeChar))
            {
                text?.Append(resolvedString.SemanticValue);
                escaped = false;
                return;
            }
            if (snapshot.OpeningQuote is char openingQuote)
            {
                AppendCharacter(openingQuote);
            }
            if (snapshot.Source is AggregateToken)
            {
                foreach (ResolutionSnapshot child in children)
                {
                    child.Visit(this);
                }
            }
            else if (serializedText is not null)
            {
                AppendText(serializedText);
            }
            else
            {
                AppendValue(AnalysisValue.Failure(AnalysisDiagnosticCode.UnsupportedEvaluation,
                    $"Token type '{snapshot.Source.GetType().Name}' is not supported by analysis."));
            }
            if (snapshot.ClosingQuote is char closingQuote)
            {
                AppendCharacter(closingQuote);
            }
        }

        public AnalysisValue GetResult()
        {
            if (quote.HasValue)
            {
                AppendValue(AnalysisValue.Failure(AnalysisDiagnosticCode.UnsupportedEvaluation,
                    "The expression contains an unterminated quote."));
            }

            if (failure is not null)
            {
                return AnalysisValue.Failure(failure.DiagnosticCode!.Value, failure.Message!, unresolvedVariables);
            }

            if (escaped)
            {
                text?.Append(escapeChar);
            }
            return AnalysisValue.Known(text?.ToString() ?? String.Empty);
        }

        private void AppendValue(AnalysisValue value)
        {
            if (value.IsKnown)
            {
                text?.Append(value.Value);
                return;
            }

            unresolvedVariables.AddRange(value.VariableNames);
            if (failure is null || (failure.DiagnosticCode == AnalysisDiagnosticCode.UnresolvedVariable &&
                value.DiagnosticCode != AnalysisDiagnosticCode.UnresolvedVariable))
            {
                failure = value;
            }
        }

        private void AppendText(string value)
        {
            foreach (char character in value)
            {
                AppendCharacter(character);
            }
        }

        private void AppendCharacter(char character)
        {
            if (escaped)
            {
                if (quote == '"' && character != '$' && character != '"' && character != escapeChar)
                {
                    text?.Append(escapeChar);
                }
                text?.Append(character);
                escaped = false;
            }
            else if (quote == '\'')
            {
                if (character == '\'')
                {
                    quote = null;
                }
                else
                {
                    text?.Append(character);
                }
            }
            else if (character == escapeChar)
            {
                escaped = true;
            }
            else if (character == quote)
            {
                quote = null;
            }
            else if (!quote.HasValue && character is '\'' or '"')
            {
                quote = character;
            }
            else
            {
                text?.Append(character);
            }
        }
    }
}
