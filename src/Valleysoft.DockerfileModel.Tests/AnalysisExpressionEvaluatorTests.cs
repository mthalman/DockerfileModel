using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class AnalysisExpressionEvaluatorTests
{
    [Fact]
    public void ValuesDistinguishUnsetEmptyAndUnknown()
    {
        AnalysisValue unset = AnalysisValue.Unset();
        Assert.True(unset.IsKnown);
        Assert.False(unset.IsSet);
        Assert.Null(unset.Value);
        Assert.Empty(unset.VariableNames);
        Assert.Null(unset.DiagnosticCode);

        AssertKnown(AnalysisValue.Known(""), "");
        AnalysisValue unknown = AnalysisValue.Unknown("BASE");
        Assert.False(unknown.IsKnown);
        Assert.Null(unknown.Value);
        Assert.Equal(new[] { "BASE" }, unknown.VariableNames);
        Assert.Equal(AnalysisDiagnosticCode.UnresolvedVariable, unknown.DiagnosticCode);
    }

    [Theory]
    [InlineData("$BASE", "alpine")]
    [InlineData("${BASE}:3.22", "alpine:3.22")]
    [InlineData("${BASE}${BASE}", "alpinealpine")]
    [InlineData("registry/${BASE}:latest", "registry/alpine:latest")]
    public void SubstitutesKnownValues(string expression, string expected)
    {
        AssertKnown(Evaluate(expression, AnalysisValue.Known("alpine")), expected);
    }

    [Fact]
    public void InsertedValuesAreNotExpandedOrUnquotedAgain()
    {
        AssertKnown(Evaluate("${BASE}", AnalysisValue.Known("$MISSING\\\"'")), "$MISSING\\\"'");
        AssertKnown(Evaluate("cost$"), "cost$");
        AssertKnown(Evaluate("\\$BASE"), "$BASE");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unset")]
    public void SimpleUnsetReferenceIsUnknown(string? state)
    {
        AnalysisValue result = Evaluate("${BASE}", state is null ? null : AnalysisValue.Unset());
        Assert.False(result.IsKnown);
        Assert.Null(result.Value);
        Assert.Equal(AnalysisDiagnosticCode.UnresolvedVariable, result.DiagnosticCode);
        Assert.Equal(new[] { "BASE" }, result.VariableNames);
    }

    [Theory]
    [InlineData("-", null, "fallback")]
    [InlineData("-", "unset", "fallback")]
    [InlineData("-", "", "")]
    [InlineData("-", "alpine", "alpine")]
    [InlineData(":-", null, "fallback")]
    [InlineData(":-", "unset", "fallback")]
    [InlineData(":-", "", "fallback")]
    [InlineData(":-", "alpine", "alpine")]
    [InlineData("+", null, "")]
    [InlineData("+", "unset", "")]
    [InlineData("+", "", "fallback")]
    [InlineData("+", "alpine", "fallback")]
    [InlineData(":+", null, "")]
    [InlineData(":+", "unset", "")]
    [InlineData(":+", "", "")]
    [InlineData(":+", "alpine", "fallback")]
    [InlineData("?", "", "")]
    [InlineData("?", "alpine", "alpine")]
    [InlineData(":?", "alpine", "alpine")]
    public void ModifiersDistinguishUnsetFromEmpty(string modifier, string? state, string expected)
    {
        AnalysisValue? value = state switch
        {
            null => null,
            "unset" => AnalysisValue.Unset(),
            _ => AnalysisValue.Known(state)
        };
        AssertKnown(Evaluate($"${{BASE{modifier}fallback}}", value), expected);
    }

    [Theory]
    [InlineData("?", null)]
    [InlineData("?", "unset")]
    [InlineData(":?", null)]
    [InlineData(":?", "unset")]
    [InlineData(":?", "")]
    public void RequiredModifierReportsFailure(string modifier, string? state)
    {
        AnalysisValue? value = state switch
        {
            null => null,
            "unset" => AnalysisValue.Unset(),
            _ => AnalysisValue.Known(state)
        };
        AnalysisValue result = Evaluate($"${{BASE{modifier}must provide a base}}", value);
        Assert.False(result.IsKnown);
        Assert.Null(result.Value);
        Assert.Equal(AnalysisDiagnosticCode.VariableSubstitutionFailed, result.DiagnosticCode);
        Assert.Contains("must provide a base", result.Message);
        Assert.Contains("BASE", result.VariableNames);
    }

    [Theory]
    [InlineData("${BASE:-${MISSING:?unused}}", "alpine", "alpine")]
    [InlineData("${BASE-${MISSING:?unused}}", "", "")]
    [InlineData("${BASE:?${MISSING:?unused}}", "alpine", "alpine")]
    [InlineData("${BASE?${MISSING:?unused}}", "", "")]
    [InlineData("${BASE:+${MISSING:?unused}}", "", "")]
    [InlineData("${BASE+${MISSING:?unused}}", null, "")]
    [InlineData("${BASE:-${MISSING#unused}}", "alpine", "alpine")]
    public void UnusedModifierBranchesAreNotEvaluated(string expression, string? value, string expected)
    {
        AssertKnown(Evaluate(expression, value is null ? null : AnalysisValue.Known(value)), expected);
    }

    [Theory]
    [InlineData("${BASE:-${OTHER:-alpine}}", "alpine")]
    [InlineData("${BASE:-}", "")]
    [InlineData("${BASE+}", "")]
    [InlineData("${BASE:-\"alpine:3.22\"}", "alpine:3.22")]
    [InlineData("${BASE:-'$OTHER'}", "$OTHER")]
    public void SelectedModifierBranchesAreEvaluated(string expression, string expected)
    {
        AssertKnown(Evaluate(expression), expected);
    }

    [Theory]
    [InlineData("${BASE}")]
    [InlineData("${BASE:-fallback}")]
    [InlineData("${BASE-fallback}")]
    [InlineData("${BASE:+alternate}")]
    [InlineData("${BASE+alternate}")]
    [InlineData("${BASE:?required}")]
    [InlineData("${BASE?required}")]
    public void UnknownDependenciesPropagateWithoutSelectingFallback(string expression)
    {
        AnalysisValue dependency = Evaluate("${OTHER}");
        AnalysisValue result = Evaluate(expression, dependency);
        Assert.False(result.IsKnown);
        Assert.Equal(AnalysisDiagnosticCode.UnresolvedVariable, result.DiagnosticCode);
        Assert.Equal(new[] { "OTHER" }, result.VariableNames);
    }

    [Fact]
    public void AggregatesUnknownNamesInFirstOccurrenceOrderWithoutDuplicates()
    {
        AnalysisValue result = Evaluate("$SECOND/${FIRST}-$SECOND");
        Assert.False(result.IsKnown);
        Assert.Null(result.Value);
        Assert.Equal(new[] { "SECOND", "FIRST" }, result.VariableNames);
    }

    [Theory]
    [InlineData("#")]
    [InlineData("##")]
    [InlineData("%")]
    [InlineData("%%")]
    [InlineData("/")]
    [InlineData("//")]
    public void PatternModifiersAreExplicitlyUnsupported(string modifier)
    {
        AnalysisValue result = Evaluate($"${{BASE{modifier}pattern}}", AnalysisValue.Known("alpine"));
        Assert.False(result.IsKnown);
        Assert.Equal(AnalysisDiagnosticCode.UnsupportedEvaluation, result.DiagnosticCode);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("\"${BASE}\"", "alpine")]
    [InlineData("'${BASE}'", "${BASE}")]
    [InlineData("'${BASE:?unused}'", "${BASE:?unused}")]
    [InlineData("\"a'${BASE}'z\"", "a'alpine'z")]
    [InlineData("'a\\b'", "a\\b")]
    [InlineData("\"a\\b\"", "a\\b")]
    [InlineData("\"a\\$BASE\"", "a$BASE")]
    [InlineData("\"a\\\"b\"", "a\"b")]
    [InlineData("a\\ b", "a b")]
    [InlineData("a\\\\b", "a\\b")]
    public void HonorsQuotesAndEscapes(string expression, string expected)
    {
        AssertKnown(Evaluate(expression, AnalysisValue.Known("alpine")), expected);
    }

    [Theory]
    [InlineData('`', "a`$BASE", "a$BASE")]
    [InlineData('`', "a` b", "a b")]
    [InlineData('`', "'a`b'", "a`b")]
    [InlineData('`', "\"a`b\"", "a`b")]
    [InlineData('`', "\"a`\"b\"", "a\"b")]
    [InlineData('`', "a\\b", "a\\b")]
    [InlineData('\\', "a\\\nb", "ab")]
    [InlineData('`', "a`\r\nb", "ab")]
    public void HonorsActiveEscapeCharacterAndContinuations(char escapeChar, string expression, string expected)
    {
        AssertKnown(Evaluate(expression, escapeChar: escapeChar), expected);
    }

    [Fact]
    public void QuoteAndEscapeStateCanSpanPrimitiveTokens()
    {
        LiteralToken token = new(
            new Token[]
            {
                new StringToken("pre'"),
                new VariableRefToken("BASE"),
                new StringToken("'\""),
                new VariableRefToken("BASE"),
                new StringToken("\"\\"),
                new StringToken(" ")
            },
            canContainVariables: true, '\\');
        AssertKnown(AnalysisExpressionEvaluator.Evaluate(token,
            new Dictionary<string, AnalysisValue> { ["BASE"] = AnalysisValue.Known("alpine") }, '\\'),
            "pre$BASEalpine ");
    }

    [Fact]
    public void FailureNamesAreReadOnlySnapshots()
    {
        List<string> names = new() { "BASE", "OTHER", "BASE" };
        AnalysisValue result = AnalysisValue.Failure(AnalysisDiagnosticCode.UnsupportedEvaluation, "unsupported", names);
        names.Clear();
        Assert.Equal(new[] { "BASE", "OTHER" }, result.VariableNames);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.VariableNames).Add("new"));
    }

    [Fact]
    public void EvaluationPreservesSerializationAndAllTokenIdentities()
    {
        LiteralToken token = new(
            new Token[]
            {
                VariableRefToken.Parse("${BASE:-${OTHER:-alpine}}"),
                new LineContinuationToken("\n"),
                new StringToken(":latest")
            },
            canContainVariables: true, '\\')
        {
            QuoteChar = '"'
        };
        string original = token.ToString();
        Token[] descendants = Descendants(token).ToArray();
        Dictionary<string, AnalysisValue> variables = new() { ["OTHER"] = AnalysisValue.Known("debian") };
        AnalysisValue binding = variables["OTHER"];

        AssertKnown(AnalysisExpressionEvaluator.Evaluate(token, variables, '\\'), "debian:latest");
        Assert.Equal(original, token.ToString());
        Assert.Equal(descendants.Length, Descendants(token).Count());
        Assert.All(descendants.Zip(Descendants(token)), pair => Assert.Same(pair.First, pair.Second));
        Assert.Same(binding, variables["OTHER"]);
        Assert.Single(variables);
    }

    private static AnalysisValue Evaluate(string expression, AnalysisValue? value = null, char escapeChar = '\\')
    {
        Dictionary<string, AnalysisValue> variables = new();
        if (value is not null)
        {
            variables.Add("BASE", value);
        }
        LiteralToken token = new(expression, canContainVariables: true, escapeChar);
        return AnalysisExpressionEvaluator.Evaluate(token, variables, escapeChar);
    }

    private static void AssertKnown(AnalysisValue result, string expected)
    {
        Assert.True(result.IsKnown, result.Message);
        Assert.True(result.IsSet);
        Assert.Equal(expected, result.Value);
        Assert.Empty(result.VariableNames);
        Assert.Null(result.DiagnosticCode);
        Assert.Null(result.Message);
    }

    private static IEnumerable<Token> Descendants(Token token)
    {
        yield return token;
        if (token is AggregateToken aggregate)
        {
            foreach (Token child in aggregate.Tokens.SelectMany(Descendants))
            {
                yield return child;
            }
        }
    }
}
