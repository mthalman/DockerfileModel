using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class ArgResolutionTests
{
    public static IEnumerable<object?[]> BindingCases()
    {
        (string Declarations, string Expression, string Expected, bool HasOverride, string? Override)[] cases =
        {
            ("ARG X=alpine\nARG X=busybox", "$X", "busybox", false, null),
            ("ARG X=alpine\nARG X=busybox", "$X", "debian", true, "debian"),
            ("ARG X=ok\nARG X", "$X", "ok", false, null),
            ("ARG X=ok\nARG X", "$X", "override", true, "override"),
            ("ARG X\nARG X", "${X-fallback}", "fallback", false, null),
            ("ARG X=ok\nARG X=", "${X-fallback}", "", false, null),
            ("ARG X=ok\nARG X=", "${X:-fallback}", "fallback", false, null),
            ("ARG X=\"\"", "${X:+wrong}", "", false, null),
            ("ARG X=''", "${X:+wrong}", "", false, null),
            ("ARG X=\"\"", "${X+set}", "set", false, null),
            ("ARG X=\"\"", "${X-fallback}", "", false, null),
            ("ARG X=\"\"", "${X:-fallback}", "fallback", false, null),
            ("ARG X=\"value\"", "$X", "value", false, null),
            ("ARG X=\"\"", "$X", "\"override\"", true, "\"override\""),
            ("ARG X=default", "${X-fallback}", "fallback", true, null),
            ("ARG X=default", "${X+set}", "", true, null),
            ("ARG X=default", "${X-fallback}", "", true, ""),
            ("ARG X=default", "${X+set}", "set", true, ""),
            ("ARG X=ok\nARG X=$X-next", "$X", "ok-next", false, null),
            ("ARG X=\"\"\nARG Y=${X:-fallback}\nARG Z=\"$Y-next\"", "$Z", "fallback-next", false, null),
            ("ARG X\nARG Y=${X-fallback}\nARG X=$Y", "$X", "fallback", false, null),
            ("ARG X=\"\"", "${X?required}", "", false, null),
            ("ARG X=ok", "${X:?required}", "ok", false, null),
            ("ARG X=\"\"", "${X:+right}", "right", true, "override")
        };

        foreach (var scenario in cases)
        {
            foreach (bool stageScope in new[] { false, true })
            {
                foreach (bool updateInline in new[] { false, true })
                {
                    foreach (bool targetInstruction in new[] { false, true })
                    {
                        yield return new object?[]
                        {
                            scenario.Declarations, scenario.Expression, scenario.Expected, scenario.HasOverride, scenario.Override,
                            stageScope, updateInline, targetInstruction
                        };
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BindingCases))]
    public void ResolveVariables_ArgBindings(
        string declarations, string expression, string expected, bool hasOverride, string? overrideValue,
        bool stageScope, bool updateInline, bool targetInstruction)
    {
        string text = stageScope
            ? $"FROM alpine\n{declarations}\nENV RESULT=prefix{expression}"
            : $"{declarations}\nFROM prefix{expression}";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        Instruction target = dockerfile.Items.OfType<Instruction>().Last();
        Dictionary<string, string?> overrides = new();
        if (hasOverride)
        {
            overrides["X"] = overrideValue;
        }
        ResolutionOptions options = new() { UpdateInline = updateInline };

        string result = targetInstruction
            ? dockerfile.ResolveVariables(target, overrides, options)
            : dockerfile.ResolveVariables(overrides, options);

        Assert.Equal(stageScope ? $"ENV RESULT=prefix{expected}" : $"FROM prefix{expected}", result);
        if (updateInline)
        {
            Assert.Equal(result, target.ToString());
        }
        else
        {
            Assert.Equal(text, dockerfile.ToString());
        }
        if (hasOverride)
        {
            Assert.Equal(overrideValue, overrides["X"]);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_StageImportsAndRedeclarations(bool updateInline, bool targetInstruction)
    {
        const string text = "ARG X=global\nFROM alpine\nARG X=local\nARG X\nENV RESULT=$X\n" +
            "FROM alpine\nARG Y=local\nARG Y=\nARG Y\nENV RESULT=${Y-fallback}/${Y:-empty}";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        EnvInstruction[] targets = dockerfile.Items.OfType<EnvInstruction>().ToArray();
        ResolutionOptions options = new() { UpdateInline = updateInline };

        if (targetInstruction)
        {
            Assert.Equal("ENV RESULT=global\n", dockerfile.ResolveVariables(targets[0], options: options));
            Assert.Equal("ENV RESULT=/empty", dockerfile.ResolveVariables(targets[1], options: options));
        }
        else
        {
            Assert.Equal("ENV RESULT=/empty", dockerfile.ResolveVariables(options: options));
        }
        if (!updateInline)
        {
            Assert.Equal(text, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false, false, "ARG X", "${X?required}")]
    [InlineData(false, true, "ARG X", "${X:?required}")]
    [InlineData(true, false, "ARG X=\"\"", "${X:?required}")]
    [InlineData(true, true, "ARG X=''", "${X:?required}")]
    public void ResolveVariables_UnsetOrEmptyRequiredArgThrows(
        bool stageScope, bool updateInline, string declaration, string expression)
    {
        string text = stageScope
            ? $"FROM alpine\n{declaration}\nENV RESULT={expression}"
            : $"{declaration}\nFROM alpine{expression}";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline)
        {
            Assert.Equal(text, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_NestedQuotedDefaultsPreserveSyntax(bool updateInline)
    {
        Dockerfile dockerfile = Dockerfile.Parse("ARG X=value\nARG Y=placeholder\nFROM $Y");
        ArgDeclaration declaration = dockerfile.Items.OfType<ArgInstruction>().Last().ArgTokens[0];
        LiteralToken quotedVariable = new("$X", canContainVariables: true) { QuoteChar = '"' };
        declaration.ValueToken = new LiteralToken(new Token[]
        {
            new LiteralToken("") { QuoteChar = '\'' },
            quotedVariable,
            new LiteralToken("") { QuoteChar = '"' },
            new StringToken("-suffix")
        }, canContainVariables: true, Dockerfile.DefaultEscapeChar);
        string original = dockerfile.ToString();

        Assert.Equal("FROM value-suffix", dockerfile.ResolveVariables(
            options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(updateInline ? "ARG Y=''\"value\"\"\"-suffix\n" : "ARG Y=''\"$X\"\"\"-suffix\n",
            dockerfile.Items.OfType<ArgInstruction>().Last().ToString());
        Assert.Equal('"', quotedVariable.QuoteChar);
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_OverridesDoNotRewriteQuotedDefaults(bool updateInline)
    {
        const string text = "ARG X=\"default\"\nFROM alpine\nARG X='stage'\nENV RESULT=$X";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        Assert.Equal("ENV RESULT=override", dockerfile.ResolveVariables(
            new Dictionary<string, string?> { ["X"] = "override" },
            new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("ARG X=\"default\"\n", dockerfile.Items.OfType<ArgInstruction>().First().ToString());
        Assert.Equal("ARG X='stage'\n", dockerfile.Items.OfType<ArgInstruction>().Last().ToString());
        if (!updateInline)
        {
            Assert.Equal(text, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_QuotedModifierDefaultRetainsEmptyBinding(bool quoteBearingToken)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "ARG X=${UNSET:-\"\"}\nFROM alpine AS first\nFROM alpine${X:+-wrong}");
        if (quoteBearingToken)
        {
            dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!.Tokens
                .OfType<VariableRefToken>().Single().ModifierValueToken = new LiteralToken("")
                {
                    QuoteChar = '"'
                };
        }
        FromInstruction[] stages = dockerfile.Items.OfType<FromInstruction>().ToArray();

        Assert.Equal("FROM alpine", dockerfile.ResolveVariables(stages[1]));
        Assert.Equal("FROM alpine AS first\n", dockerfile.ResolveVariables(
            stages[0], options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("ARG X=\"\"\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal("FROM alpine", dockerfile.ResolveVariables(stages[1]));
        Assert.Equal("FROM alpine", dockerfile.ResolveVariables(stages[1],
            options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("FROM alpine", Dockerfile.Parse(dockerfile.ToString()).ResolveVariables());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void ResolveVariables_ContinuedDefaultHasNoSemanticNewline(
        bool stageScope, bool updateInline, bool removeEscapes)
    {
        string text = stageScope
            ? "FROM alpine\nARG X=\"al\\\npine\"\nENV RESULT=$X"
            : "ARG X=\"al\\\npine\"\nFROM $X";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        Assert.Equal(stageScope ? "ENV RESULT=alpine" : "FROM alpine",
            dockerfile.ResolveVariables(options: new ResolutionOptions
            {
                UpdateInline = updateInline,
                RemoveEscapeCharacters = removeEscapes
            }));
        Assert.Equal("ARG X=\"al\\\npine\"\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        if (!updateInline)
        {
            Assert.Equal(text, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false, "\"quoted\"")]
    [InlineData(true, "\"quoted\"")]
    [InlineData(false, "$UNSET")]
    [InlineData(true, "$UNSET")]
    [InlineData(false, "\\path")]
    [InlineData(true, "\\path")]
    public void ResolveVariables_SubstitutedDefaultValuesAreNotReinterpreted(bool stageScope, string overrideValue)
    {
        string text = stageScope
            ? "FROM alpine\nARG X=default\nARG Y=$X\nENV RESULT=$Y"
            : "ARG X=default\nARG Y=$X\nFROM alpine AS first\nFROM prefix$Y";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        Dictionary<string, string?> overrides = new() { ["X"] = overrideValue };
        Instruction target = stageScope
            ? dockerfile.Items.OfType<ArgInstruction>().Last()
            : dockerfile.Items.OfType<FromInstruction>().First();

        dockerfile.ResolveVariables(target, overrides, new ResolutionOptions { UpdateInline = true });

        string expected = stageScope ? $"ENV RESULT={overrideValue}" : $"FROM prefix{overrideValue}";
        Assert.Equal(expected, dockerfile.ResolveVariables(overrides));
        Assert.Equal(expected, dockerfile.ResolveVariables(overrides, new ResolutionOptions { UpdateInline = true }));
    }

    [Fact]
    public void ResolveVariables_EditingSubstitutedDefaultDiscardsOldSemanticValue()
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "ARG X=${UNSET:-\"\"}\nFROM alpine AS first\nFROM prefix$X");
        FromInstruction[] stages = dockerfile.Items.OfType<FromInstruction>().ToArray();
        dockerfile.ResolveVariables(stages[0], options: new ResolutionOptions { UpdateInline = true });
        StringToken substituted = Assert.IsAssignableFrom<StringToken>(
            Assert.Single(dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!.Tokens));
        substituted.Value = "\"edited\"";

        Assert.Equal("FROM prefixedited", dockerfile.ResolveVariables(stages[1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_QuoteEditsInvalidateSubstitutedDefaultSemantics(bool nested)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "ARG X=${UNSET:-\"value\"}\nFROM alpine\nARG X\nENV RESULT=$X");
        FromInstruction from = dockerfile.Items.OfType<FromInstruction>().Single();
        dockerfile.ResolveVariables(from, options: new ResolutionOptions { UpdateInline = true });
        ArgDeclaration declaration = dockerfile.Items.OfType<ArgInstruction>().First().ArgTokens[0];
        LiteralToken value = declaration.ValueToken!;
        StringToken substituted = Assert.IsAssignableFrom<StringToken>(Assert.Single(value.Tokens));
        if (nested)
        {
            declaration.ValueToken = new LiteralToken(new Token[] { value },
                canContainVariables: true, Dockerfile.DefaultEscapeChar);
            value = declaration.ValueToken!;
        }
        value.QuoteChar = '\'';

        Assert.Same(substituted, Assert.Single(nested
            ? Assert.IsType<LiteralToken>(Assert.Single(value.Tokens)).Tokens
            : value.Tokens));
        Assert.Equal("ENV RESULT=\"value\"", dockerfile.ResolveVariables());
        Assert.Equal("ENV RESULT=\"value\"", dockerfile.ResolveVariables(
            options: new ResolutionOptions { UpdateInline = true }));
    }

    [Fact]
    public void ResolveVariables_UnchangedQuoteRetainsSubstitutedDefaultSemantics()
    {
        Dockerfile dockerfile = Dockerfile.Parse("ARG X=default\nARG Y=$X\nFROM alpine\nARG Y\nENV RESULT=$Y");
        Dictionary<string, string?> overrides = new() { ["X"] = "\"quoted\"" };
        dockerfile.ResolveVariables(dockerfile.Items.OfType<FromInstruction>().Single(),
            overrides, new ResolutionOptions { UpdateInline = true });
        LiteralToken value = dockerfile.Items.OfType<ArgInstruction>().ElementAt(1).ArgTokens[0].ValueToken!;
        value.QuoteChar = value.QuoteChar;

        Assert.Equal("ENV RESULT=\"quoted\"", dockerfile.ResolveVariables(overrides));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_MultipleDeclarationsRetainLeftToRightBindings(bool updateInline)
    {
        const string text = "FROM alpine\nARG X=first X=$X-next Y=$X\nENV RESULT=$Y";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        Assert.Equal("ENV RESULT=first-next", dockerfile.ResolveVariables(
            options: new ResolutionOptions { UpdateInline = updateInline }));
        if (!updateInline)
        {
            Assert.Equal(text, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData('\'', "\"quoted\"")]
    [InlineData('"', "quoted")]
    public void ResolveVariables_QuoteEditsDoNotInvalidateOtherOwners(char quote, string expectedAliasValue)
    {
        Dockerfile source = Dockerfile.Parse(
            "ARG X=default\nARG Y=$X\nFROM alpine\nARG Y\nENV RESULT=$Y");
        Dictionary<string, string?> overrides = new() { ["X"] = "\"quoted\"" };
        source.ResolveVariables(source.Items.OfType<FromInstruction>().Single(),
            overrides, new ResolutionOptions { UpdateInline = true });
        Token shared = Assert.Single(source.Items.OfType<ArgInstruction>()
            .ElementAt(1).ArgTokens[0].ValueToken!.Tokens);
        TokenBuilder builder = new();
        builder.Literal(contents => contents.Tokens.Add(shared), canContainVariables: true);
        LiteralToken alias = Assert.IsType<LiteralToken>(Assert.Single(builder.Tokens));
        Dockerfile other = Dockerfile.Parse("ARG Z=placeholder\nFROM alpine\nARG Z\nENV RESULT=$Z");
        other.Items.OfType<ArgInstruction>().First().ArgTokens[0].ValueToken = alias;
        string originalSource = source.ToString();
        Assert.Equal("ENV RESULT=\"quoted\"", source.ResolveVariables(overrides));

        alias.QuoteChar = quote;

        Assert.Same(shared, Assert.Single(alias.Tokens));
        Assert.Equal(originalSource, source.ToString());
        Assert.Equal("ENV RESULT=\"quoted\"", source.ResolveVariables(overrides));
        Assert.Equal($"ENV RESULT={expectedAliasValue}", other.ResolveVariables());
        Assert.Equal("ENV RESULT=\"quoted\"", source.ResolveVariables(overrides));

        alias.QuoteChar = null;

        Assert.Equal("ENV RESULT=\"quoted\"", other.ResolveVariables());
        Assert.Equal("ENV RESULT=\"quoted\"", source.ResolveVariables(overrides));
    }

    [Theory]
    [InlineData('\'', "\"value\"")]
    [InlineData('"', "value")]
    public void ResolveVariables_ReembeddedSubstitutionUsesCurrentLexicalContext(char quote, string expected)
    {
        Dockerfile source = Dockerfile.Parse(
            "ARG X=${UNSET:-\"value\"}\nFROM alpine\nARG X\nENV RESULT=$X");
        source.ResolveVariables(source.Items.OfType<FromInstruction>().Single(),
            options: new ResolutionOptions { UpdateInline = true });
        Token shared = Assert.Single(source.Items.OfType<ArgInstruction>()
            .First().ArgTokens[0].ValueToken!.Tokens);
        TokenBuilder builder = new();
        builder.Literal(contents =>
        {
            contents.Tokens.Add(new StringToken(quote.ToString()));
            contents.Tokens.Add(shared);
            contents.Tokens.Add(new StringToken(quote.ToString()));
        }, canContainVariables: true);
        Dockerfile other = Dockerfile.Parse("ARG Z=placeholder\nFROM alpine\nARG Z\nENV RESULT=$Z");
        other.Items.OfType<ArgInstruction>().First().ArgTokens[0].ValueToken =
            Assert.IsType<LiteralToken>(Assert.Single(builder.Tokens));

        Assert.Equal($"ENV RESULT={expected}", other.ResolveVariables());
        Assert.Equal("ENV RESULT=value", source.ResolveVariables());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_DefaultContextPreservesSuppressedReferences(bool stageScope, bool updateInline)
    {
        string text = stageScope
            ? "FROM alpine\nARG X='${UNSET?boom}'\nENV RESULT=$X"
            : "ARG X='${UNSET?boom}'\nFROM alpine\nARG X\nENV RESULT=$X";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        Assert.Equal("ENV RESULT=${UNSET?boom}", dockerfile.ResolveVariables(
            options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal("ARG X='${UNSET?boom}'\n", dockerfile.Items.OfType<ArgInstruction>().First().ToString());
        if (!updateInline)
        {
            Assert.Equal(text, dockerfile.ToString());
        }
    }

    [Fact]
    public void ResolveVariables_ReembeddedQuotedSubstitutionDoesNotUseItsOriginalContext()
    {
        Dockerfile source = Dockerfile.Parse(
            "ARG X=default\nARG Y=\"$X\"\nFROM alpine\nARG Y\nENV RESULT=$Y");
        Dictionary<string, string?> overrides = new() { ["X"] = "\"quoted\"" };
        source.ResolveVariables(source.Items.OfType<FromInstruction>().Single(),
            overrides, new ResolutionOptions { UpdateInline = true });
        Token shared = Assert.Single(source.Items.OfType<ArgInstruction>()
            .ElementAt(1).ArgTokens[0].ValueToken!.Tokens);
        TokenBuilder builder = new();
        builder.Literal(contents => contents.Tokens.Add(shared), canContainVariables: true);
        Dockerfile other = Dockerfile.Parse("ARG Z=placeholder\nFROM alpine\nARG Z\nENV RESULT=$Z");
        other.Items.OfType<ArgInstruction>().First().ArgTokens[0].ValueToken =
            Assert.IsType<LiteralToken>(Assert.Single(builder.Tokens));

        Assert.Equal("ENV RESULT=\"quoted\"", source.ResolveVariables(overrides));
        Assert.Equal("ENV RESULT=quoted", other.ResolveVariables());
        Assert.Equal("ENV RESULT=\"quoted\"", source.ResolveVariables(overrides));
    }

    [Theory]
    [InlineData(false, false, "${A?left}${B?right}")]
    [InlineData(false, true, "${A?left}${B?right}")]
    [InlineData(true, false, "${A?left}${B?right}")]
    [InlineData(true, true, "${A?left}${B?right}")]
    [InlineData(false, false, "${UNSET:-${A?left}${B?right}}")]
    [InlineData(false, true, "${UNSET:-${A?left}${B?right}}")]
    [InlineData(true, false, "${UNSET:-${A?left}${B?right}}")]
    [InlineData(true, true, "${UNSET:-${A?left}${B?right}}")]
    public void ResolveVariables_ArgDefaultsPreserveRightmostRequiredError(
        bool stageScope, bool updateInline, string expression)
    {
        string text = stageScope
            ? $"FROM alpine\nARG X={expression}\nENV RESULT=$X"
            : $"ARG X={expression}\nFROM prefix$X";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal("Variable 'B' is not set. Error detail: 'right'.", error.Message);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Theory]
    [InlineData(false, false, 8)]
    [InlineData(false, true, 8)]
    [InlineData(true, false, 8)]
    [InlineData(true, true, 8)]
    [InlineData(false, false, 16)]
    [InlineData(false, true, 16)]
    [InlineData(true, false, 16)]
    [InlineData(true, true, 16)]
    [InlineData(false, false, 32)]
    [InlineData(false, true, 32)]
    [InlineData(true, false, 32)]
    [InlineData(true, true, 32)]
    public void ResolveVariables_NestedArgDefaultsRequireLinearEvaluations(
        bool stageScope, bool updateInline, int depth)
    {
        List<CountingVariableRefToken> variables = new();
        LiteralToken value = new("end", canContainVariables: true);
        for (int i = 0; i < depth; i++)
        {
            CountingVariableRefToken variable = new() { ModifierValueToken = value };
            variables.Add(variable);
            TokenBuilder builder = new();
            builder.Literal(contents => contents.Tokens.Add(variable), canContainVariables: true);
            value = Assert.IsType<LiteralToken>(Assert.Single(builder.Tokens));
        }
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : "ARG X=placeholder\nFROM prefix$X");
        dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken = value;
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=end" : "FROM prefixend",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.InRange(variables.Sum(variable => variable.ResolutionCount), depth, 2 * depth);
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    private sealed class CountingVariableRefToken : VariableRefToken
    {
        public CountingVariableRefToken() : base("UNSET", ":-", "end") { }

        public int ResolutionCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            return base.ResolveVariables(escapeChar, variables, options);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CustomVariableIsResolvedOnce(bool stageScope, bool updateInline)
    {
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : "ARG X=placeholder\nFROM prefix$X");
        ChangingVariableRefToken variable = new();
        TokenBuilder builder = new();
        builder.Literal(contents => contents.Tokens.Add(variable), canContainVariables: true);
        dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken =
            Assert.IsType<LiteralToken>(Assert.Single(builder.Tokens));
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=first" : "FROM prefixfirst",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal(1, variable.ResolutionCount);
        Assert.Equal(updateInline, variable.UpdateInline);
        Assert.Equal(updateInline ? "ARG X=first\n" : "ARG X=$UNSET\n",
            dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void ResolveVariables_CustomLiteralSuppliesArgBinding(
        bool stageScope, bool updateInline, bool nested)
    {
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : "ARG X=placeholder\nFROM prefix$X");
        ComputingLiteralToken literal = new();
        LiteralToken value = literal;
        if (nested)
        {
            TokenBuilder builder = new();
            builder.Literal(contents => contents.Tokens.Add(literal), canContainVariables: true);
            value = Assert.IsType<LiteralToken>(Assert.Single(builder.Tokens));
        }
        dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken = value;
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=computed" : "FROM prefixcomputed",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal(1, literal.ResolutionCount);
        Assert.Equal(updateInline, literal.UpdateInline);
        Assert.Equal(updateInline ? "ARG X=computed\n" : "ARG X=placeholder\n",
            dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    private sealed class ChangingVariableRefToken : VariableRefToken
    {
        public ChangingVariableRefToken() : base("UNSET") { }

        public int ResolutionCount { get; private set; }

        public bool UpdateInline { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            UpdateInline = options?.UpdateInline ?? false;
            return ++ResolutionCount == 1 ? "first" : "second";
        }
    }

    private sealed class ComputingLiteralToken : LiteralToken
    {
        private readonly string? result;

        public ComputingLiteralToken(string? result = "computed") : base("placeholder", canContainVariables: true)
        {
            this.result = result;
        }

        public int ResolutionCount { get; private set; }

        public bool UpdateInline { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            UpdateInline = options?.UpdateInline ?? false;
            if (UpdateInline)
            {
                Value = result ?? String.Empty;
            }
            return result;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CustomModifierLiteralPreservesReturnedData(
        bool stageScope, bool updateInline)
    {
        const string result = "\"$UNSET\\path\"";
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=${UNSET:-placeholder}\nENV RESULT=$X"
            : "ARG X=${UNSET:-placeholder}\nFROM prefix$X");
        ComputingLiteralToken literal = new(result);
        dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!.Tokens
            .OfType<VariableRefToken>().Single().ModifierValueToken = literal;
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? $"ENV RESULT={result}" : $"FROM prefix{result}",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal(1, literal.ResolutionCount);
        Assert.Equal(updateInline, literal.UpdateInline);
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CustomLiteralNullResultIsAnExplicitEmptyDefault(
        bool stageScope, bool updateInline)
    {
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=placeholder\nENV RESULT=${X+set}/${X:-empty}"
            : "ARG X=placeholder\nFROM prefix${X+set}-${X:-empty}");
        ComputingLiteralToken literal = new(null);
        dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken = literal;
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=set/empty" : "FROM prefixset-empty",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal(1, literal.ResolutionCount);
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_SharedCustomModifierKeepsOccurrenceResults(bool stageScope, bool updateInline)
    {
        ChangingModifierLiteral shared = new();
        VariableRefToken left = new("UNSET", ":-", "placeholder") { ModifierValueToken = shared };
        VariableRefToken right = new("UNSET", ":-", "placeholder") { ModifierValueToken = shared };
        Dockerfile dockerfile = CreateCustomDefault(stageScope, left, right);
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=secondfirst" : "FROM prefixsecondfirst",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal(2, shared.ResolutionCount);
        Assert.Equal(updateInline ? "ARG X=secondfirst\n" : "ARG X=${UNSET:-placeholder}${UNSET:-placeholder}\n",
            dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    [Theory]
    [InlineData(false, false, "${A:-a}${B:-b}", "abright")]
    [InlineData(false, true, "${A:-a}${B:-b}", "abright")]
    [InlineData(true, false, "${A:-a}${B:-b}", "abright")]
    [InlineData(true, true, "${A:-a}${B:-b}", "abright")]
    [InlineData(false, false, "edited", "editedright")]
    [InlineData(false, true, "edited", "editedright")]
    [InlineData(true, false, "edited", "editedright")]
    [InlineData(true, true, "edited", "editedright")]
    public void ResolveVariables_CustomResolverCanEditPendingLiteral(
        bool stageScope, bool updateInline, string edit, string expected)
    {
        LiteralToken left = new("$LEFT", canContainVariables: true);
        EditingVariableRefToken right = new(left, edit);
        Dockerfile dockerfile = CreateCustomDefault(stageScope, left, right);

        Assert.Equal(stageScope ? $"ENV RESULT={expected}" : $"FROM prefix{expected}",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal(1, right.ResolutionCount);
        Assert.Equal(updateInline ? $"ARG X={expected}\n" : $"ARG X={edit}$RIGHT\n",
            dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_CustomResolverEditsRetainRightmostErrors(bool updateInline)
    {
        LiteralToken left = new("$LEFT", canContainVariables: true);
        EditingVariableRefToken right = new(left, "${A?left}${B?right}");
        Dockerfile dockerfile = CreateCustomDefault(false, left, right);

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal("Variable 'B' is not set. Error detail: 'right'.", error.Message);
        Assert.Equal(1, right.ResolutionCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CustomModifierCanEditPendingLiteral(bool stageScope, bool updateInline)
    {
        LiteralToken left = new("$LEFT", canContainVariables: true);
        EditingModifierLiteral modifier = new(left);
        VariableRefToken right = new("UNSET", ":-", "placeholder") { ModifierValueToken = modifier };
        Dockerfile dockerfile = CreateCustomDefault(stageScope, left, right);

        Assert.Equal(stageScope ? "ENV RESULT=abright" : "FROM prefixabright",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal(1, modifier.ResolutionCount);
        Assert.Equal(updateInline ? "ARG X=abright\n" : "ARG X=${A:-a}${B:-b}${UNSET:-placeholder}\n",
            dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CustomPrimitiveUsesSerializedText(bool stageScope, bool updateInline)
    {
        Dockerfile dockerfile = CreateCustomDefault(stageScope, new SerializedStringToken());
        string original = dockerfile.ToString();

        Assert.Equal(stageScope ? "ENV RESULT=computed" : "FROM prefixcomputed",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal("ARG X=computed\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        if (!updateInline)
        {
            Assert.Equal(original, dockerfile.ToString());
        }
    }

    private static Dockerfile CreateCustomDefault(bool stageScope, params Token[] tokens)
    {
        Dockerfile dockerfile = Dockerfile.Parse(stageScope
            ? "FROM alpine\nARG X=placeholder\nENV RESULT=$X"
            : "ARG X=placeholder\nFROM prefix$X");
        dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken =
            new LiteralToken(tokens, canContainVariables: true, Dockerfile.DefaultEscapeChar);
        return dockerfile;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CompletedSuffixCanGainCapturedCustomLiteralAlias(bool stageScope, bool updateInline)
    {
        ComputingLiteralToken shared = new();
        LiteralToken right = new(new[] { shared }, canContainVariables: true, Dockerfile.DefaultEscapeChar);
        CallbackVariableRefToken left = new(() => right.TokenList.Add(shared));
        Dockerfile dockerfile = CreateCustomDefault(stageScope, left, right);

        Assert.Equal(stageScope ? "ENV RESULT=leftcomputed" : "FROM prefixleftcomputed",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(1, shared.ResolutionCount);
        Assert.Equal(2, right.TokenList.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CompletedSuffixCanGainCapturedVariableAlias(bool stageScope, bool updateInline)
    {
        VariableRefToken shared = new("UNSET", ":-", "old");
        LiteralToken right = new(new[] { shared }, canContainVariables: true, Dockerfile.DefaultEscapeChar);
        CallbackVariableRefToken left = new(() => right.TokenList.Add(shared));
        Dockerfile dockerfile = CreateCustomDefault(stageScope, left, right);

        Assert.Equal(stageScope ? "ENV RESULT=leftold" : "FROM prefixleftold",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        if (updateInline)
        {
            Assert.IsType<ResolvedStringToken>(right.TokenList[0]);
            Assert.Same(shared, right.TokenList[1]);
            Assert.Equal("$old", shared.ToString());
            Assert.Equal("ARG X=leftold$old\n",
                dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_CompletedSuffixCustomVariableAliasRetainsOccurrenceIdentity(bool stageScope)
    {
        ChangingVariableRefToken shared = new();
        LiteralToken right = new(new[] { shared }, canContainVariables: true, Dockerfile.DefaultEscapeChar);
        CallbackVariableRefToken left = new(() => right.TokenList.Add(shared));
        Dockerfile dockerfile = CreateCustomDefault(stageScope, left, right);

        Assert.Equal(stageScope ? "ENV RESULT=leftfirst" : "FROM prefixleftfirst",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(1, shared.ResolutionCount);
        Assert.IsType<ResolvedStringToken>(right.TokenList[0]);
        Assert.Same(shared, right.TokenList[1]);
        Assert.Equal("ARG X=leftfirst$UNSET\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
    }

    [Theory]
    [InlineData(false, "\"old\"")]
    [InlineData(true, "\"old\"")]
    [InlineData(false, "\"${B:-old}\"")]
    [InlineData(true, "\"${B:-old}\"")]
    public void ResolveVariables_InlineReplayPreservesCompletedModifierOperand(bool stageScope, string expression)
    {
        LiteralToken operand = new(expression, canContainVariables: true);
        VariableRefToken right = new("UNSET", ":-", "placeholder") { ModifierValueToken = operand };
        CallbackVariableRefToken left = new(() => operand.Value = "new");
        Dockerfile dockerfile = CreateCustomDefault(stageScope, left, right);

        Assert.Equal(stageScope ? "ENV RESULT=leftold" : "FROM prefixleftold",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("ARG X=left\"old\"\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal("new", operand.Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CustomDefaultReceivesOriginalOptions(bool stageScope, bool updateInline)
    {
        ContextResolutionOptions options = new() { UpdateInline = updateInline };
        OptionsCheckingVariable variable = new(options);
        Dockerfile dockerfile = CreateCustomDefault(stageScope, variable);

        Assert.Equal(stageScope ? "ENV RESULT=context" : "FROM prefixcontext",
            dockerfile.ResolveVariables(options: options));
        Assert.Equal(1, variable.ResolutionCount);
        Assert.Equal(updateInline, options.UpdateInline);
        Assert.Null(options.ModifierValueResolver);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CustomLiteralReceivesOriginalOptions(bool stageScope, bool updateInline)
    {
        ContextResolutionOptions options = new() { UpdateInline = updateInline };
        OptionsCheckingLiteral literal = new(options);
        Dockerfile dockerfile = CreateCustomDefault(stageScope, literal);

        Assert.Equal(stageScope ? "ENV RESULT=context" : "FROM prefixcontext",
            dockerfile.ResolveVariables(options: options));
        Assert.Equal(1, literal.ResolutionCount);
        Assert.Equal(updateInline, options.UpdateInline);
        Assert.Null(options.ModifierValueResolver);
    }

    [Fact]
    public void ResolveVariables_CustomResolutionRestoresOptionsScopeAfterException()
    {
        ContextResolutionOptions options = new() { UpdateInline = true };
        InvalidOperationException expected = new("Custom resolution.");
        OptionsDelegatingVariable variable = new(options, () => throw expected);
        Dockerfile dockerfile = CreateCustomDefault(false, variable);

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            dockerfile.ResolveVariables(options: options)));
        Assert.Null(options.ModifierValueResolver);
        Assert.True(options.UpdateInline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveVariables_SharedOptionsIsolateConcurrentModifierScopes(bool updateInline)
    {
        using Barrier barrier = new(2);
        ContextResolutionOptions options = new() { UpdateInline = updateInline };
        Task<string> Resolve(string binding) => Task.Factory.StartNew(() =>
        {
            Dockerfile dockerfile = Dockerfile.Parse($"ARG VALUE={binding}\nARG X=placeholder\nFROM prefix$X");
            dockerfile.Items.OfType<ArgInstruction>().Last().ArgTokens[0].ValueToken =
                new LiteralToken(new[]
                {
                    new OptionsDelegatingVariable(options, () =>
                        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30))))
                }, canContainVariables: true, Dockerfile.DefaultEscapeChar);
            return dockerfile.ResolveVariables(options: options);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Task<string> first = Resolve("one");
        Task<string> second = Resolve("two");
        string[] results = await Task.WhenAll(first, second);

        Assert.Equal("FROM prefixone", results[0]);
        Assert.Equal("FROM prefixtwo", results[1]);
        Assert.Null(options.ModifierValueResolver);
        Assert.Equal(updateInline, options.UpdateInline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_ReentrantCustomResolutionRestoresOuterModifierScope(bool updateInline)
    {
        ContextResolutionOptions options = new() { UpdateInline = updateInline };
        Dockerfile inner = Dockerfile.Parse("ARG VALUE=inner\nARG X=${UNSET:-$VALUE}\nFROM prefix$X");
        Dockerfile outer = Dockerfile.Parse("ARG VALUE=outer\nARG X=placeholder\nFROM prefix$X");
        outer.Items.OfType<ArgInstruction>().Last().ArgTokens[0].ValueToken =
            new LiteralToken(new[]
            {
                new OptionsDelegatingVariable(options, () =>
                    Assert.Equal("FROM prefixinner", inner.ResolveVariables(options: options)))
            }, canContainVariables: true, Dockerfile.DefaultEscapeChar);

        Assert.Equal("FROM prefixouter", outer.ResolveVariables(options: options));
        Assert.Null(options.ModifierValueResolver);
        Assert.Equal(updateInline, options.UpdateInline);
    }

    [Fact]
    public void ResolveVariables_StageQuoteGetterPreservesRightmostRequiredError()
    {
        Dockerfile dockerfile = CreateCustomDefault(true,
            new ThrowingQuoteStringToken(), new VariableRefToken("UNSET", "?", "right"));

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));

        Assert.Equal("Variable 'UNSET' is not set. Error detail: 'right'.", error.Message);
    }

    [Fact]
    public void ResolveVariables_StageQuoteGetterFailureIsRethrownUnchanged()
    {
        InvalidOperationException expected = new("Quote getter.");
        Dockerfile dockerfile = CreateCustomDefault(true,
            new ThrowingQuoteStringToken(expected), new VariableRefToken("R", ":-", "right"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));

        Assert.Same(expected, error);
        Assert.Contains(nameof(ThrowingQuoteStringToken), error.StackTrace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_CallbackQuoteGetterFailurePreservesPendingRequiredError(bool throwOnce)
    {
        bool fail = false;
        ConditionalQuoteStringToken primitive = new(() =>
        {
            if (fail)
            {
                if (throwOnce) fail = false;
                throw new InvalidOperationException("Pending quote.");
            }
            return null;
        });
        Dockerfile dockerfile = CreateCustomDefault(true, primitive,
            new VariableRefToken("UNSET", "?", "left"), new CallbackVariableRefToken(() => fail = true));

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));

        Assert.Equal("Variable 'UNSET' is not set. Error detail: 'left'.", error.Message);
    }

    [Fact]
    public void ResolveVariables_CallbackQuoteGetterFailureIsNotLostWhenGetterRecovers()
    {
        bool fail = false;
        InvalidOperationException expected = new("Pending quote.");
        ConditionalQuoteStringToken primitive = new(() =>
        {
            if (fail)
            {
                fail = false;
                throw expected;
            }
            return null;
        });
        Dockerfile dockerfile = CreateCustomDefault(true,
            primitive, new CallbackVariableRefToken(() => fail = true));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));

        Assert.Same(expected, error);
        Assert.Contains(nameof(ConditionalQuoteStringToken), error.StackTrace);
    }

    [Fact]
    public void ResolveVariables_ConstantStageDefaultsAllocateLinearly()
    {
        static long Measure(int count)
        {
            Dockerfile dockerfile = Dockerfile.Parse("FROM alpine\n" +
                String.Concat(Enumerable.Range(0, count).Select(i => $"ARG X{i:D4}=value\n")) +
                "ENV RESULT=ok");
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal("ENV RESULT=ok", dockerfile.ResolveVariables());
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Measure(32);
        long small = Measure(512);
        long large = Measure(1024);
        Assert.InRange(large, 0, 2 * small + 2_000_000);
    }

    private sealed class ContextResolutionOptions : ResolutionOptions
    {
        public string Context { get; } = "context";
    }

    private sealed class OptionsCheckingVariable : VariableRefToken
    {
        private readonly ContextResolutionOptions expected;

        public OptionsCheckingVariable(ContextResolutionOptions expected) : base("CUSTOM")
        {
            this.expected = expected;
        }

        public int ResolutionCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            Assert.Same(expected, options);
            return expected.Context;
        }
    }

    private sealed class OptionsCheckingLiteral : LiteralToken
    {
        private readonly ContextResolutionOptions expected;

        public OptionsCheckingLiteral(ContextResolutionOptions expected) : base("placeholder")
        {
            this.expected = expected;
        }

        public int ResolutionCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            Assert.Same(expected, options);
            return expected.Context;
        }
    }

    private sealed class OptionsDelegatingVariable : VariableRefToken
    {
        private readonly ContextResolutionOptions expected;
        private readonly Action beforeResolution;

        public OptionsDelegatingVariable(ContextResolutionOptions expected, Action beforeResolution)
            : base("UNSET", ":-", "$VALUE")
        {
            this.expected = expected;
            this.beforeResolution = beforeResolution;
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            Assert.Same(expected, options);
            beforeResolution();
            return base.ResolveVariables(escapeChar, variables, options);
        }
    }

    private sealed class ThrowingQuoteStringToken : StringToken, IQuotableToken
    {
        private readonly Exception error;

        public ThrowingQuoteStringToken(Exception? error = null) : base("p")
        {
            this.error = error ?? new InvalidOperationException("Quote getter.");
        }

        public char? QuoteChar
        {
            get => throw error;
            set { }
        }
    }

    private sealed class ConditionalQuoteStringToken : StringToken, IQuotableToken
    {
        private readonly Func<char?> getQuote;

        public ConditionalQuoteStringToken(Func<char?> getQuote) : base("p")
        {
            this.getQuote = getQuote;
        }

        public char? QuoteChar
        {
            get => getQuote();
            set { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_InlineReplayUsesCapturedBindings(bool stageScope)
    {
        EnvironmentEditingVariable left = new();
        Dockerfile dockerfile = CreateCustomDefault(stageScope,
            left, new VariableRefToken("B", ":-", "\"old\""));

        Assert.Equal(stageScope ? "ENV RESULT=leftold" : "FROM prefixleftold",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("ARG X=left\"old\"\n", dockerfile.Items.OfType<ArgInstruction>().Single().ToString());
        Assert.Equal(1, left.ResolutionCount);
    }

    [Theory]
    [InlineData("$B", "old")]
    [InlineData("${B:-fallback}", "old")]
    [InlineData("${B:+\"present\"}", "\"present\"")]
    [InlineData("${B?required}", "old")]
    public void ResolveVariables_InlineReplayRetainsSetBindingAndModifierBranch(string expression, string syntax)
    {
        foreach (bool stageScope in new[] { false, true })
        {
            foreach (bool updateInline in new[] { false, true })
            {
                Dockerfile dockerfile = Dockerfile.Parse(stageScope
                    ? "FROM alpine\nARG B=old\nARG X=placeholder\nENV RESULT=$X"
                    : "ARG B=old\nARG X=placeholder\nFROM prefix$X");
                EnvironmentEditingVariable left = new(removeBinding: true);
                dockerfile.Items.OfType<ArgInstruction>().Last().ArgTokens[0].ValueToken =
                    new LiteralToken(new Token[] { left, VariableRefToken.Parse(expression) },
                        canContainVariables: true, Dockerfile.DefaultEscapeChar);
                string original = dockerfile.ToString();

                Assert.Equal(stageScope ? $"ENV RESULT=left{syntax.Trim('\"')}" : $"FROM prefixleft{syntax.Trim('\"')}",
                    dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
                Assert.Equal(1, left.ResolutionCount);
                if (updateInline)
                {
                    Assert.Equal($"ARG X=left{syntax}\n", dockerfile.Items.OfType<ArgInstruction>().Last().ToString());
                }
                else
                {
                    Assert.Equal(original, dockerfile.ToString());
                }
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CallbackQuoteEditsKeepBalancedBoundaries(bool stageScope, bool updateInline)
    {
        LiteralToken? root = null;
        CallbackVariableRefToken left = new(() => root!.QuoteChar = null);
        Dockerfile dockerfile = CreateCustomDefault(stageScope, left, new VariableRefToken("R", ":-", "r"));
        root = dockerfile.Items.OfType<ArgInstruction>().Single().ArgTokens[0].ValueToken!;
        root.QuoteChar = '"';

        Assert.Equal(stageScope ? "ENV RESULT=leftr" : "FROM prefixleftr",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Null(root.QuoteChar);
    }

    [Fact]
    public void ResolveVariables_InactiveModifierDoesNotReadCustomQuoteGetter()
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "FROM alpine\nARG SET=set\nARG X=placeholder\nENV RESULT=$X");
        VariableRefToken variable = new("SET", ":-", "placeholder")
        {
            ModifierValueToken = new ThrowingQuoteLiteral()
        };
        dockerfile.Items.OfType<ArgInstruction>().Last().ArgTokens[0].ValueToken =
            new LiteralToken(new[] { variable }, canContainVariables: true, Dockerfile.DefaultEscapeChar);

        Assert.Equal("ENV RESULT=set",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_GlobalDefaultSerializationPreservesRightmostError(
        bool updateInline, bool formatError)
    {
        Dockerfile dockerfile = CreateCustomDefault(false,
            new ThrowingPrimitive(formatError ? new FormatException("Bad serialization.") : null),
            new VariableRefToken("UNSET", "?", "right"));

        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Equal("Variable 'UNSET' is not set. Error detail: 'right'.", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_DeferredSerializerFailureIsRethrownUnchanged(bool updateInline)
    {
        FormatException expected = new("Bad serialization.");
        Dockerfile dockerfile = CreateCustomDefault(false,
            new ThrowingPrimitive(expected), new VariableRefToken("R", ":-", "right"));

        FormatException error = Assert.Throws<FormatException>(() =>
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));

        Assert.Same(expected, error);
        Assert.Contains(nameof(ThrowingPrimitive), error.StackTrace);
    }

    private sealed class EnvironmentEditingVariable : VariableRefToken
    {
        private readonly bool removeBinding;

        public EnvironmentEditingVariable(bool removeBinding = false) : base("LEFT")
        {
            this.removeBinding = removeBinding;
        }

        public int ResolutionCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            if (removeBinding)
            {
                variables!.Remove("B");
            }
            else
            {
                variables!["B"] = "new";
            }
            return "left";
        }
    }

    private sealed class ThrowingQuoteLiteral : LiteralToken
    {
        public ThrowingQuoteLiteral() : base("placeholder", canContainVariables: true) { }

        public override char? QuoteChar
        {
            get => throw new InvalidOperationException("Inactive quote getter.");
            set { }
        }
    }

    private sealed class ThrowingPrimitive : StringToken
    {
        private readonly Exception error;

        public ThrowingPrimitive(Exception? error = null) : base("p")
        {
            this.error = error ?? new InvalidOperationException("Primitive serialization.");
        }

        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            options.ExcludeQuotes ? throw error : "p";
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void ResolveVariables_NestedDefaultsDoNotSnapshotInactiveDescendants(int depth)
    {
        CountingTerminalLiteral terminal = new();
        LiteralToken value = terminal;
        for (int i = 0; i < depth; i++)
        {
            value = new LiteralToken(new[]
            {
                new VariableRefToken("UNSET", ":-", "end") { ModifierValueToken = value }
            }, canContainVariables: true, Dockerfile.DefaultEscapeChar);
        }
        Dockerfile dockerfile = CreateCustomDefault(true, value);
        terminal.QuoteReads = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal("ENV RESULT=end",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(terminal.QuoteReads, 0, 4);
        Assert.InRange(allocated, 0, 8192L * depth);
    }

    private sealed class CountingTerminalLiteral : LiteralToken
    {
        public CountingTerminalLiteral() : base("end", canContainVariables: true) { }

        public int QuoteReads { get; set; }

        public override char? QuoteChar
        {
            get { QuoteReads++; return base.QuoteChar; }
            set => base.QuoteChar = value;
        }
    }

    [Theory]
    [InlineData(false, false, "changed")]
    [InlineData(false, true, "changed")]
    [InlineData(true, false, "changed")]
    [InlineData(true, true, "changed")]
    [InlineData(false, false, "${UNSET?changed}")]
    [InlineData(false, true, "${UNSET?changed}")]
    [InlineData(true, false, "${UNSET?changed}")]
    [InlineData(true, true, "${UNSET?changed}")]
    public void ResolveVariables_CustomResolverCannotRewriteCompletedSuffix(
        bool stageScope, bool updateInline, string edit)
    {
        LiteralToken right = new("${R:-r}", canContainVariables: true);
        EditingVariableRefToken left = new(right, edit);
        Dockerfile dockerfile = CreateCustomDefault(stageScope, left, right);

        Assert.Equal(stageScope ? "ENV RESULT=rightr" : "FROM prefixrightr",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(1, left.ResolutionCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CustomLiteralInsertedIntoCompletedSuffixIsNotResolved(
        bool stageScope, bool updateInline)
    {
        LiteralToken right = new("${R:-r}", canContainVariables: true);
        ComputingLiteralToken inserted = new("wrong");
        CallbackVariableRefToken left = new(() =>
        {
            right.TokenList.Clear();
            right.TokenList.Add(inserted);
        });
        Dockerfile dockerfile = CreateCustomDefault(stageScope, left, right);

        Assert.Equal(stageScope ? "ENV RESULT=leftr" : "FROM prefixleftr",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(0, inserted.ResolutionCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ResolveVariables_CustomPrimitiveSerializationIsCapturedOnce(bool stageScope, bool updateInline)
    {
        ChangingSerializedStringToken primitive = new();
        Dockerfile dockerfile = CreateCustomDefault(stageScope,
            primitive, new VariableRefToken("R", ":-", "r"), new StringToken("'"));

        Assert.Equal(stageScope ? "ENV RESULT=${R:-r}" : "FROM prefix${R:-r}",
            dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(updateInline ? 3 : 2, primitive.SerializationCount);
    }

    [Fact]
    public void ResolveVariables_CustomCallbacksRecheckPendingSerializersLinearly()
    {
        const int count = 256;
        int serializations = 0;
        Token[] tokens = Enumerable.Range(0, count)
            .SelectMany(_ => new Token[]
            {
                new CountingSerializedStringToken(() => serializations++),
                new ChangingVariableRefToken()
            }).ToArray();
        Dockerfile dockerfile = CreateCustomDefault(false, tokens);

        Assert.Equal("FROM prefix" + String.Concat(Enumerable.Repeat("pfirst", count)),
            dockerfile.ResolveVariables());
        Assert.Equal(3 * count, serializations);
    }

    private sealed class ChangingSerializedStringToken : StringToken
    {
        public ChangingSerializedStringToken() : base("placeholder") { }

        public int SerializationCount { get; private set; }

        protected override string GetUnderlyingValue(TokenStringOptions options) =>
            ++SerializationCount <= 2 ? "'" : "";
    }

    private sealed class CountingSerializedStringToken : StringToken
    {
        private readonly Action onSerialize;

        public CountingSerializedStringToken(Action onSerialize) : base("p")
        {
            this.onSerialize = onSerialize;
        }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            onSerialize();
            return "p";
        }
    }

    private sealed class CallbackVariableRefToken : VariableRefToken
    {
        private readonly Action callback;

        public CallbackVariableRefToken(Action callback) : base("LEFT")
        {
            this.callback = callback;
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            callback();
            return "left";
        }
    }

    private sealed class ChangingModifierLiteral : LiteralToken
    {
        public ChangingModifierLiteral() : base("placeholder", canContainVariables: true) { }

        public int ResolutionCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null) => ++ResolutionCount == 1 ? "first" : "second";
    }

    private sealed class EditingVariableRefToken : VariableRefToken
    {
        private readonly LiteralToken literal;
        private readonly string edit;

        public EditingVariableRefToken(LiteralToken literal, string edit) : base("RIGHT")
        {
            this.literal = literal;
            this.edit = edit;
        }

        public int ResolutionCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            literal.Value = edit;
            return "right";
        }
    }

    private sealed class SerializedStringToken : StringToken
    {
        public SerializedStringToken() : base("placeholder") { }

        protected override string GetUnderlyingValue(TokenStringOptions options) => "computed";
    }

    private sealed class EditingModifierLiteral : LiteralToken
    {
        private readonly LiteralToken literal;

        public EditingModifierLiteral(LiteralToken literal) : base("placeholder", canContainVariables: true)
        {
            this.literal = literal;
        }

        public int ResolutionCount { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            literal.Value = "${A:-a}${B:-b}";
            return "right";
        }
    }
}
