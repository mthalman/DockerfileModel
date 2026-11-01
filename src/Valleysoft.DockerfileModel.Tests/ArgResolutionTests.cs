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
    public void ResolveVariables_UnchangedCustomCallbacksDoNotRebuildLexicalPlan()
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
        Assert.Equal(2 * count, serializations);
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
