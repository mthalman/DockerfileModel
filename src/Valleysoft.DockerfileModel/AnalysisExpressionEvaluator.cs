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
        private readonly StringBuilder text = new();
        private readonly List<string> unresolvedVariables = new();
        private AnalysisValue? failure;
        private char? quote;
        private bool escaped;

        public Evaluation(IReadOnlyDictionary<string, AnalysisValue> variables, char escapeChar)
        {
            this.variables = variables;
            this.escapeChar = escapeChar;
        }

        public void Visit(Token token)
        {
            if (token is LineContinuationToken or CommentToken or NewLineToken)
            {
                return;
            }

            if (token is VariableRefToken variable)
            {
                if (quote == '\'' || escaped)
                {
                    AppendText(variable.ToString());
                }
                else
                {
                    AppendValue(EvaluateVariable(variable, variables, escapeChar));
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
                text.Append(escapeChar);
            }
            return AnalysisValue.Known(text.ToString());
        }

        private void AppendValue(AnalysisValue value)
        {
            if (value.IsKnown)
            {
                text.Append(value.Value);
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
                    text.Append(escapeChar);
                }
                text.Append(character);
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
                    text.Append(character);
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
                text.Append(character);
            }
        }
    }
}
