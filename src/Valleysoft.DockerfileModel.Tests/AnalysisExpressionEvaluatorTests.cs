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

    [Fact]
    public void ResolutionScanDoesNotAllocateAnUnusedDecodedCopy()
    {
        const int length = 256 * 1024;
        string expected = new('x', length);
        LiteralToken token = new(expected, canContainVariables: true);
        AnalysisExpressionEvaluator.ResolveVariables(token, '\\', _ => String.Empty);
        long before = GC.GetAllocatedBytesForCurrentThread();

        string result = AnalysisExpressionEvaluator.ResolveVariables(token, '\\', _ => String.Empty);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(expected, result);
        Assert.InRange(allocated, 0, 5L * length);
    }

    [Fact]
    public void UnchangedCustomCallbacksDoNotAllocateRepeatedResolutionPlans()
    {
        const int count = 512;
        LiteralToken token = new(Enumerable.Range(0, count)
            .Select(_ => new VariableRefToken("X")), canContainVariables: true, '\\');
        int version = 0;
        string Resolve(VariableRefToken variable)
        {
            version++;
            return "x";
        }
        AnalysisExpressionEvaluator.ResolveVariables(token, '\\', Resolve,
            getCustomResolutionVersion: () => version);
        long before = GC.GetAllocatedBytesForCurrentThread();

        string result = AnalysisExpressionEvaluator.ResolveVariables(token, '\\', Resolve,
            getCustomResolutionVersion: () => version);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(new string('x', count), result);
        Assert.InRange(allocated, 0, 2048L * count);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(2048)]
    public void ChangedPendingSerializersDoNotAllocateRepeatedScans(int count)
    {
        bool active = false;
        int version = 0;
        long before = 0;
        LiteralToken token = new(Enumerable.Range(0, count)
            .Select(_ => (Token)new MutableSerializedString(() => active ? "new" : "old"))
            .Append(new VariableRefToken("X")), canContainVariables: true, '\\');
        string Resolve(VariableRefToken variable)
        {
            active = true;
            version++;
            before = GC.GetAllocatedBytesForCurrentThread();
            return "left";
        }
        string Run()
        {
            active = false;
            version = 0;
            return AnalysisExpressionEvaluator.ResolveVariables(token, '\\', Resolve,
                getCustomResolutionVersion: () => version, resolveAggregate: _ => String.Empty);
        }
        Run();

        string result = Run();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(String.Concat(Enumerable.Repeat("new", count)) + "left", result);
        Assert.InRange(allocated, 0, 96L * count + 4096);
    }

    private sealed class MutableSerializedString : StringToken
    {
        private readonly Func<string> serialize;

        public MutableSerializedString(Func<string> serialize) : base("placeholder") => this.serialize = serialize;

        protected override string GetUnderlyingValue(TokenStringOptions options) => serialize();
    }

    [Fact]
    public void ConstantDefaultsDoNotAllocateUnusedCaptureGraphs()
    {
        const int count = 128;
        LiteralToken token = new("value");
        ResolutionOptions options = new();
        string Run(bool record) => AnalysisExpressionEvaluator.ResolveVariables(token, '\\',
            static _ => String.Empty, syntaxOptions: options,
            recordTokenResolution: record ? static (_, _, _) =>
                throw new InvalidOperationException("A constant default has no captures.") : null);
        long Measure(bool record)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++) Assert.Equal("value", Run(record));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Run(false);
        Run(true);

        long baseline = Measure(false);
        long withCaptures = Measure(true);

        Assert.InRange(withCaptures, 0, baseline + 128 * count);
    }

    [Fact]
    public void ReferenceCollectionDoesNotAllocateAnalysisValues()
    {
        const int count = 2048;
        LiteralToken token = new(Enumerable.Range(0, count)
            .Select(_ => new VariableRefToken("X")), canContainVariables: true, '\\');
        string Run() => AnalysisExpressionEvaluator.ResolveVariables(token, '\\', static _ => "x");
        Run();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(new string('x', count), Run());

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 300L * count + 8192);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(2048)]
    public void CompletedSuffixChangesDoNotInvalidateEveryPendingCheck(int count)
    {
        long Measure(bool append)
        {
            LiteralToken token = new(Enumerable.Range(0, count)
                .Select(_ => new VariableRefToken("X")), canContainVariables: true, '\\');
            int version = 0;
            long before = 0;
            string Resolve(VariableRefToken variable)
            {
                if (version++ == 0)
                {
                    if (append) token.TokenList.Add(new StringToken("suffix"));
                    before = GC.GetAllocatedBytesForCurrentThread();
                }
                return "x";
            }
            Assert.Equal(new string('x', count), AnalysisExpressionEvaluator.ResolveVariables(token, '\\', Resolve,
                getCustomResolutionVersion: () => version));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Measure(false);
        Measure(true);

        long unchanged = Measure(false);
        long appended = Measure(true);

        Assert.InRange(appended, 0, unchanged + 8192);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativePrefixChangesRemainVisibleAfterSuffixChanges(bool append, bool replaceNative)
    {
        StringToken prefix = new("old");
        VariableRefToken left = new("LEFT");
        VariableRefToken right = new("RIGHT");
        LiteralToken token = new(new Token[] { prefix, left, right }, canContainVariables: true, '\\');
        int version = 0;
        string Resolve(VariableRefToken variable)
        {
            version++;
            if (ReferenceEquals(variable, right) && append) token.TokenList.Add(new StringToken("suffix"));
            if (ReferenceEquals(variable, left))
            {
                if (replaceNative) token.TokenList[0] = new StringToken("new");
                else prefix.Value = "new";
            }
            return variable.VariableName;
        }

        Assert.Equal("newLEFTRIGHT", AnalysisExpressionEvaluator.ResolveVariables(token, '\\', Resolve,
            getCustomResolutionVersion: () => version));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MixedNativePrefixesTrackRepeatedValueChanges(bool updateInline, bool mutate)
    {
        const int count = 512;
        StringToken prefix = new(".");
        LiteralToken token = new(Enumerable.Range(0, count).SelectMany(i => new Token[]
        {
            i == 0 ? prefix : new StringToken("."), new VariableRefToken("X")
        }), canContainVariables: true, '\\');
        int version = 0;
        HashSet<VariableRefToken> resolved = new();
        string Resolve(VariableRefToken variable)
        {
            Assert.True(resolved.Add(variable));
            version++;
            if (mutate && version <= 2) prefix.Value = version == 1 ? "new" : "newer";
            return "x";
        }

        Assert.Equal((mutate ? "newer" : ".") + "x" + String.Concat(Enumerable.Repeat(".x", count - 1)),
            AnalysisExpressionEvaluator.ResolveVariables(token, '\\', Resolve,
                getCustomResolutionVersion: () => version,
                syntaxOptions: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(count, resolved.Count);
    }

    [Fact]
    public async Task NativeValueMutationScopesOnlyTrackTheirOwnSources()
    {
        StringToken shared = new("old");
        StringToken innerOnly = new("inner");
        StringToken unrelated = new("other");
        using PrimitiveToken.ValueMutationScope outer = new();
        outer.Track(shared);
        PrimitiveToken.ValueMutationScope inner = new();
        ExecutionContext inherited;
        using (inner)
        {
            inner.Track(shared);
            inner.Track(innerOnly);
            inherited = ExecutionContext.Capture()!;
            unrelated.Value = "edit";
            string equalValue = new(shared.Value.ToCharArray());
            shared.Value = equalValue;
            Assert.Same(equalValue, shared.Value);
            Assert.Equal(0, outer.Version);
            Assert.Equal(0, inner.Version);
            Task mutation;
            using (ExecutionContext.SuppressFlow()) mutation = Task.Run(() => shared.Value = "new");
            await mutation;
            Assert.Equal(1, outer.Version);
            Assert.Equal(1, inner.Version);
            innerOnly.Value = "changed";
            Assert.Equal(1, outer.Version);
            Assert.Equal(2, inner.Version);
        }

        ExecutionContext.Run(inherited, _ => shared.Value = "after disposal", null);
        Assert.Equal(2, outer.Version);
        Assert.Equal(2, inner.Version);
    }

    [Fact]
    public async Task ConcurrentNativeValueSubscriptionsDetachIndependently()
    {
        StringToken source = new("old");
        PrimitiveToken.ValueMutationScope[] scopes = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            Task.Run(() =>
            {
                PrimitiveToken.ValueMutationScope scope = new();
                scope.Track(source);
                scope.Track(source);
                return scope;
            })));
        try
        {
            source.Value = "first";
            Assert.All(scopes, scope => Assert.Equal(1, scope.Version));
            for (int i = 0; i < scopes.Length; i += 2) scopes[i].Dispose();
            source.Value = "second";
            for (int i = 0; i < scopes.Length; i++)
                Assert.Equal(i % 2 == 0 ? 1 : 2, scopes[i].Version);
        }
        finally
        {
            foreach (PrimitiveToken.ValueMutationScope scope in scopes) scope.Dispose();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NestedResolutionsRetainOuterNativeValueTracking(bool worker, bool shared)
    {
        StringToken prefix = new("old");
        LiteralToken outer = new(new Token[] { prefix, new VariableRefToken("OUTER") }, true, '\\');
        LiteralToken inner = new(new Token[]
        {
            shared ? prefix : new StringToken("inner"), new VariableRefToken("INNER")
        }, true, '\\');
        int outerVersion = 0;
        int innerVersion = 0;
        string ResolveInner(VariableRefToken variable)
        {
            if (worker)
            {
                Task mutation;
                using (ExecutionContext.SuppressFlow()) mutation = Task.Run(() => prefix.Value = "new");
                mutation.GetAwaiter().GetResult();
            }
            else prefix.Value = "new";
            innerVersion++;
            return "i";
        }
        string ResolveOuter(VariableRefToken variable)
        {
            Assert.Equal(shared ? "newi" : "inneri", AnalysisExpressionEvaluator.ResolveVariables(inner, '\\',
                ResolveInner, getCustomResolutionVersion: () => innerVersion));
            outerVersion++;
            return "o";
        }

        Assert.Equal("newo", AnalysisExpressionEvaluator.ResolveVariables(outer, '\\', ResolveOuter,
            getCustomResolutionVersion: () => outerVersion));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedPrimitiveEditsPreserveMixedNativeDefaults(bool updateInline)
    {
        const int count = 512;
        StringToken unrelated = new("other");
        LiteralToken root = new(Enumerable.Range(0, count).SelectMany(_ => new Token[]
        {
            new StringToken("."), new VariableRefToken("X")
        }), true, '\\');
        int version = 0;
        string Resolve(VariableRefToken variable)
        {
            unrelated.Value = ++version % 2 == 0 ? "even" : "odd";
            return "x";
        }

        Assert.Equal(String.Concat(Enumerable.Repeat(".x", count)),
            AnalysisExpressionEvaluator.ResolveVariables(root, '\\', Resolve,
                getCustomResolutionVersion: () => version,
                syntaxOptions: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(count, version);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConsumedNativeAliasesDoNotHidePendingValueEdits(bool updateInline, bool nested)
    {
        StringToken shared = new("old");
        Token[] suffix = new Token[] { new VariableRefToken("X"), shared };
        LiteralToken root = new(nested
            ? new Token[] { shared, new LiteralToken(suffix, true, '\\') }
            : new[] { shared }.Concat(suffix), true, '\\');
        int version = 0;
        string Resolve(VariableRefToken variable)
        {
            shared.Value = "new";
            version++;
            return "x";
        }

        Assert.Equal("newxold", AnalysisExpressionEvaluator.ResolveVariables(root, '\\', Resolve,
            getCustomResolutionVersion: () => version,
            syntaxOptions: new ResolutionOptions { UpdateInline = updateInline }));
    }

    [Fact]
    public void NativeMutationSubscriptionsRemainActiveUntilEveryAliasRetires()
    {
        StringToken shared = new("old");
        using PrimitiveToken.ValueMutationScope scope = new();
        scope.Track(shared);
        scope.Track(shared);
        scope.Untrack(shared);
        shared.Value = "pending";
        Assert.Equal(1, scope.Version);

        scope.Untrack(shared);
        shared.Value = "consumed";
        Assert.Equal(1, scope.Version);

        scope.Track(shared);
        shared.Value = "tracked again";
        Assert.Equal(2, scope.Version);
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
