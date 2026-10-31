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
}
