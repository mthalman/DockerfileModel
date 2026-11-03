using Valleysoft.DockerfileModel.Parsing;
using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class VolumeInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<VolumeInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, VolumeInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        VolumeInstruction result = new(scenario.Paths);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Parse_MountFlagTreatedAsLiteralPath()
    {
        VolumeInstruction result = VolumeInstruction.Parse("VOLUME --mount=type=bind,target=/data /data");
        Assert.Equal("VOLUME --mount=type=bind,target=/data /data", result.ToString());
        Assert.Collection(result.Paths,
            path => Assert.Equal("--mount=type=bind,target=/data", path),
            path => Assert.Equal("/data", path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_JsonPaths(bool updateInline)
    {
        const string text = "FROM scratch\nARG ROOT=/srv\nVOLUME [\"$ROOT/data\", \"${ROOT}/logs\"]";
        const string expected = "VOLUME [\"/srv/data\", \"/srv/logs\"]";
        Dockerfile dockerfile = Dockerfile.Parse(text);
        VolumeInstruction instruction = Assert.Single(dockerfile.Items.OfType<VolumeInstruction>());
        LiteralToken[] paths = instruction.PathTokens.ToArray();

        Assert.All(paths, path => Assert.Single(path.Tokens.OfType<VariableRefToken>()));
        Assert.Equal(text, dockerfile.ToString());
        Assert.Equal(expected, dockerfile.ResolveVariables(instruction,
            options: new ResolutionOptions { UpdateInline = updateInline }));
        Assert.Equal(updateInline ? expected : "VOLUME [\"$ROOT/data\", \"${ROOT}/logs\"]",
            instruction.ToString());
        Assert.Equal(updateInline ? new[] { "/srv/data", "/srv/logs" } : new[] { "$ROOT/data", "${ROOT}/logs" },
            instruction.Paths);
        Assert.Equal(paths, instruction.PathTokens);
        Assert.Equal(updateInline ? text.Replace("VOLUME [\"$ROOT/data\", \"${ROOT}/logs\"]", expected) : text,
            dockerfile.ToString());
    }

    [Theory]
    [InlineData("${ROOT:-/fallback}", "/srv")]
    [InlineData("${MISSING:-/fallback}", "/fallback")]
    [InlineData("${ROOT-/fallback}", "/srv")]
    [InlineData("${MISSING-/fallback}", "/fallback")]
    [InlineData("${ROOT:+/alternate}", "/alternate")]
    [InlineData("${MISSING:+/alternate}", "")]
    [InlineData("${ROOT+/alternate}", "/alternate")]
    [InlineData("${MISSING+/alternate}", "")]
    [InlineData("${ROOT:?required}", "/srv")]
    [InlineData("${ROOT?required}", "/srv")]
    public void ResolveVariables_JsonPathModifiers(string expression, string expected)
    {
        string text = $"VOLUME [\"{expression}/data\"]";
        foreach (bool updateInline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            Assert.Equal($"VOLUME [\"{expected}/data\"]", instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "/srv" },
                new ResolutionOptions { UpdateInline = updateInline }));
            Assert.Equal(updateInline ? $"VOLUME [\"{expected}/data\"]" : text, instruction.ToString());
        }
    }

    [Theory]
    [InlineData("${MISSING:?required}")]
    [InlineData("${MISSING?required}")]
    public void ResolveVariables_JsonPathRequiredModifierThrows(string expression)
    {
        string text = $"VOLUME [\"{expression}/data\"]";
        foreach (bool updateInline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            Assert.Throws<VariableSubstitutionException>(() => instruction.ResolveVariables('\\',
                new Dictionary<string, string?>(), new ResolutionOptions { UpdateInline = updateInline }));
            Assert.Equal(text, instruction.ToString());
        }
    }

    [Theory]
    [InlineData('\\', "VOLUME [\"$ROOT/a\\\"b\\\"\", \"$ROOT/c\\\\d\"]",
        "VOLUME [\"/srv/a\\\"b\\\"\", \"/srv/c\\\\d\"]")]
    [InlineData('\\', "VOLUME [\"$ROOT/data\", \\\n \"${ROOT}/logs\"]",
        "VOLUME [\"/srv/data\", \\\n \"/srv/logs\"]")]
    [InlineData('`', "VOLUME [\"$ROOT/data\", `\r\n \"${ROOT}/logs\"]",
        "VOLUME [\"/srv/data\", `\r\n \"/srv/logs\"]")]
    [InlineData('\\', "VOLUME [\"$ROOT/da\\\nta\"]", "VOLUME [\"/srv/da\\\nta\"]")]
    [InlineData('`', "VOLUME [\"$ROOT/da`\r\nta\"]", "VOLUME [\"/srv/da`\r\nta\"]")]
    public void ResolveVariables_JsonPathsPreserveSyntax(char escapeChar, string text, string expected)
    {
        foreach (bool updateInline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse(text, escapeChar);
            Assert.Equal(text, instruction.ToString());
            Assert.Equal(expected, instruction.ResolveVariables(escapeChar,
                new Dictionary<string, string?> { ["ROOT"] = "/srv" },
                new ResolutionOptions { UpdateInline = updateInline }));
            Assert.Equal(updateInline ? expected : text, instruction.ToString());
            Assert.Equal(instruction.Paths, VolumeInstruction.Parse(instruction.ToString(), escapeChar).Paths);
        }
    }

    [Fact]
    public void Paths_JsonVariableEditsRemainExpandable()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT/data\", \"/logs\"]");
        LiteralToken original = instruction.PathTokens[0];
        original.Value = "${ROOT}/cache";
        instruction.Paths[1] = "$ROOT/logs";
        instruction.Paths.Add("${ROOT:-/fallback}/extra");
        LiteralToken inserted = new("$ROOT/token", canContainVariables: true);
        instruction.PathTokens.Add(inserted);

        Assert.Same(original, instruction.PathTokens[0]);
        Assert.Same(inserted, instruction.PathTokens[3]);
        Assert.All(instruction.PathTokens, path =>
        {
            Assert.Equal('"', path.QuoteChar);
            Assert.Single(path.Tokens.OfType<VariableRefToken>());
        });
        Assert.Equal("VOLUME [\"/srv/cache\", \"/srv/logs\", \"/srv/extra\", \"/srv/token\"]",
            instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["ROOT"] = "/srv" }));
        Assert.Equal(new[] { "${ROOT}/cache", "$ROOT/logs", "${ROOT:-/fallback}/extra", "$ROOT/token" },
            instruction.Paths);
    }

    [Fact]
    public void Create_JsonPathsRecognizeVariables()
    {
        VolumeInstruction instruction = new(new[] { "$ROOT/data", "${ROOT}/logs" });
        Assert.All(instruction.PathTokens, path => Assert.Single(path.Tokens.OfType<VariableRefToken>()));
        Assert.Equal("VOLUME [\"/srv/data\", \"/srv/logs\"]",
            instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["ROOT"] = "/srv" }));
        Assert.Equal("VOLUME [\"$ROOT/data\", \"${ROOT}/logs\"]", instruction.ToString());
    }

    [Theory]
    [InlineData("/srv/\"cache")]
    [InlineData("C:\\cache")]
    [InlineData("/srv/\n\tcache")]
    [InlineData("/srv/$LATER")]
    public void ResolveVariables_JsonPathsEncodeSubstitutions(string root)
    {
        const string text = "VOLUME [\"$ROOT/data\"]";
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            LiteralToken path = instruction.PathTokens[0];
            Dictionary<string, string?> variables = new() { ["ROOT"] = root, ["LATER"] = "unexpected" };
            string resolved = instruction.ResolveVariables('\\', variables,
                new ResolutionOptions { UpdateInline = inline })!;
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(resolved.Substring(7));
            Assert.Equal(root + "/data", json.RootElement[0].GetString());
            Assert.Equal(inline ? resolved : text, instruction.ToString());
            Assert.Same(path, instruction.PathTokens[0]);
            Assert.Equal(resolved, VolumeInstruction.Parse(resolved).ToString());
            if (inline)
            {
                Assert.Empty(path.Tokens.OfType<VariableRefToken>());
                Assert.Equal(resolved, instruction.ResolveVariables('\\', variables,
                    new ResolutionOptions { UpdateInline = true }));
            }
        }
    }

    [Theory]
    [InlineData("VOLUME [\"\\u0024ROOT/data\"]")]
    [InlineData("VOLUME [\"$RO\\u004fT/data\"]")]
    [InlineData("VOLUME [\"\\u0024\\u007bROOT\\u007d/data\"]")]
    public void ResolveVariables_JsonPathsDecodeVariableSyntax(string text)
    {
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            Assert.Equal(text, instruction.ToString());
            string resolved = instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "/srv" },
                new ResolutionOptions { UpdateInline = inline })!;
            Assert.Equal("VOLUME [\"/srv/data\"]", resolved);
            Assert.Equal(inline ? resolved : text, instruction.ToString());
        }
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('`')]
    public void ResolveVariables_JsonPathsHonorDecodedEscapes(char escapeChar)
    {
        string text = escapeChar == '\\' ? "VOLUME [\"\\\\$ROOT/data\"]" : "VOLUME [\"`$ROOT/data\"]";
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse(text, escapeChar);
            Token[] children = instruction.PathTokens[0].Tokens.ToArray();
            Assert.Equal(text, instruction.ResolveVariables(escapeChar,
                new Dictionary<string, string?> { ["ROOT"] = "/srv" },
                new ResolutionOptions { UpdateInline = inline }));
            Assert.Equal(text, instruction.ToString());
            if (inline)
            {
                Assert.Equal(children, instruction.PathTokens[0].Tokens);
                Assert.Equal("VOLUME [\"$ROOT/data\"]", instruction.ResolveVariables(escapeChar,
                    new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
                    new ResolutionOptions { RemoveEscapeCharacters = true }));
                Assert.Equal(text, instruction.ToString());
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_JsonPathTokenAndEditsUseJsonContext(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"\\u0024ROOT/data\"]", '`');
        Dictionary<string, string?> variables = new() { ["ROOT"] = "/srv/\"cache", ["LATER"] = "next" };
        LiteralToken path = instruction.PathTokens[0];
        string resolved = path.ResolveVariables('`', variables,
            new ResolutionOptions { UpdateInline = inline })!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(resolved);
        Assert.Equal("/srv/\"cache/data", json.RootElement.GetString());
        Assert.Equal(resolved, VolumeInstruction.Parse("VOLUME [" + resolved + "]", '`').PathTokens[0].ToString());

        instruction.Paths[0] = "$LATER/cache";
        Assert.Same(path, instruction.PathTokens[0]);
        Assert.Equal("\"next/cache\"", path.ResolveVariables('`', variables));
        LiteralToken incoming = new("$ROOT/extra", canContainVariables: true, escapeChar: '`');
        instruction.PathTokens.Add(incoming);
        string incomingResolved = incoming.ResolveVariables('`', variables)!;
        using System.Text.Json.JsonDocument incomingJson = System.Text.Json.JsonDocument.Parse(incomingResolved);
        Assert.Equal("/srv/\"cache/extra", incomingJson.RootElement.GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_JsonPathsSupportEmptyAndNestedModifiers(bool inline)
    {
        const string text = "VOLUME [\"${MISSING:-}/data\", \"${MISSING:-prefix${OTHER}}/data\", \"/$_ROOT/data\", \"${ROOT:+}\", \"${ROOT:?}\"]";
        const string expected = "VOLUME [\"/data\", \"prefixnested/data\", \"/srv/data\", \"\", \"/srv\"]";
        VolumeInstruction instruction = VolumeInstruction.Parse(text);
        Assert.Equal(expected, instruction.ResolveVariables('\\', new Dictionary<string, string?>
        {
            ["ROOT"] = "/srv", ["OTHER"] = "nested", ["_ROOT"] = "srv"
        }, new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(inline ? expected : text, instruction.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_JsonDecodedEscapesCanBeRemovedSafely(bool inline)
    {
        const string text = "VOLUME [\"\\\\$ROOT/data\"]";
        VolumeInstruction instruction = VolumeInstruction.Parse(text);
        const string expected = "VOLUME [\"$ROOT/data\"]";
        Assert.Equal(expected, instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
            new ResolutionOptions { UpdateInline = inline, RemoveEscapeCharacters = true }));
        Assert.Equal(text, instruction.ToString());
        if (inline)
        {
            Assert.Equal(text, instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
                new ResolutionOptions { UpdateInline = true }));
        }
    }

    [Theory]
    [InlineData("VOLUME [\"\\u0027$ROOT\\u0027/data\"]",
        "VOLUME [\"\\u0027$ROOT\\u0027/data\"]")]
    [InlineData("VOLUME [\"prefix'$ROOT'/data/$ROOT\"]",
        "VOLUME [\"prefix'$ROOT'/data//srv\"]")]
    [InlineData("VOLUME [\"\\u0027\\u0024{BROKEN\\u0027\"]",
        "VOLUME [\"\\u0027\\u0024{BROKEN\\u0027\"]")]
    [InlineData("VOLUME [\"\\u0022'$ROOT'\\u0022/data\"]",
        "VOLUME [\"\\u0022'/srv'\\u0022/data\"]")]
    [InlineData("VOLUME [\"\\\\'$ROOT/data\"]",
        "VOLUME [\"\\\\'/srv/data\"]")]
    public void ResolveVariables_JsonPathsHonorDecodedQuoteState(string text, string expected)
    {
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            Assert.Equal(expected, instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "/srv" },
                new ResolutionOptions { UpdateInline = inline }));
            Assert.Equal(inline ? expected : text, instruction.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_JsonPathTokensRetainVariablePolicy(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"/data\"]");
        LiteralToken incoming = new("$ROOT/cache", canContainVariables: false);
        instruction.PathTokens.Add(incoming);
        incoming.Value = "$ROOT/logs";
        Token[] children = incoming.Tokens.ToArray();

        Assert.Equal("VOLUME [\"/data\", \"$ROOT/logs\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "/srv" },
            new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(children, incoming.Tokens);
        Assert.Equal("$ROOT/logs", incoming.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_JsonInlinePreservesUntouchedChildren(bool removeEscapes)
    {
        const string text = "VOLUME [\"C:\\\\cache\", \"prefix$ROOT/data\"]";
        VolumeInstruction instruction = VolumeInstruction.Parse(text);
        LiteralToken first = instruction.PathTokens[0];
        Token[] firstChildren = first.Tokens.ToArray();
        LiteralToken second = instruction.PathTokens[1];
        Token prefix = second.Tokens.First();
        Token suffix = second.Tokens.Last();
        const string model = "VOLUME [\"C:\\\\cache\", \"prefix/srv/data\"]";

        Assert.Equal(removeEscapes ? "VOLUME [\"C:cache\", \"prefix/srv/data\"]" : model,
            instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["ROOT"] = "/srv" },
                new ResolutionOptions { UpdateInline = true, RemoveEscapeCharacters = removeEscapes }));
        Assert.Equal(model, instruction.ToString());
        Assert.Equal(firstChildren, first.Tokens);
        Assert.Same(prefix, second.Tokens.First());
        Assert.Same(suffix, second.Tokens.Last());
        Assert.Equal(model, instruction.ResolveVariables('\\'));
    }

    [Fact]
    public void ResolveVariables_JsonInlineSubstitutionsStayLiteralAfterPrimitiveEdits()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"prefix$ROOT/data\"]");
        LiteralToken path = instruction.PathTokens[0];
        Dictionary<string, string?> variables = new() { ["ROOT"] = "$LATER", ["LATER"] = "unexpected" };
        Assert.Equal("VOLUME [\"prefix$LATER/data\"]",
            instruction.ResolveVariables('\\', variables, new ResolutionOptions { UpdateInline = true }));
        Assert.IsType<StringToken>(path.Tokens.First()).Value = "edited";
        Assert.Equal("VOLUME [\"edited$LATER/data\"]", instruction.ResolveVariables('\\', variables));

        path.Value = "$LATER/data";
        Assert.Equal("VOLUME [\"unexpected/data\"]", instruction.ResolveVariables('\\', variables));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_JsonInlineValuesDoNotAffectNewReferenceQuoteOrEscapeState(bool inline)
    {
        const string root = "$LATER'\\";
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        Dictionary<string, string?> variables = new()
        {
            ["ROOT"] = root, ["LATER"] = "unexpected", ["NEXT"] = "/next"
        };
        instruction.ResolveVariables('\\', variables, new ResolutionOptions { UpdateInline = true });
        instruction.PathTokens[0].TokenList.Add(new VariableRefToken("NEXT"));

        string resolved = instruction.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = inline })!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(resolved.Substring(7));
        Assert.Equal(root + "/next", json.RootElement[0].GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_JsonInlineValuesCanBeFormattedWithoutModelMutation(bool inline)
    {
        const string root = "/a'\\cache";
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        string model = instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["ROOT"] = root },
            new ResolutionOptions { UpdateInline = true })!;
        string formatted = instruction.ResolveVariables('\\', options: new ResolutionOptions
        {
            UpdateInline = inline, RemoveEscapeCharacters = true
        })!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(formatted.Substring(7));
        Assert.Equal("/a'cache", json.RootElement[0].GetString());
        Assert.Equal(model, instruction.ToString());
    }

    [Theory]
    [InlineData("\\uD800")]
    [InlineData("\\uDC00")]
    [InlineData("\\uD800x")]
    [InlineData("\\uD800\\uD800")]
    public void ResolveVariables_JsonPathsAcceptUnpairedSurrogates(string content)
    {
        string text = $"VOLUME [\"{content}\"]";
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            Token[] children = instruction.PathTokens[0].Tokens.ToArray();
            Assert.Equal(text, instruction.ResolveVariables('\\',
                options: new ResolutionOptions { UpdateInline = inline }));
            Assert.Equal(text, instruction.ToString());
            Assert.Equal(children, instruction.PathTokens[0].Tokens);
        }
    }

    [Theory]
    [InlineData("\\uD800", "\uFFFD")]
    [InlineData("\\uDC00", "\uFFFD")]
    [InlineData("\\uD800x", "\uFFFDx")]
    [InlineData("\\uD83D\\uDE80", "\uD83D\uDE80")]
    public void ResolveVariables_JsonModifierValuesDecodeSurrogates(string content, string expected)
    {
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"${{MISSING:-{content}}}\"]");
            string resolved = instruction.ResolveVariables('\\',
                options: new ResolutionOptions { UpdateInline = inline })!;
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(resolved.Substring(7));
            Assert.Equal(expected, json.RootElement[0].GetString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_JsonConstructorAcceptedTabRemainsResolvable(bool inline)
    {
        VolumeInstruction instruction = new(new[] { "/my\tdata" });
        const string expected = "VOLUME [\"/my\tdata\"]";
        Assert.Equal(expected, instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(expected, instruction.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveVariables_ParserAcceptedNonJsonEscapesRemainResolvable(bool inline)
    {
        const string text = "VOLUME [\"C:\\cache\"]";
        VolumeInstruction instruction = VolumeInstruction.Parse(text);
        Assert.Equal(text, instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(text, instruction.ToString());
    }

    [Theory]
    [InlineData(64)]
    [InlineData(512)]
    public void ResolveVariables_JsonPathsRenderSourceTokensConstantTimes(int count)
    {
        string content = string.Concat(Enumerable.Repeat("$ROOT/data/", count));
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"{content}\"]");
        CountingStringToken source = new(content);
        instruction.PathTokens[0].ReplaceWithToken(source);

        Assert.Equal($"VOLUME [\"{string.Concat(Enumerable.Repeat("/srv/data/", count))}\"]",
            instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["ROOT"] = "/srv" }));
        Assert.InRange(source.RenderCount, 1, 10);
    }

    private sealed class CountingStringToken(string value) : StringToken(value)
    {
        public int RenderCount { get; private set; }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            RenderCount++;
            return base.GetUnderlyingValue(options);
        }
    }

    [Theory]
    [InlineData("VOLUME [\"\\\\${BROKEN\"]", '\\')]
    [InlineData("VOLUME [\"`$\\u007bBROKEN\"]", '`')]
    [InlineData("VOLUME [\"prefix'${BROKEN'\"]", '\\')]
    public void JsonProtectedMalformedReferencesRemainLiteral(string text, char escapeChar)
    {
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse(text, escapeChar);
            Assert.Equal(text, instruction.ToString());
            Assert.Equal(text, instruction.ResolveVariables(escapeChar,
                options: new ResolutionOptions { UpdateInline = inline }));
            Assert.Equal(text, instruction.ToString());
            instruction.PathTokens[0].Value = text.Substring(9, text.Length - 11);
            Assert.Equal(text, instruction.ResolveVariables(escapeChar));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonResolutionCannotConsumePreviousSubstitutionsAcrossTokenBoundaries(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$${NAME}\"]");
        Dictionary<string, string?> variables = new() { ["NAME"] = "ROOT", ["ROOT"] = "unexpected" };
        const string expected = "VOLUME [\"$ROOT\"]";
        Assert.Equal(expected, instruction.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(expected, instruction.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(expected, instruction.ToString());
    }

    [Theory]
    [InlineData("{ROOT}")]
    [InlineData("{ROOT:-fallback}")]
    public void JsonResolutionCannotConsumeSubstitutedBraces(string name)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$${NAME}\"]");
        Dictionary<string, string?> variables = new() { ["NAME"] = name, ["ROOT"] = "unexpected" };
        string expected = instruction.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = true })!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(expected.Substring(7));
        Assert.Equal("$" + name, json.RootElement[0].GetString());
        Assert.Equal(expected, instruction.ResolveVariables('\\', variables));
    }

    [Theory]
    [InlineData("a\\nb", "a\nb")]
    [InlineData("a\\rb", "a\rb")]
    [InlineData("a\\u000Ab", "a\nb")]
    public void JsonModifierValuesAllowDecodedLineTerminators(string content, string expected)
    {
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"${{MISSING:-{content}}}\"]");
            string resolved = instruction.ResolveVariables('\\',
                options: new ResolutionOptions { UpdateInline = inline })!;
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(resolved.Substring(7));
            Assert.Equal(expected, json.RootElement[0].GetString());
        }
    }

    [Theory]
    [InlineData("${MISSING:-'\\u0024ROOT'}", "'$ROOT'")]
    [InlineData("${MISSING:-prefix'\\u0024ROOT'}", "prefix'$ROOT'")]
    [InlineData("${MISSING:-${OTHER:-'\\u0024ROOT'}}", "'$ROOT'")]
    [InlineData("${MISSING:-\\u0022'\\u0024ROOT'\\u0022}", "\"'/srv'\"")]
    public void JsonModifierValuesHonorDecodedQuotes(string content, string expected)
    {
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"{content}\"]");
            string resolved = instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["ROOT"] = "/srv" },
                new ResolutionOptions { UpdateInline = inline })!;
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(resolved.Substring(7));
            Assert.Equal(expected, json.RootElement[0].GetString());
        }
    }

    [Theory]
    [InlineData("\\uD83D\\uDE80")]
    [InlineData("\uD83D\uDE80")]
    public void JsonEscapeRemovalPreservesSupplementaryCharacters(string content)
    {
        foreach (bool inline in new[] { false, true })
        {
            string text = $"VOLUME [\"\\\\{content}\"]";
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            string resolved = instruction.ResolveVariables('\\',
                options: new ResolutionOptions { UpdateInline = inline, RemoveEscapeCharacters = true })!;
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(resolved.Substring(7));
            Assert.Equal("\uD83D\uDE80", json.RootElement[0].GetString());
            Assert.Equal(text, instruction.ToString());
        }
    }

    [Fact]
    public void JsonResolutionContextDoesNotFollowPathsIntoShellCopy()
    {
        VolumeInstruction volume = VolumeInstruction.Parse("VOLUME [\"\\u0024ROOT\"]");
        LiteralToken path = volume.PathTokens[0];
        volume.PathTokens.RemoveAt(0);
        CopyInstruction copy = CopyInstruction.Parse("COPY /data /dest");
        copy.SourceTokens.Add(path);
        Assert.Equal("COPY /data \"\\u0024ROOT\" /dest", copy.ToString());
        Assert.Equal(copy.ToString(), copy.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" }));
    }

    [Fact]
    public void JsonResolutionContextDoesNotFollowPathsIntoCopyDestinations()
    {
        VolumeInstruction volume = VolumeInstruction.Parse("VOLUME [\"\\u0024ROOT\"]");
        LiteralToken path = volume.PathTokens[0];
        volume.PathTokens.RemoveAt(0);
        CopyInstruction copy = CopyInstruction.Parse("COPY /data /dest");
        copy.DestinationToken = path;
        Assert.Equal(copy.ToString(), copy.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonActiveMalformedReferencesStillFailWithoutMutatingPaths(bool inline)
    {
        const string text = "VOLUME [\"\\u0024{BROKEN\"]";
        VolumeInstruction instruction = VolumeInstruction.Parse(text);
        Assert.Throws<ParseException>(() => instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(text, instruction.ToString());
    }

    [Theory]
    [InlineData("${MISSING:-'\\u0024{BROKEN'}", "'${BROKEN'")]
    [InlineData("${MISSING:-a\\\\\\u0024ROOT}", "a\\$ROOT")]
    [InlineData("${MISSING:-a\\nb${OTHER}}", "a\nbnested")]
    public void JsonNestedModifiersRetainProtectionAndWhitespace(string content, string expected)
    {
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"{content}\"]");
            string resolved = instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["OTHER"] = "nested", ["ROOT"] = "unexpected" },
                new ResolutionOptions { UpdateInline = inline })!;
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(resolved.Substring(7));
            Assert.Equal(expected, json.RootElement[0].GetString());
        }
    }

    [Fact]
    public void JsonPlainResolutionSerializesSourceOnce()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"/data\"]");
        CountingStringToken source = new("/data");
        instruction.PathTokens[0].ReplaceWithToken(source);
        Assert.Equal("VOLUME [\"/data\"]", instruction.ResolveVariables('\\'));
        Assert.Equal(1, source.RenderCount);
    }

    [Theory]
    [InlineData("WORKDIR")]
    [InlineData("USER")]
    [InlineData("ARG")]
    public void JsonResolutionContextDoesNotFollowPathsIntoScalarOperands(string name)
    {
        VolumeInstruction volume = VolumeInstruction.Parse("VOLUME [\"\\u0024ROOT\"]");
        LiteralToken path = volume.PathTokens[0];
        volume.PathTokens.RemoveAt(0);
        AggregateToken owner;
        if (name == "WORKDIR")
        {
            WorkdirInstruction workdir = new("/data") { PathToken = path };
            owner = workdir;
        }
        else if (name == "USER")
        {
            UserInstruction user = new("root") { UserToken = path };
            owner = user;
        }
        else
        {
            ArgDeclaration declaration = new("PATH") { ValueToken = path };
            owner = declaration;
        }
        Dictionary<string, string?> variables = new() { ["ROOT"] = "unexpected" };
        Assert.Equal(owner.ToString(), owner.ResolveVariables('\\', variables));
        Assert.Equal(path.ToString(), path.ResolveVariables('\\', variables));
    }

    [Fact]
    public void JsonResolutionPolicyBelongsToEachSharedPathOwner()
    {
        VolumeInstruction volume = VolumeInstruction.Parse("VOLUME [\"\\u0024ROOT\"]");
        CopyInstruction copy = CopyInstruction.Parse("COPY /data /dest");
        copy.SourceTokens.Add(volume.PathTokens[0]);
        Dictionary<string, string?> variables = new() { ["ROOT"] = "/srv" };
        Assert.Equal("VOLUME [\"/srv\"]", volume.ResolveVariables('\\', variables));
        Assert.Equal(copy.ToString(), copy.ResolveVariables('\\', variables));

        CopyInstruction donor = CopyInstruction.Parse("COPY \"\\u0024ROOT\" /dest");
        VolumeInstruction receiver = VolumeInstruction.Parse("VOLUME []");
        receiver.PathTokens.Add(donor.SourceTokens[0]);
        Assert.Equal("VOLUME [\"/srv\"]", receiver.ResolveVariables('\\', variables));
        Assert.Equal(donor.ToString(), donor.ResolveVariables('\\', variables));
    }

    [Theory]
    [InlineData("'a\\\\b'", "'ab'")]
    [InlineData("'\\\\$ROOT'", "'$ROOT'")]
    [InlineData("'a\\\\\\\\b'", "'a\\b'")]
    [InlineData("a\\\\", "a")]
    public void JsonEscapeFormattingAppliesToProtectedAndTerminalEscapes(string content, string expected)
    {
        foreach (bool inline in new[] { false, true })
        {
            string text = $"VOLUME [\"{content}\"]";
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            string resolved = instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
                new ResolutionOptions { RemoveEscapeCharacters = true, UpdateInline = inline })!;
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(resolved.Substring(7));
            Assert.Equal(expected, json.RootElement[0].GetString());
            Assert.Equal(text, instruction.ToString());
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(512)]
    public void JsonProtectedNestedMalformedReferencesRemainLossless(int count)
    {
        string text = "VOLUME [\"'" + string.Concat(Enumerable.Repeat("${X:-", count)) + "'\"]";
        VolumeInstruction instruction = VolumeInstruction.Parse(text);
        Assert.Equal(text, instruction.ToString());
        Assert.Equal(text, instruction.ResolveVariables('\\'));
    }

    [Fact]
    public void JsonUnclosedOuterReferencesDoNotHideCompleteInnerReferences()
    {
        string text = "VOLUME [\"'" + string.Concat(Enumerable.Repeat("${X:-", 512)) + "${ROOT}'\"]";
        VolumeInstruction instruction = VolumeInstruction.Parse(text);
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        Assert.Equal(text, instruction.ToString());
        Assert.Equal(text, instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" }));
    }

    [Fact]
    public void JsonTokenizationKeepsPhysicalReferenceBoundariesAcrossContinuations()
    {
        const string text = "VOLUME [\"${ROOT:-$\\\n{literal}\"]";
        VolumeInstruction instruction = VolumeInstruction.Parse(text);
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        Assert.Equal(text, instruction.ToString());
    }

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    public void JsonProtectedBalancedReferencesParseWithoutRecursiveDescent(int depth)
    {
        string content = "'" + string.Concat(Enumerable.Repeat("${X:-", depth)) +
            "value" + new string('}', depth) + "'";
        string text = $"VOLUME [\"{content}\"]";
        VolumeInstruction instruction = VolumeInstruction.Parse(text);

        Assert.Equal(text, instruction.ToString());
        Assert.Equal(text, instruction.ResolveVariables('\\'));
        Assert.Equal(text, instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        VariableRefToken? reference = instruction.PathTokens[0].Tokens.OfType<VariableRefToken>().Single();
        int actualDepth = 0;
        while (reference is not null)
        {
            actualDepth++;
            reference = reference.ModifierValueToken!.Tokens.OfType<VariableRefToken>().SingleOrDefault();
        }
        Assert.Equal(depth, actualDepth);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonSharedGlobalArgDefaultsRetainTheirDocumentResolutionContext(bool inline)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "ARG ROOT=alpine\nARG IMAGE=\"\\u0024ROOT\"\nFROM $IMAGE\nVOLUME []");
        FromInstruction from = Assert.Single(dockerfile.Items.OfType<FromInstruction>());
        string expected = dockerfile.ResolveVariables(from);
        LiteralToken value = dockerfile.Items.OfType<ArgInstruction>().Last().ArgTokens[0].ValueToken!;
        Assert.Single(dockerfile.Items.OfType<VolumeInstruction>()).PathTokens.Add(value);

        Assert.Equal(expected, dockerfile.ResolveVariables(from,
            options: new ResolutionOptions { UpdateInline = inline }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonSharedStageArgDefaultsRetainTheirDocumentResolutionContext(bool inline)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "FROM scratch\nARG ROOT=/srv\nARG PATH=\"\\u0024ROOT\"\nVOLUME []\nWORKDIR $PATH");
        WorkdirInstruction workdir = Assert.Single(dockerfile.Items.OfType<WorkdirInstruction>());
        string expected = dockerfile.ResolveVariables(workdir);
        LiteralToken value = dockerfile.Items.OfType<ArgInstruction>().Last().ArgTokens[0].ValueToken!;
        Assert.Single(dockerfile.Items.OfType<VolumeInstruction>()).PathTokens.Add(value);

        Assert.Equal(expected, dockerfile.ResolveVariables(workdir,
            options: new ResolutionOptions { UpdateInline = inline }));
    }

    [Theory]
    [InlineData("\\u0027$ROOT")]
    [InlineData("\\u0022$ROOT")]
    [InlineData("prefix'$ROOT")]
    public void JsonUnmatchedDecodedQuotesFailWithoutMutatingPaths(string content)
    {
        foreach (bool inline in new[] { false, true })
        {
            string text = $"VOLUME [\"{content}\"]";
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            Assert.Throws<ParseException>(() => instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "/srv" },
                new ResolutionOptions { UpdateInline = inline }));
            Assert.Equal(text, instruction.ToString());
        }
    }

    [Theory]
    [InlineData("#")]
    [InlineData("##")]
    [InlineData("%")]
    [InlineData("%%")]
    [InlineData("/")]
    [InlineData("//")]
    public void JsonUnresolvedModifiersRetainSourceSpellingAndTokens(string modifier)
    {
        foreach (bool inline in new[] { false, true })
        {
            string expression = $"${{ROOT{modifier}\\u0061}}";
            string text = $"VOLUME [\"{expression}/$OTHER\"]";
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            VariableRefToken unresolved = instruction.PathTokens[0].Tokens.OfType<VariableRefToken>().First();
            string expected = $"VOLUME [\"{expression}/data\"]";

            Assert.Equal(expected, instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "/srv", ["OTHER"] = "data" },
                new ResolutionOptions { UpdateInline = inline }));
            Assert.Equal(inline ? expected : text, instruction.ToString());
            Assert.Contains(unresolved, instruction.PathTokens[0].Tokens);
            Assert.Equal(expression, unresolved.ToString());
        }
    }

    [Fact]
    public void JsonDecodedReferenceParserBoundsNestedGraphAllocations()
    {
        string text = "${MISSING:-" + string.Concat(Enumerable.Repeat("$ROOT/", 128)) + "}";
        const string warmup = "${MISSING:-$ROOT/}";
        DecodedVariableParsers.Variable('\\', Character.AnyChar, warmup).Parse(warmup);
        long before = GC.GetAllocatedBytesForCurrentThread();
        TextParser<VariableRefToken> parser = DecodedVariableParsers.Variable('\\', Character.AnyChar, text);
        VariableRefToken variable = parser.Parse(text);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(text, variable.ToString());
        Assert.True(allocated < 1_000_000, $"Nested reference parsing allocated {allocated} bytes.");
    }

    [Fact]
    public void JsonDeepDecodedReferencesResolveWithoutRecursiveDescent()
    {
        const int depth = 4096;
        string content = string.Concat(Enumerable.Repeat("\\u0024{X:-", depth)) +
            "value" + new string('}', depth);
        string text = $"VOLUME [\"{content}\"]";
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse(text);
            Assert.Equal("VOLUME [\"value\"]", instruction.ResolveVariables('\\',
                options: new ResolutionOptions { UpdateInline = inline }));
            Assert.Equal(inline ? "VOLUME [\"value\"]" : text, instruction.ToString());
        }
    }

    [Theory]
    [InlineData(":-", null, "value")]
    [InlineData("-", "", "")]
    [InlineData(":+", "set", "value")]
    [InlineData("+", null, "value")]
    [InlineData(":?", "set", "set")]
    [InlineData("?", null, "")]
    public void JsonDeepDecodedModifiersRetainSetAndEmptySemantics(string modifier, string? value, string expected)
    {
        string content = string.Concat(Enumerable.Repeat($"\\u0024{{X{modifier}", 512)) +
            "value" + new string('}', 512);
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"{content}\"]");

        Assert.Equal($"VOLUME [\"{expected}\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["X"] = value }));
    }

    [Fact]
    public void JsonDeepUnusedModifierErrorsRemainLazy()
    {
        string content = "\\u0024{ROOT:-" +
            string.Concat(Enumerable.Repeat("\\u0024{MISSING:?", 512)) + "required" + new string('}', 513);
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"{content}\"]");

        Assert.Equal("VOLUME [\"set\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "set" }));
        Assert.Throws<VariableSubstitutionException>(() => instruction.ResolveVariables('\\'));
    }

    [Fact]
    public void JsonDeepNestedUnsupportedModifiersRemainLossless()
    {
        string pattern = "\\u0024{ROOT#" +
            string.Concat(Enumerable.Repeat("\\u0024{X:-", 4096)) + "value" + new string('}', 4097);
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"\\u0024{{MISSING:-{pattern}}}\"]");
        string resolved = instruction.ResolveVariables('\\')!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(resolved.Substring(7));

        Assert.Equal(pattern.Replace("\\u0024", "$"), json.RootElement[0].GetString());
    }

    [Fact]
    public void JsonSubstitutedModifierOperatorsStayLiteralAcrossPrimitiveEdits()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"prefix:${OP}fallback}\"]");
        instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["OP"] = "-" },
            new ResolutionOptions { UpdateInline = true });
        Assert.IsType<StringToken>(instruction.PathTokens[0].Tokens.First()).Value = "${ROOT:";
        string expected = instruction.ToString();

        Assert.Equal(expected, instruction.ResolveVariables('\\'));
        Assert.Equal(expected, instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
    }

    [Theory]
    [InlineData("\\u0024{BROKEN", 7)]
    [InlineData("\\u0027\\ntext", 13)]
    [InlineData("\\u0027line\\\ntext", 17)]
    public void JsonResolutionErrorsUsePhysicalPathPositions(string content, int offset)
    {
        string path = $"\"{content}\"";
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [{path}]");
        ParseException exception = Assert.Throws<ParseException>(() => instruction.ResolveVariables('\\'));
        Position expected = new TextSpan(path).Skip(offset).Position;

        Assert.Equal(expected.Absolute, exception.ErrorPosition.Absolute);
        Assert.Equal(expected.Line, exception.ErrorPosition.Line);
        Assert.Equal(expected.Column, exception.ErrorPosition.Column);
    }

    [Fact]
    public void JsonTokenizationAvoidsParsingEscapedReferences()
    {
        string content = string.Concat(Enumerable.Repeat("`$ROOT/", 512));
        LiteralToken raw = new(new[] { new StringToken(content) }, canContainVariables: false, '`')
        {
            QuoteChar = '"'
        };
        JsonStringVariableResolver.Tokenize(raw, '`');
        long before = GC.GetAllocatedBytesForCurrentThread();
        LiteralToken tokenized = JsonStringVariableResolver.Tokenize(raw, '`');
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(raw.ToString(), tokenized.ToString());
        Assert.Empty(tokenized.Tokens.OfType<VariableRefToken>());
        Assert.True(allocated < 500_000, $"Protected reference tokenization allocated {allocated} bytes.");
    }

    [Fact]
    public void JsonSimpleReferencesAvoidEvaluationWorklistAllocations()
    {
        VariableRefToken reference = new("ROOT");
        Dictionary<string, string?> variables = new() { ["ROOT"] = "value" };
        ResolutionOptions options = new();
        JsonStringVariableResolver.ResolveReference(reference, '\\', variables, options);
        long baselineStart = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1024; index++)
        {
            reference.ResolveVariables('\\', variables, options);
        }
        long baseline = GC.GetAllocatedBytesForCurrentThread() - baselineStart;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1024; index++)
        {
            JsonStringVariableResolver.ResolveReference(reference, '\\', variables, options);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated - baseline < 150_000,
            $"Simple evaluation allocated {allocated - baseline} bytes above token resolution.");
    }

    [Theory]
    [InlineData("24", "\\u00", "ROOT")]
    [InlineData("27", "\\u00", "$ROOT")]
    [InlineData("5c", "\\u00", "$ROOT")]
    public void JsonEscapesCannotReactivateSubstitutedCharacters(string digits, string prefix, string suffix)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"prefix${DIGITS}suffix\"]");
        LiteralToken path = instruction.PathTokens[0];
        instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["DIGITS"] = digits },
            new ResolutionOptions { UpdateInline = true });
        Assert.IsType<StringToken>(path.Tokens.First()).Value = prefix;
        Assert.IsType<StringToken>(path.Tokens.Last()).Value = suffix;
        string expected = instruction.ToString();

        foreach (bool inline in new[] { false, true })
        {
            string resolved = instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "value" },
                new ResolutionOptions { UpdateInline = inline })!;
            Assert.Equal(digits == "24" ? expected : expected.Replace("$ROOT", "value"), resolved);
            Assert.Equal(inline && digits != "24" ? resolved : expected, instruction.ToString());
        }
    }

    [Fact]
    public void JsonProtectedOpeningBracesDoNotChangeCandidateQuoteState()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"\\u0022${OPEN}suffix\\u0022\"]");
        LiteralToken path = instruction.PathTokens[0];
        instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["OPEN"] = "{" },
            new ResolutionOptions { UpdateInline = true });
        Assert.IsType<StringToken>(path.Tokens.First()).Value = "\\u0022$";
        Assert.IsType<StringToken>(path.Tokens.Last()).Value = "'$ROOT\\u0022";
        string expected = instruction.ToString().Replace("$ROOT", "value");

        Assert.Equal(expected, instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "value" }));
    }

    [Fact]
    public void JsonPrimitiveRenderingAvoidsTraversalAllocations()
    {
        StringToken token = new("value");
        JsonStringVariableResolver.RenderToken(token);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1024; index++)
        {
            Assert.Equal("value", JsonStringVariableResolver.RenderToken(token));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 100_000, $"Primitive rendering allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonBlockedReferenceNamesDoNotHideLaterVariables(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"\\u0022${NAME}suffix\\u0022\"]");
        LiteralToken path = instruction.PathTokens[0];
        instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["NAME"] = "X" },
            new ResolutionOptions { UpdateInline = true });
        Assert.IsType<StringToken>(path.Tokens.First()).Value = "\\u0022${";
        Assert.IsType<StringToken>(path.Tokens.Last()).Value = "'$ROOT\\u0022";
        string original = instruction.ToString();
        string expected = original.Replace("$ROOT", "value");

        Assert.Equal(expected, instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "value" },
            new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(inline ? expected : original, instruction.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonModifierLiteralPolicyIsRetained(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${MISSING:-$ROOT}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        LiteralToken literal = new("$ROOT", canContainVariables: false);
        reference.ModifierValueToken = literal;
        const string expected = "VOLUME [\"$ROOT\"]";

        Assert.Equal("$ROOT", literal.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" }));
        Assert.Equal(expected, instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
            new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(inline ? expected : "VOLUME [\"${MISSING:-$ROOT}\"]", instruction.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void JsonNestedOpaqueModifiersPreserveDecodedContents(bool inline, bool removeEscapes)
    {
        string[] contents =
        {
            "$ROOT", "'$ROOT", "\\u0024ROOT", "\\u0022$ROOT", "\\\\$ROOT",
            "${BROKEN", "}\\u0024ROOT", "\\u0027$ROOT", "\\\\", "\\n$ROOT"
        };
        foreach (string modifier in new[] { ":-", "-", ":+", "+" })
        {
            foreach (string content in contents)
            {
                VolumeInstruction instruction = VolumeInstruction.Parse(
                    $"VOLUME [\"${{OUTER:-${{INNER{modifier}placeholder}}}}/$ROOT\"]");
                VariableRefToken outer = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>(),
                    token => token.VariableName == "OUTER");
                VariableRefToken inner = Assert.Single(outer.ModifierValueToken!.Tokens.OfType<VariableRefToken>());
                LiteralToken literal = new(new[] { new StringToken(content) }, canContainVariables: false, '\\');
                inner.ModifierValueToken = literal;
                string original = instruction.ToString();
                Dictionary<string, string?> variables = new() { ["ROOT"] = "value" };
                if (modifier.Last() == '+')
                {
                    variables["INNER"] = "set";
                }
                string decoded = System.Text.Json.JsonSerializer.Deserialize<string>($"\"{content}\"")!;
                VariableRefToken expectedInner = new("INNER", modifier, "placeholder");
                expectedInner.ModifierValueToken = new LiteralToken(new[] { new StringToken(decoded) },
                    canContainVariables: false, '\\');
                VariableRefToken expectedOuter = new("OUTER", ":-", "placeholder");
                expectedOuter.ModifierValueToken = new LiteralToken(new Token[] { expectedInner },
                    canContainVariables: true, '\\');
                string expectedValue = expectedOuter.ResolveVariables('\\', variables,
                    new ResolutionOptions { RemoveEscapeCharacters = removeEscapes }) + "/value";
                string expected = $"VOLUME [\"{System.Text.Json.JsonEncodedText.Encode(expectedValue)}\"]";

                Assert.Equal(expected, instruction.ResolveVariables('\\', variables,
                    new ResolutionOptions { UpdateInline = inline, RemoveEscapeCharacters = removeEscapes }));
                Assert.Equal(inline ? expected : original, instruction.ToString());
                if (inline)
                {
                    Assert.Equal(expected, instruction.ResolveVariables('\\',
                        new Dictionary<string, string?> { ["ROOT"] = "unexpected" }));
                }
                else
                {
                    Assert.Same(literal, inner.ModifierValueToken);
                }
            }
        }
    }

    [Theory]
    [InlineData("?", false)]
    [InlineData(":?", true)]
    public void JsonOpaqueRequiredModifierRemainsLazy(string modifier, bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"${{ROOT{modifier}placeholder}}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        reference.ModifierValueToken = new LiteralToken("$DETAIL");
        string original = instruction.ToString();
        ResolutionOptions options = new() { UpdateInline = inline };

        Assert.Equal("VOLUME [\"value\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "value" }, new ResolutionOptions()));
        VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
            instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["DETAIL"] = "unexpected" }, options));
        Assert.Contains("'$DETAIL'", error.Message);
        Assert.Equal(original, instruction.ToString());
    }

    [Fact]
    public void JsonDisabledExpansionAvoidsDecodedModelAllocations()
    {
        string content = new('a', 65_536);
        LiteralToken path = new(content, canContainVariables: false) { QuoteChar = '"' };
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME []");
        instruction.PathTokens.Add(path);
        string physical = path.ToString();
        string logical = ConstructReader.FoldHeader(physical, 0, '\\', out _, new List<int>());
        List<int> offsets = Enumerable.Range(0, physical.Length).ToList();
        ResolutionOptions options = new();
        Dictionary<string, string?> variables = new();
        JsonStringVariableResolver.Resolve(path, physical, logical, offsets, '\\', variables, options,
            expandVariables: false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        string resolved = JsonStringVariableResolver.Resolve(path, physical, logical, offsets, '\\', variables, options,
            expandVariables: false);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Same(physical, resolved);
        Assert.True(allocated < 1_000, $"Disabled expansion allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData("/data")]
    [InlineData("`$ROOT")]
    public void JsonRawPathsWithoutReferenceCandidatesAvoidParserAllocations(string content)
    {
        LiteralToken path = new(content, escapeChar: '`') { QuoteChar = '"' };
        JsonStringVariableResolver.Tokenize(path, '`');
        long before = GC.GetAllocatedBytesForCurrentThread();
        LiteralToken result = JsonStringVariableResolver.Tokenize(path, '`');
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(path.ToString(), result.ToString());
        Assert.True(allocated < 2_500, $"Candidate-free tokenization allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonDeepRawReferencesUseStackSafeResolutionSerialization(bool protectedText)
    {
        string content = string.Concat(Enumerable.Repeat("${X:-", 4096)) + "value" + new string('}', 4096);
        if (protectedText)
        {
            content = "'" + content + "'";
        }
        string text = $"VOLUME [\"{content}\"]";
        VolumeInstruction instruction = VolumeInstruction.Parse(text);
        Token[] tokens = instruction.PathTokens[0].Tokens.ToArray();

        Assert.Equal(protectedText ? text : "VOLUME [\"value\"]", instruction.ResolveVariables('\\'));
        Assert.Equal(tokens, instruction.PathTokens[0].Tokens);
    }

    [Theory]
    [InlineData(1, "\\")]
    [InlineData(2, "")]
    [InlineData(3, "")]
    public void JsonDeferredFormattingPreservesCrossFragmentEscapePairs(int depth, string expected)
    {
        string content = string.Concat(Enumerable.Repeat("${X:-", depth)) + "${A}${B}" + new string('}', depth);
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"{content}\"]");
        string result = instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["A"] = "\\\\", ["B"] = "\\\\" },
            new ResolutionOptions { RemoveEscapeCharacters = true })!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));

        Assert.Equal(expected, json.RootElement[0].GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonAccumulatingNestedModifiersHaveBoundedAllocations(bool removeEscapes)
    {
        const int depth = 4096;
        string content = string.Concat(Enumerable.Repeat("${X:-a", depth)) +
            "value" + new string('}', depth);
        VariableRefToken reference = DecodedVariableParsers.Variable('\\', Character.AnyChar, content).Parse(content);
        Dictionary<string, string?> variables = new();
        ResolutionOptions options = new() { RemoveEscapeCharacters = removeEscapes };
        JsonStringVariableResolver.ResolveReference(reference, '\\', variables, options);
        long before = GC.GetAllocatedBytesForCurrentThread();
        string result = JsonStringVariableResolver.ResolveReference(reference, '\\', variables, options);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal($"{new string('a', depth)}value", result);
        Assert.True(allocated < 12_000_000, $"Accumulating modifiers allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "")]
    [InlineData(4, "")]
    [InlineData(1, "a")]
    [InlineData(2, "a")]
    [InlineData(4, "a")]
    public void JsonDeferredFormattingMatchesModifierResolution(int depth, string suffix)
    {
        string content = string.Concat(Enumerable.Repeat("${X:-", depth)) +
            "${A}${B}" + suffix + new string('}', depth);
        VariableRefToken reference = VariableRefToken.Parse(content);
        ResolutionOptions options = new() { RemoveEscapeCharacters = true };
        string[] values = { "", "\\", "\\\\", "a\\", "\\a", "a" };
        foreach (string first in values)
        {
            foreach (string second in values)
            {
                Dictionary<string, string?> variables = new() { ["A"] = first, ["B"] = second };
                Assert.Equal(reference.ResolveVariables('\\', variables, options),
                    JsonStringVariableResolver.ResolveReference(reference, '\\', variables, options));
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void JsonProtectedReferenceResolutionHasBoundedAllocations(bool variables, bool escaped)
    {
        string content = string.Concat(Enumerable.Repeat(escaped ? "`$ROOT/" : "$ROOT/", 1024));
        if (variables && !escaped)
        {
            content = "'" + content + "'";
        }
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME []", '`');
        LiteralToken path = new(new[] { new StringToken(content) }, variables, '`') { QuoteChar = '"' };
        instruction.PathTokens.Add(path);
        path.ResolveVariables('`');
        long before = GC.GetAllocatedBytesForCurrentThread();
        string? result = path.ResolveVariables('`');
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal($"\"{content}\"", result);
        Assert.True(allocated < 500_000, $"Protected references allocated {allocated} bytes.");
    }

    [Fact]
    public void JsonOwnerResolutionPreservesOptionsIdentity()
    {
        ResolutionOptions options = new() { UpdateInline = true, RemoveEscapeCharacters = true };
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${MISSING:-placeholder}\"]");
        ContextLiteralToken literal = new();
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken = literal;

        Assert.Equal("VOLUME [\"inline\"]", instruction.ResolveVariables('\\', options: options));
        Assert.Same(options, literal.SeenOptions);
        Assert.True(options.UpdateInline);
        Assert.True(options.RemoveEscapeCharacters);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonSharedModifiersRetainTheirReferenceResolutionContext(bool inline)
    {
        LiteralToken modifier = new("\"\\u0024ROOT\"", canContainVariables: true);
        VariableRefToken reference = new("MISSING", ":-", "placeholder") { ModifierValueToken = modifier };
        string expected = reference.ResolveVariables('\\')!;
        VolumeInstruction volume = VolumeInstruction.Parse("VOLUME []");
        volume.PathTokens.Add(modifier);

        Assert.Equal(expected, reference.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
            new ResolutionOptions { UpdateInline = inline }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonDerivedModifiersRetainVariablePolicy(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${MISSING:-placeholder}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        reference.ModifierValueToken = new OpaqueLiteralToken("$ROOT");

        Assert.Equal("VOLUME [\"$ROOT\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
            new ResolutionOptions { UpdateInline = inline }));
    }

    private sealed class OpaqueLiteralToken(string value) : LiteralToken(value, canContainVariables: false);

    [Fact]
    public void JsonDerivedOpaqueModifiersPreserveCustomRendering()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${MISSING:-placeholder}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        reference.ModifierValueToken = new RenderedOpaqueLiteralToken();

        Assert.Equal("VOLUME [\"$ROOT\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" }));
    }

    private sealed class RenderedOpaqueLiteralToken() : LiteralToken("$IGNORED", canContainVariables: false)
    {
        protected override string GetUnderlyingValue(TokenStringOptions options) => "$ROOT";
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonMixedProtectedReferencesHaveBoundedAllocations(bool escaped)
    {
        string protectedText = string.Concat(Enumerable.Repeat(escaped ? "`$ROOT/" : "$ROOT/", 1024));
        if (!escaped)
        {
            protectedText = "'" + protectedText + "'";
        }
        string content = "$ACTIVE/" + protectedText;
        VolumeInstruction volume = VolumeInstruction.Parse("VOLUME []", '`');
        LiteralToken path = new(new[] { new StringToken(content) }, canContainVariables: true, '`')
        {
            QuoteChar = '"'
        };
        volume.PathTokens.Add(path);
        Dictionary<string, string?> variables = new() { ["ACTIVE"] = "value", ["ROOT"] = "unexpected" };
        path.ResolveVariables('`', variables);
        long before = GC.GetAllocatedBytesForCurrentThread();
        string? result = path.ResolveVariables('`', variables);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal($"\"value/{protectedText}\"", result);
        Assert.True(allocated < 650_000, $"Mixed protected references allocated {allocated} bytes.");
    }

    [Fact]
    public void JsonPublicDisabledExpansionAvoidsOffsetMapAllocations()
    {
        LiteralToken path = new(new string('a', 65_536), canContainVariables: false) { QuoteChar = '"' };
        VolumeInstruction volume = VolumeInstruction.Parse("VOLUME []");
        volume.PathTokens.Add(path);
        path.ResolveVariables('\\');
        long before = GC.GetAllocatedBytesForCurrentThread();
        string expected = JsonStringVariableResolver.RenderToken(path);
        long rendering = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        string? result = path.ResolveVariables('\\');
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(expected, result);
        Assert.True(allocated - rendering < 10_000,
            $"Disabled expansion allocated {allocated - rendering} bytes above rendering.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonDisabledExpansionStillFormatsEscapes(bool inline)
    {
        LiteralToken path = new("C:\\\\cache", canContainVariables: false) { QuoteChar = '"' };
        VolumeInstruction volume = VolumeInstruction.Parse("VOLUME []");
        volume.PathTokens.Add(path);
        string original = volume.ToString();

        Assert.Equal("VOLUME [\"C:cache\"]", volume.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline, RemoveEscapeCharacters = true }));
        Assert.Equal(original, volume.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonDerivedVariableAwareModifiersRetainNestedLiteralPolicies(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${MISSING:-placeholder}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        LiteralToken modifier = new TransparentLiteralToken("${INNER:-$ROOT}");
        Assert.Single(modifier.Tokens.OfType<VariableRefToken>()).ModifierValueToken = new LiteralToken("$ROOT", false);
        reference.ModifierValueToken = modifier;
        string original = instruction.ToString();

        Assert.Equal("VOLUME [\"$ROOT\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
            new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(inline ? "VOLUME [\"$ROOT\"]" : original, instruction.ToString());
    }

    private sealed class TransparentLiteralToken(string value) : LiteralToken(value, canContainVariables: true);

    [Theory]
    [InlineData(false, false, "placeholder")]
    [InlineData(true, false, "placeholder")]
    [InlineData(false, true, "placeholder")]
    [InlineData(true, true, "placeholder")]
    [InlineData(false, false, "")]
    [InlineData(true, false, "")]
    [InlineData(false, true, "")]
    [InlineData(true, true, "")]
    [InlineData(false, false, "\\u0024ROOT")]
    [InlineData(true, false, "\\u0024ROOT")]
    [InlineData(false, false, "${BROKEN")]
    [InlineData(true, false, "'")]
    public void JsonModifiersPreserveVirtualResolution(bool inline, bool nested, string text)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse(nested
            ? "VOLUME [\"${OUTER:-${INNER:-placeholder}}\"]"
            : "VOLUME [\"${INNER:-placeholder}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        if (nested)
        {
            reference = Assert.Single(reference.ModifierValueToken!.Tokens.OfType<VariableRefToken>());
        }
        ResolvingLiteralToken modifier = new(text);
        reference.ModifierValueToken = modifier;
        string original = instruction.ToString();

        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(1, modifier.ResolutionCount);
        Assert.Equal(inline ? "VOLUME [\"custom\"]" : original, instruction.ToString());
    }

    [Theory]
    [InlineData(":-", false)]
    [InlineData(":+", true)]
    [InlineData(":?", false)]
    public void JsonCustomModifierResolutionRemainsLazy(string operation, bool set)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"${{INNER{operation}placeholder}}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        ResolvingLiteralToken modifier = new("placeholder");
        reference.ModifierValueToken = modifier;
        Dictionary<string, string?> variables = new();
        if (set)
        {
            variables["INNER"] = "present";
        }

        if (operation == ":?")
        {
            VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
                instruction.ResolveVariables('\\', variables));
            Assert.Contains("custom", error.Message);
        }
        else
        {
            Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\', variables));
        }
        Assert.Equal(1, modifier.ResolutionCount);

        modifier.ResolutionCount = 0;
        variables.Clear();
        if (!set)
        {
            variables["INNER"] = "present";
        }
        Assert.Equal(operation == ":+" ? "VOLUME [\"\"]" : "VOLUME [\"present\"]",
            instruction.ResolveVariables('\\', variables));
        Assert.Equal(0, modifier.ResolutionCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonCustomModifierResultsAreEncodedAndFormattedOnce(bool inline, bool removeEscapes)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${INNER:-placeholder}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        ResolvingLiteralToken modifier = new("placeholder") { Result = "C:\\\\cache\"$ROOT" };
        reference.ModifierValueToken = modifier;
        string original = instruction.ToString();

        string result = instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
            new ResolutionOptions { UpdateInline = inline, RemoveEscapeCharacters = removeEscapes })!;

        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
        Assert.Equal(1, json.RootElement.GetArrayLength());
        Assert.Equal(removeEscapes ? "C:\\cache\"$ROOT" : modifier.Result, json.RootElement[0].GetString());
        Assert.Equal(1, modifier.ResolutionCount);
        Assert.Equal(inline ? result : original, instruction.ToString());
    }

    private sealed class ResolvingLiteralToken(string value) : LiteralToken(value)
    {
        public int ResolutionCount { get; set; }
        public string Result { get; init; } = "custom";

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            return Result;
        }
    }

    [Fact]
    public void JsonSemanticPathReplacementReleasesDetachedSubstitutionTracking()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        LiteralToken path = instruction.PathTokens[0];
        instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["ROOT"] = new string('a', 65_536) },
            new ResolutionOptions { UpdateInline = true });
        Token substitution = Assert.Single(path.Tokens);
        Assert.True(path.IsResolvedJsonLiteral(substitution));

        Assert.Throws<ArgumentException>(() => instruction.Paths[0] = "invalid\"value");
        Assert.True(path.IsResolvedJsonLiteral(substitution));
        instruction.Paths.Add("/extra");
        Assert.True(path.IsResolvedJsonLiteral(substitution));

        instruction.Paths[0] = "/small";

        Assert.Same(path, instruction.PathTokens[0]);
        Assert.False(path.IsResolvedJsonLiteral(substitution));
        Assert.Equal("VOLUME [\"/small\", \"/extra\"]", instruction.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonRawTokenizationWithoutBracesHasBoundedAllocations(bool simpleReference)
    {
        string content = (simpleReference ? "$ROOT/" : "/") + new string('a', 65_536);
        LiteralToken path = new(new[] { new StringToken(content) }, canContainVariables: false, '\\')
        {
            QuoteChar = '"'
        };
        JsonStringVariableResolver.Tokenize(path, '\\');
        long before = GC.GetAllocatedBytesForCurrentThread();
        LiteralToken result = JsonStringVariableResolver.Tokenize(path, '\\');
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal($"\"{content}\"", result.ToString());
        Assert.Equal(simpleReference ? 1 : 0, result.Tokens.OfType<VariableRefToken>().Count());
        Assert.True(allocated < 600_000, $"Brace-free tokenization allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(false, "literal")]
    [InlineData(true, "literal")]
    [InlineData(false, "$ROOT")]
    [InlineData(true, "$ROOT")]
    [InlineData(false, "'$ROOT")]
    [InlineData(true, "'$ROOT")]
    public void JsonSingleQuotedModifiersConsumeOpaqueSpans(bool inline, string content)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${OUTER:-'${INNER:-placeholder}'}\"]");
        VariableRefToken outer = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        VariableRefToken inner = Assert.Single(outer.ModifierValueToken!.Tokens.OfType<VariableRefToken>());
        inner.ModifierValueToken = new LiteralToken(new[] { new StringToken(content) }, false, '\\');
        string original = instruction.ToString();

        string result = instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
            new ResolutionOptions { UpdateInline = inline })!;

        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
        Assert.Equal($"'${{INNER:-{content}}}'", json.RootElement[0].GetString());
        Assert.Equal(inline ? result : original, instruction.ToString());
    }

    [Fact]
    public void JsonResolutionContextPreservesOptionsSubtype()
    {
        TaggedResolutionOptions options = new() { Label = "custom", UpdateInline = true, RemoveEscapeCharacters = true };
        ContextLiteralToken literal = new();
        WorkdirInstruction instruction = new("/data") { PathToken = literal };

        Assert.Equal("WORKDIR custom", instruction.ResolveVariables('\\', options: options));
        Assert.Same(options, literal.SeenOptions);
        Assert.Equal(options.Label, Assert.IsType<TaggedResolutionOptions>(literal.SeenOptions).Label);
        Assert.True(options.UpdateInline);
        Assert.True(options.RemoveEscapeCharacters);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonCustomOptionsReachOrdinaryLiteralOverrides(bool inline)
    {
        ContextLiteralToken literal = new();
        WorkdirInstruction instruction = new("/data") { PathToken = literal };
        TaggedResolutionOptions options = new() { Label = "custom", UpdateInline = inline };

        Assert.Equal("WORKDIR custom", instruction.ResolveVariables('\\', options: options));
        Assert.Equal(options.Label, Assert.IsType<TaggedResolutionOptions>(literal.SeenOptions).Label);
        Assert.Equal(inline, literal.SeenOptions!.UpdateInline);
        Assert.Same(options, literal.SeenOptions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonCustomModifierReceivesCallerContext(bool inline, bool nested)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse(nested
            ? "VOLUME [\"${OUTER:-${INNER:-placeholder}}\"]"
            : "VOLUME [\"${INNER:-placeholder}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        if (nested)
        {
            reference = Assert.Single(reference.ModifierValueToken!.Tokens.OfType<VariableRefToken>());
        }
        ContextLiteralToken literal = new();
        reference.ModifierValueToken = literal;
        string original = instruction.ToString();
        ResolutionOptions options = new() { UpdateInline = inline };

        string result = instruction.ResolveVariables('\\', options: options)!;

        Assert.Equal(inline ? "VOLUME [\"inline\"]" : "VOLUME [\"detached\"]", result);
        Assert.Equal(inline, literal.SeenOptions!.UpdateInline);
        Assert.Equal(inline ? result : original, instruction.ToString());

        if (!inline)
        {
            TaggedResolutionOptions tagged = new() { Label = "custom" };
            Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\', options: tagged));
            Assert.Equal(tagged.Label, Assert.IsType<TaggedResolutionOptions>(literal.SeenOptions).Label);
        }
    }

    [Theory]
    [InlineData(":-", false)]
    [InlineData("-", false)]
    [InlineData(":+", true)]
    [InlineData("+", true)]
    [InlineData(":?", false)]
    [InlineData("?", false)]
    public void JsonCustomModifierContextRemainsLazy(string operation, bool set)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"${{INNER{operation}placeholder}}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        ContextLiteralToken literal = new();
        reference.ModifierValueToken = literal;
        TaggedResolutionOptions options = new() { Label = "custom", UpdateInline = true, RemoveEscapeCharacters = true };
        Dictionary<string, string?> variables = new();
        if (!set)
        {
            variables["INNER"] = "present";
        }

        Assert.Equal(operation.Last() == '+' ? "VOLUME [\"\"]" : "VOLUME [\"present\"]",
            instruction.ResolveVariables('\\', variables, options));
        Assert.Null(literal.SeenOptions);

        instruction = VolumeInstruction.Parse($"VOLUME [\"${{INNER{operation}placeholder}}\"]");
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken = literal;
        variables.Clear();
        if (set)
        {
            variables["INNER"] = "present";
        }
        if (operation.Last() == '?')
        {
            VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
                instruction.ResolveVariables('\\', variables, options));
            Assert.Contains("custom", error.Message);
        }
        else
        {
            Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\', variables, options));
        }
        Assert.Equal(options.Label, Assert.IsType<TaggedResolutionOptions>(literal.SeenOptions).Label);
        Assert.True(literal.SeenOptions!.UpdateInline);
        Assert.True(literal.SeenOptions.RemoveEscapeCharacters);
        Assert.Same(options, literal.SeenOptions);
    }

    private sealed class TaggedResolutionOptions : ResolutionOptions
    {
        public string Label { get; init; } = "";
    }

    private sealed class ContextLiteralToken() : LiteralToken("placeholder")
    {
        public ResolutionOptions? SeenOptions { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            SeenOptions = options;
            return options is TaggedResolutionOptions tagged ? tagged.Label :
                options?.UpdateInline == true ? "inline" : "detached";
        }
    }

    [Fact]
    public void JsonBareDollarsAvoidRawReferenceParserAllocations()
    {
        string content = string.Concat(Enumerable.Repeat("$/", 4096));
        LiteralToken raw = new(new[] { new StringToken(content) }, false, '\\') { QuoteChar = '"' };
        JsonStringVariableResolver.Tokenize(raw, '\\');
        long before = GC.GetAllocatedBytesForCurrentThread();
        LiteralToken tokenized = JsonStringVariableResolver.Tokenize(raw, '\\');
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(raw.ToString(), tokenized.ToString());
        Assert.Empty(tokenized.Tokens.OfType<VariableRefToken>());
        Assert.True(allocated < 150_000, $"Bare-dollar tokenization allocated {allocated} bytes.");
    }

    [Fact]
    public void JsonBareDollarsAvoidDecodedReferenceCacheAllocations()
    {
        string content = string.Concat(Enumerable.Repeat("$/", 4096)) + "$ROOT";
        int start = content.Length - "$ROOT".Length;
        DecodedVariableParsers.Variable('\\', Character.AnyChar, content);
        long before = GC.GetAllocatedBytesForCurrentThread();
        TextParser<VariableRefToken> parser = DecodedVariableParsers.Variable('\\', Character.AnyChar, content);
        Result<VariableRefToken> result = parser(new TextSpan(content).Skip(start));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(result.HasValue);
        Assert.Equal("ROOT", result.Value.VariableName);
        Assert.True(allocated < 150_000, $"Bare-dollar reference caching allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonPlainParsedPathsAvoidDecodedModelAllocations(bool inline, bool removeEscapes)
    {
        string content = "/" + new string('a', 65_536);
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"{content}\"]");
        LiteralToken path = instruction.PathTokens[0];
        ResolutionOptions options = new() { UpdateInline = inline, RemoveEscapeCharacters = removeEscapes };
        path.ResolveVariables('\\', options: options);
        long before = GC.GetAllocatedBytesForCurrentThread();
        string expected = JsonStringVariableResolver.RenderToken(path);
        long rendering = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        string? result = path.ResolveVariables('\\', options: options);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(expected, result);
        Assert.Equal(expected, path.ToString());
        Assert.True(allocated - rendering < 10_000,
            $"Plain-path resolution allocated {allocated - rendering} bytes above rendering.");
    }

    [Theory]
    [InlineData("$", "$")]
    [InlineData("$-", "$-")]
    [InlineData("$/", "$/")]
    [InlineData("$$ROOT", "$value")]
    [InlineData("$9", "nine")]
    [InlineData("$_ROOT", "hidden")]
    [InlineData("$\\u0052OOT", "value")]
    [InlineData("\\u0024ROOT", "value")]
    [InlineData("$\\\nROOT", "value")]
    public void JsonReferenceCandidateFilteringPreservesVariableBoundaries(string content, string expected)
    {
        foreach (bool inline in new[] { false, true })
        {
            string original = $"VOLUME [\"{content}\"]";
            VolumeInstruction instruction = VolumeInstruction.Parse(original);
            string result = instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "value", ["9"] = "nine", ["_ROOT"] = "hidden" },
                new ResolutionOptions { UpdateInline = inline })!;

            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
            Assert.Equal(expected, json.RootElement[0].GetString());
            Assert.Equal(inline ? result : original, instruction.ToString());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonModifiersPreserveCustomReferenceResolution(bool inline, bool nested)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse(nested
            ? "VOLUME [\"${OUTER:-${INNER:-placeholder}}\"]"
            : "VOLUME [\"${INNER:-placeholder}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        if (nested)
        {
            reference = Assert.Single(reference.ModifierValueToken!.Tokens.OfType<VariableRefToken>());
        }
        CustomResolvingReference custom = new();
        reference.ModifierValueToken = new ReferenceLiteral(custom);
        string original = instruction.ToString();
        TaggedResolutionOptions options = new() { Label = "custom", UpdateInline = inline };

        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" }, options));
        Assert.Equal(1, custom.ResolutionCount);
        Assert.Equal(options.Label, Assert.IsType<TaggedResolutionOptions>(custom.SeenOptions).Label);
        Assert.Equal(inline, custom.SeenOptions!.UpdateInline);
        Assert.Equal(inline ? "VOLUME [\"custom\"]" : original, instruction.ToString());
    }

    private sealed class ReferenceLiteral : LiteralToken
    {
        public ReferenceLiteral(VariableRefToken reference, string prefix = "", string suffix = "")
            : base("placeholder", canContainVariables: true)
        {
            TokenList.Clear();
            TokenList.Add(new StringToken(prefix));
            TokenList.Add(reference);
            TokenList.Add(new StringToken(suffix));
        }
    }

    private sealed class CustomResolvingReference() : VariableRefToken("ROOT")
    {
        public int ResolutionCount { get; private set; }
        public ResolutionOptions? SeenOptions { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            ResolutionCount++;
            SeenOptions = options;
            return options is TaggedResolutionOptions tagged ? tagged.Label : "custom";
        }
    }

    [Theory]
    [InlineData(":-", false)]
    [InlineData("-", false)]
    [InlineData(":+", true)]
    [InlineData("+", true)]
    [InlineData(":?", false)]
    [InlineData("?", false)]
    public void JsonCustomReferencesRemainLazy(string operation, bool selectedWhenSet)
    {
        foreach (bool selected in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"${{OUTER{operation}placeholder}}\"]");
            CustomResolvingReference custom = new();
            Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
                new ReferenceLiteral(custom);
            Dictionary<string, string?> variables = new();
            if (selected == selectedWhenSet)
            {
                variables["OUTER"] = "present";
            }
            if (selected && operation.Last() == '?')
            {
                VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
                    instruction.ResolveVariables('\\', variables));
                Assert.Contains("custom", error.Message);
            }
            else
            {
                Assert.Equal(selected ? "VOLUME [\"custom\"]" :
                    operation.Last() == '+' ? "VOLUME [\"\"]" : "VOLUME [\"present\"]",
                    instruction.ResolveVariables('\\', variables));
            }
            Assert.Equal(selected ? 1 : 0, custom.ResolutionCount);
        }
    }

    [Theory]
    [InlineData("'", "'", "'$ROOT'", 0)]
    [InlineData("\\u0022", "\\u0022", "\"custom\"", 1)]
    public void JsonCustomReferencesRetainDecodedQuoteProtection(
        string prefix, string suffix, string expected, int calls)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${OUTER:-placeholder}\"]");
        CustomResolvingReference custom = new();
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
            new ReferenceLiteral(custom, prefix, suffix);

        string result = instruction.ResolveVariables('\\')!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
        Assert.Equal(expected, json.RootElement[0].GetString());
        Assert.Equal(calls, custom.ResolutionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonDirectCustomReferencesRetainCallerContext(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        CustomResolvingReference custom = new();
        instruction.PathTokens[0].ReplaceWithToken(custom);
        TaggedResolutionOptions options = new() { Label = "C:\\\"$LATER", UpdateInline = inline };
        string original = instruction.ToString();

        string result = instruction.ResolveVariables('\\', options: options)!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
        Assert.Equal(options.Label, json.RootElement[0].GetString());
        Assert.Equal(1, custom.ResolutionCount);
        Assert.Equal(inline, custom.SeenOptions!.UpdateInline);
        Assert.Same(options, custom.SeenOptions);
        Assert.Equal(inline ? result : original, instruction.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonInlinePreservesUntouchedEmptyChildren(bool suffix)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse(suffix
            ? "VOLUME [\"$ROOT/suffix\"]" : "VOLUME [\"prefix$ROOT\"]");
        LiteralToken path = instruction.PathTokens[0];
        StringToken untouched = Assert.IsType<StringToken>(suffix ? path.Tokens.Last() : path.Tokens.First());
        untouched.Value = "";

        Assert.Equal("VOLUME [\"value\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "value" },
            new ResolutionOptions { UpdateInline = true }));
        Assert.Contains(untouched, path.Tokens);
        untouched.Value = "/";
        Assert.Equal(suffix ? "VOLUME [\"value/\"]" : "VOLUME [\"/value\"]", instruction.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonBareDollarPathsAvoidDecodedModelAllocations(bool inline, bool removeEscapes)
    {
        string content = string.Concat(Enumerable.Repeat("$/", 32_768)) + "$-/$";
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"{content}\"]");
        LiteralToken path = instruction.PathTokens[0];
        ResolutionOptions options = new() { UpdateInline = inline, RemoveEscapeCharacters = removeEscapes };
        path.ResolveVariables('\\', options: options);
        long before = GC.GetAllocatedBytesForCurrentThread();
        string expected = JsonStringVariableResolver.RenderToken(path);
        long rendering = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        string? actual = path.ResolveVariables('\\', options: options);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(expected, actual);
        Assert.Equal(expected, path.ToString());
        Assert.True(allocated - rendering < 10_000,
            $"Bare-dollar resolution allocated {allocated - rendering} bytes above rendering.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonEscapedCustomReferencesRemainLiteral(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${OUTER:-placeholder}\"]");
        CustomResolvingReference custom = new();
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
            new ReferenceLiteral(custom, "\\\\");
        string original = instruction.ToString();

        string result = instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline })!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
        Assert.Equal("\\$ROOT", json.RootElement[0].GetString());
        Assert.Equal(0, custom.ResolutionCount);
        Assert.Equal(inline ? result : original, instruction.ToString());
    }

    [Fact]
    public void JsonResolutionDoesNotDuplicateOptionsFinalization()
    {
        FinalizationCounter counter = ResolveWithFinalizableOptions();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(1, counter.Count);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static FinalizationCounter ResolveWithFinalizableOptions()
    {
        FinalizationCounter counter = new();
        FinalizableResolutionOptions options = new(counter);
        WorkdirInstruction instruction = new("/data") { PathToken = new ContextLiteralToken() };
        instruction.ResolveVariables('\\', options: options);
        return counter;
    }

    private sealed class FinalizationCounter
    {
        internal int Count;
    }

    private sealed class FinalizableResolutionOptions(FinalizationCounter counter) : ResolutionOptions
    {
        ~FinalizableResolutionOptions() => System.Threading.Interlocked.Increment(ref counter.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonEmptyRenderedCustomReferencesRemainEvaluable(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${OUTER:-placeholder}\"]");
        int calls = 0;
        CallbackReference custom = new("", _ => { calls++; return "custom"; });
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
            new ReferenceLiteral(custom, "before", "after");
        string original = instruction.ToString();

        Assert.Equal("VOLUME [\"beforecustomafter\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(1, calls);
        Assert.Equal(inline ? "VOLUME [\"beforecustomafter\"]" : original, instruction.ToString());
    }

    [Fact]
    public void JsonSharedCustomReferencesEvaluateEachOccurrence()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${OUTER:-placeholder}\"]");
        int calls = 0;
        CallbackReference custom = new("$ROOT", _ => (++calls).ToString());
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
            new TokenSequenceLiteral(custom, custom);

        Assert.Equal("VOLUME [\"21\"]", instruction.ResolveVariables('\\'));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(":-", false)]
    [InlineData("-", false)]
    [InlineData(":+", true)]
    [InlineData("+", true)]
    [InlineData(":?", false)]
    [InlineData("?", false)]
    public void JsonCustomCallbacksDoNotReselectAncestorBranches(string operation, bool initiallySet)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"${{OUTER{operation}placeholder}}\"]");
        CallbackReference custom = new("$ROOT", variables =>
        {
            if (initiallySet)
            {
                variables!.Remove("OUTER");
            }
            else
            {
                variables!["OUTER"] = "new";
            }
            return "custom";
        });
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
            new ReferenceLiteral(custom);
        Dictionary<string, string?> variables = new();
        if (initiallySet)
        {
            variables["OUTER"] = "present";
        }
        if (operation.Last() == '?')
        {
            VariableSubstitutionException error = Assert.Throws<VariableSubstitutionException>(() =>
                instruction.ResolveVariables('\\', variables));
            Assert.Contains("custom", error.Message);
        }
        else
        {
            Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\', variables));
        }
    }

    [Theory]
    [InlineData("", "after", "customafter", 1)]
    [InlineData("before", "", "beforecustom", 1)]
    [InlineData("", "", "custom", 1)]
    [InlineData("\\u0022", "\\u0022", "\"custom\"", 1)]
    [InlineData("'", "'", "''", 0)]
    public void JsonEmptyReferencesRetainModifierBoundaries(
        string prefix, string suffix, string expected, int expectedCalls)
    {
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${OUTER:-placeholder}\"]");
            int calls = 0;
            CallbackReference custom = new("", _ => { calls++; return "custom"; });
            Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
                new ReferenceLiteral(custom, prefix, suffix);
            string original = instruction.ToString();

            string result = instruction.ResolveVariables('\\',
                options: new ResolutionOptions { UpdateInline = inline })!;
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
            Assert.Equal(expected, json.RootElement[0].GetString());
            Assert.Equal(expectedCalls, calls);
            Assert.Equal(inline ? result : original, instruction.ToString());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    public void JsonDirectCustomReferencesResolveWithoutVariableSpelling(string rendering)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        CallbackReference custom = new(rendering, _ => { calls++; return "$LATER"; });
        instruction.PathTokens[0].ReplaceWithToken(custom);
        ResolutionOptions options = new() { UpdateInline = true };

        Assert.Equal("VOLUME [\"$LATER\"]", instruction.ResolveVariables('\\', options: options));
        Assert.Equal("VOLUME [\"$LATER\"]", instruction.ResolveVariables('\\', options: options));
        Assert.Equal(1, calls);
        Assert.DoesNotContain(custom, instruction.PathTokens[0].Tokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonSharedEmptyReferencesRemainLazyAndEvaluateEachOccurrence(bool selected)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${OUTER:-placeholder}\"]");
        int calls = 0;
        CallbackReference custom = new("", _ => (++calls).ToString());
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
            new TokenSequenceLiteral(custom, custom);
        Dictionary<string, string?> variables = new();
        if (!selected)
        {
            variables["OUTER"] = "present";
        }

        Assert.Equal(selected ? "VOLUME [\"21\"]" : "VOLUME [\"present\"]",
            instruction.ResolveVariables('\\', variables));
        Assert.Equal(selected ? 2 : 0, calls);
    }

    [Fact]
    public void JsonResolutionContextRestoresAfterReentrancyAndExceptions()
    {
        Dictionary<string, string?> variables = new() { ["ROOT"] = "value" };
        CallbackLiteral literal = new();
        LiteralToken unrelated = new("\"\\u0024ROOT\"", canContainVariables: true)
        {
            ResolveAsJsonString = true
        };
        bool nested = false;
        literal.BeforeResolve = () =>
        {
            if (!nested)
            {
                nested = true;
                Assert.Equal("\"\\u0024ROOT\"", literal.ResolveVariablesInContext(false, '\\', variables, null));
                Assert.Equal("\"value\"", unrelated.ResolveVariables('\\', variables));
                nested = false;
            }
        };

        Assert.Equal("\"value\"", literal.ResolveVariablesInContext(true, '\\', variables, null));
        literal.BeforeResolve = () => throw new InvalidOperationException("callback");
        Assert.Throws<InvalidOperationException>(() =>
            literal.ResolveVariablesInContext(true, '\\', variables, null));
        literal.BeforeResolve = null;
        Assert.Equal("\"\\u0024ROOT\"", literal.ResolveVariables('\\', variables));
    }

    [Fact]
    public void JsonResolutionContextsRemainThreadLocal()
    {
        using System.Threading.Barrier barrier = new(2);
        System.Threading.Tasks.Parallel.For(0, 2, index =>
        {
            CallbackLiteral literal = new() { BeforeResolve = () => barrier.SignalAndWait() };
            string? result = literal.ResolveVariablesInContext(index == 0, '\\',
                new Dictionary<string, string?> { ["ROOT"] = "value" }, null);
            Assert.Equal(index == 0 ? "\"value\"" : "\"\\u0024ROOT\"", result);
        });
    }

    private sealed class CallbackLiteral() : LiteralToken("\"\\u0024ROOT\"", canContainVariables: true)
    {
        internal Action? BeforeResolve { get; set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            BeforeResolve?.Invoke();
            return base.ResolveVariables(escapeChar, variables, options);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonEmptyReferenceReplacementPreservesEmptySiblingOrder(bool nested)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        LiteralToken path = instruction.PathTokens[0];
        StringToken prefix = new("");
        StringToken suffix = new("");
        CallbackReference custom = new("", _ => "custom");
        Token[] children = { prefix, custom, suffix };
        path.ReplaceWithTokens(nested ? new Token[] { new TokenSequenceLiteral(children) } : children);

        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        prefix.Value = "before";
        suffix.Value = "after";
        Assert.Equal("VOLUME [\"beforecustomafter\"]", instruction.ToString());
    }

    [Fact]
    public void JsonOuterContextSurvivesReentryThroughAnotherLiteral()
    {
        Dictionary<string, string?> variables = new() { ["ROOT"] = "value" };
        CallbackLiteral outer = new();
        CallbackLiteral inner = new();
        bool entered = false;
        outer.BeforeResolve = () =>
        {
            if (!entered)
            {
                entered = true;
                inner.ResolveVariablesInContext(false, '\\', variables, null);
                entered = false;
            }
        };
        inner.BeforeResolve = () => Assert.Equal("\"value\"", outer.ResolveVariables('\\', variables));

        Assert.Equal("\"value\"", outer.ResolveVariablesInContext(true, '\\', variables, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    public void JsonPlainPathsEvaluateNestedCustomReferences(string rendering)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        CallbackReference custom = new(rendering, _ => { calls++; return "custom"; });
        instruction.PathTokens[0].ReplaceWithToken(new TokenSequenceLiteral(custom));

        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\'));
        Assert.Equal(1, calls);
        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\'));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void JsonPlainPathShortcutHonorsNestedDisabledVariablePolicy()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        CallbackReference custom = new("", _ => { calls++; return "unexpected"; });
        LiteralToken opaque = new("", canContainVariables: false);
        opaque.ReplaceWithToken(custom);
        instruction.PathTokens[0].ReplaceWithToken(new TokenSequenceLiteral(opaque));

        Assert.Equal("VOLUME [\"\"]", instruction.ResolveVariables('\\'));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ShellVolumeChecksOwnerContextOnce()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse(
            "VOLUME " + string.Join(" ", Enumerable.Repeat("$ROOT", 128)));
        CountingSymbolToken symbol = new();
        instruction.TokenList.Add(symbol);

        Assert.Equal("VOLUME " + string.Join(" ", Enumerable.Repeat("value", 128)) + "!",
            instruction.ResolveVariables('\\', new Dictionary<string, string?> { ["ROOT"] = "value" }));
        Assert.Equal(2, symbol.RenderCount);
    }

    private sealed class CountingSymbolToken() : SymbolToken('!')
    {
        internal int RenderCount { get; private set; }

        protected override string GetUnderlyingValue(TokenStringOptions options)
        {
            RenderCount++;
            return base.GetUnderlyingValue(options);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonEmptyReferencesAtNonemptyContainerBoundariesResolveOnce(bool suffix)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        CallbackReference custom = new("", _ => { calls++; return "custom"; });
        instruction.PathTokens[0].ReplaceWithToken(new ReferenceLiteral(custom,
            suffix ? "/data" : "", suffix ? "" : "/data"));
        string expected = suffix ? "VOLUME [\"/datacustom\"]" : "VOLUME [\"custom/data\"]";

        Assert.Equal(expected, instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(expected, instruction.ResolveVariables('\\'));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void JsonEmptyReferenceReplacementRetainsUnrelatedEmptyLiteral()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        LiteralToken untouched = new("", canContainVariables: true);
        CallbackReference custom = new("", _ => "custom");
        instruction.PathTokens[0].ReplaceWithTokens(new Token[] { untouched, custom });

        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        Assert.Contains(untouched, instruction.PathTokens[0].Tokens);
        untouched.Value = "before";
        Assert.Equal("VOLUME [\"beforecustom\"]", instruction.ToString());
    }

    [Theory]
    [InlineData(false, "placeholder")]
    [InlineData(true, "placeholder")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "'${broken")]
    [InlineData(true, "'${broken")]
    public void JsonNestedCustomLiteralOverridesResolve(bool inline, string rendering)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        ResolvingLiteralToken custom = new(rendering);
        instruction.PathTokens[0].ReplaceWithToken(new TokenSequenceLiteral(custom));

        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(1, custom.ResolutionCount);
        Assert.Equal(inline ? "VOLUME [\"custom\"]" : $"VOLUME [\"{rendering}\"]", instruction.ToString());
        if (inline)
        {
            Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\'));
            Assert.Equal(1, custom.ResolutionCount);
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("placeholder", false)]
    [InlineData("placeholder", true)]
    public void JsonCustomLiteralsInsideModifierSequencesStayLazy(string rendering, bool suffix)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${ROOT:-placeholder}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        ResolvingLiteralToken custom = new(rendering);
        reference.ModifierValueToken = suffix
            ? new TokenSequenceLiteral(new StringToken("before"), custom)
            : new TokenSequenceLiteral(custom, new StringToken("after"));

        Assert.Equal("VOLUME [\"present\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "present" }));
        Assert.Equal(0, custom.ResolutionCount);
        Assert.Equal(suffix ? "VOLUME [\"beforecustom\"]" : "VOLUME [\"customafter\"]",
            instruction.ResolveVariables('\\', options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(1, custom.ResolutionCount);
        Assert.Equal(instruction.ToString(), instruction.ResolveVariables('\\'));
        Assert.Equal(1, custom.ResolutionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonModifierCustomLiteralDispatchRetainsAdjacentEmptyOccurrences(bool suffix)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${ROOT:-placeholder}\"]");
        ResolvingLiteralToken empty = new("") { Result = "empty" };
        ResolvingLiteralToken visible = new("placeholder");
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
            suffix ? new TokenSequenceLiteral(visible, empty) : new TokenSequenceLiteral(empty, visible);

        Assert.Equal(suffix ? "VOLUME [\"customempty\"]" : "VOLUME [\"emptycustom\"]",
            instruction.ResolveVariables('\\'));
        Assert.Equal(1, empty.ResolutionCount);
        Assert.Equal(1, visible.ResolutionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonSharedEmptyCustomLiteralsResolveEachOccurrence(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        ResolvingLiteralToken custom = new("");
        LiteralToken untouched = new("", canContainVariables: true);
        instruction.PathTokens[0].ReplaceWithToken(new TokenSequenceLiteral(
            custom, untouched, new StringToken("/"), custom));

        Assert.Equal("VOLUME [\"custom/custom\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(2, custom.ResolutionCount);
        if (inline)
        {
            Assert.Contains(untouched, instruction.PathTokens[0].Tokens);
            untouched.Value = "retained";
            Assert.Equal("VOLUME [\"customretained/custom\"]", instruction.ResolveVariables('\\'));
            Assert.Equal(2, custom.ResolutionCount);
        }
    }

    [Theory]
    [InlineData("'")]
    [InlineData("\\\\")]
    public void JsonProtectedCustomLiteralOverridesStayUnevaluated(string protection)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        ResolvingLiteralToken custom = new("placeholder");
        instruction.PathTokens[0].ReplaceWithToken(new TokenSequenceLiteral(
            new StringToken(protection), custom, new StringToken(protection == "'" ? "'" : "")));

        Assert.Equal(instruction.ToString(), instruction.ResolveVariables('\\'));
        Assert.Equal(0, custom.ResolutionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonNestedCustomLiteralsReceiveOriginalOptions(bool modifier)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse(modifier
            ? "VOLUME [\"${ROOT:-placeholder}\"]" : "VOLUME [\"$ROOT\"]");
        ContextLiteralToken custom = new();
        TokenSequenceLiteral container = new(new StringToken("before"), custom);
        if (modifier)
        {
            Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken = container;
        }
        else
        {
            instruction.PathTokens[0].ReplaceWithToken(container);
        }
        TaggedResolutionOptions options = new() { Label = "custom", UpdateInline = true, RemoveEscapeCharacters = true };

        Assert.Equal("VOLUME [\"beforecustom\"]", instruction.ResolveVariables('\\', options: options));
        Assert.Same(options, custom.SeenOptions);
    }

    [Fact]
    public void JsonRightHandCallbacksUpdateVariablesBeforeLeftHandReferences()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        Dictionary<string, string?> variables = new() { ["ROOT"] = "before" };
        CallbackReference callback = new("callback", values => { values!["ROOT"] = "after"; return ""; });
        instruction.PathTokens[0].ReplaceWithTokens(new Token[] { new VariableRefToken("ROOT"), callback });

        Assert.Equal("VOLUME [\"after\"]", instruction.ResolveVariables('\\', variables));
    }

    [Fact]
    public void JsonDirectReferenceCallbacksRetainReverseChildOrder()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        CallbackReference first = new("$FIRST", _ => (++calls).ToString());
        CallbackReference second = new("$SECOND", _ => (++calls).ToString());
        instruction.PathTokens[0].ReplaceWithTokens(new Token[] { first, second });

        Assert.Equal("VOLUME [\"21\"]", instruction.ResolveVariables('\\'));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false, "\\", "u0061")]
    [InlineData(true, "\\", "u0061")]
    [InlineData(false, "\\", "\\")]
    [InlineData(true, "\\", "\\")]
    [InlineData(false, "p\\u", "0061x")]
    [InlineData(true, "p\\u", "0061x")]
    public void JsonCustomOccurrencesSharingOneDecodedCharacterResolve(bool inline, string firstText, string secondText)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        CallbackReference first = new(firstText, _ => (++calls).ToString());
        CallbackReference second = new(secondText, _ => (++calls).ToString());
        instruction.PathTokens[0].ReplaceWithTokens(new Token[] { first, second });
        string original = $"VOLUME [\"{firstText}{secondText}\"]";
        Assert.Equal(original, instruction.ToString());

        Assert.Equal("VOLUME [\"21\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(2, calls);
        if (inline)
        {
            Assert.Equal("VOLUME [\"21\"]", instruction.ResolveVariables('\\'));
            Assert.Equal(2, calls);
        }
        else
        {
            Assert.Equal(original, instruction.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonNonemptyNestedReplacementRetainsUntouchedChildren(bool quoted)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        StringToken prefix = new("before");
        StringToken suffix = new("/after");
        TokenSequenceLiteral nested = new(prefix, new VariableRefToken("ROOT"), suffix);
        if (quoted)
        {
            nested.QuoteChar = '\'';
            instruction.PathTokens[0].ReplaceWithTokens(
                new Token[] { new StringToken("\\u0022"), nested, new StringToken("\\u0022") });
        }
        else
        {
            instruction.PathTokens[0].ReplaceWithToken(nested);
        }

        string Wrap(string value) => quoted ? $"VOLUME [\"\\u0022'{value}'\\u0022\"]" : $"VOLUME [\"{value}\"]";
        Assert.Equal(Wrap("beforevalue/after"), instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "value" },
            new ResolutionOptions { UpdateInline = true }));
        Assert.Contains(prefix, instruction.PathTokens[0].Tokens);
        Assert.Contains(suffix, instruction.PathTokens[0].Tokens);
        prefix.Value = "edited";
        suffix.Value = "/retained";
        Assert.Equal(Wrap("editedvalue/retained"), instruction.ToString());
    }

    [Fact]
    public void JsonNestedInlineReplacementRetainsUntouchedPoliciesAndReferences()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        LiteralToken opaque = new("$LATER", canContainVariables: false);
        LiteralToken empty = new("", canContainVariables: true);
        VariableRefToken pattern = new("KEEP", "#", "x");
        instruction.PathTokens[0].ReplaceWithToken(new TokenSequenceLiteral(
            opaque, new VariableRefToken("ROOT"), pattern, empty));
        Dictionary<string, string?> variables = new() { ["ROOT"] = "value", ["LATER"] = "unexpected" };

        Assert.Equal("VOLUME [\"$LATERvalue${KEEP#x}\"]", instruction.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = true }));
        Assert.Contains(opaque, instruction.PathTokens[0].Tokens);
        Assert.Contains(pattern, instruction.PathTokens[0].Tokens);
        Assert.Contains(empty, instruction.PathTokens[0].Tokens);
        Assert.Equal(instruction.ToString(), instruction.ResolveVariables('\\', variables));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonRightHandCallbacksUpdateFormattingOptions(bool modifier)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse(modifier
            ? "VOLUME [\"${MISSING:-placeholder}\"]" : "VOLUME [\"$ROOT\"]");
        ResolutionOptions options = new();
        CallbackReference callback = new("callback", _ =>
        {
            options.RemoveEscapeCharacters = true;
            return "";
        });
        TokenSequenceLiteral sequence = new(new VariableRefToken("ROOT"), callback);
        if (modifier)
        {
            Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken = sequence;
        }
        else
        {
            instruction.PathTokens[0].ReplaceWithToken(sequence);
        }

        Assert.Equal("VOLUME [\"cache\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "\\cache" }, options));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void JsonCallbacksChangeFormattingOnlyForPrecedingSource(bool initiallyRemove, bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        ResolutionOptions options = new() { RemoveEscapeCharacters = initiallyRemove, UpdateInline = inline };
        CallbackReference callback = new("callback", _ =>
        {
            options.RemoveEscapeCharacters = !initiallyRemove;
            return "";
        });
        instruction.PathTokens[0].ReplaceWithTokens(new Token[]
        {
            new StringToken("\\\\left/"), callback, new StringToken("/\\\\right")
        });
        string original = instruction.ToString();

        Assert.Equal(initiallyRemove ? "VOLUME [\"\\\\left//right\"]" : "VOLUME [\"left//\\\\right\"]",
            instruction.ResolveVariables('\\', options: options));
        Assert.Equal(inline ? original.Replace("callback", "") : original, instruction.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonOverlappingCustomModifierOccurrencesRemainLazy(bool selected)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${MISSING:-placeholder}\"]");
        int calls = 0;
        CallbackReference first = new("\\", _ => (++calls).ToString());
        CallbackReference second = new("u00", _ => (++calls).ToString());
        CallbackReference third = new("61", _ => (++calls).ToString());
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
            new TokenSequenceLiteral(first, second, third);
        Dictionary<string, string?> variables = new();
        if (!selected)
        {
            variables["MISSING"] = "present";
        }

        Assert.Equal(selected ? "VOLUME [\"321\"]" : "VOLUME [\"present\"]",
            instruction.ResolveVariables('\\', variables));
        Assert.Equal(selected ? 3 : 0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonOverlappingCustomOccurrencesRetainInterveningEmptyReferences(bool modifier)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse(modifier
            ? "VOLUME [\"${MISSING:-placeholder}\"]" : "VOLUME [\"$ROOT\"]");
        int calls = 0;
        TokenSequenceLiteral sequence = new(
            new CallbackReference("\\", _ => (++calls).ToString()),
            new CallbackReference("", _ => (++calls).ToString()),
            new CallbackReference("u0061", _ => (++calls).ToString()));
        if (modifier)
        {
            Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken = sequence;
        }
        else
        {
            instruction.PathTokens[0].ReplaceWithToken(sequence);
        }

        Assert.Equal("VOLUME [\"321\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("VOLUME [\"321\"]", instruction.ResolveVariables('\\'));
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData("'", false)]
    [InlineData("'", true)]
    [InlineData("\\\\", false)]
    [InlineData("\\\\", true)]
    public void JsonOverlappingCustomOccurrencesRetainQuoteAndEscapeProtection(string prefix, bool removeEscapes)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        instruction.PathTokens[0].ReplaceWithTokens(new Token[]
        {
            new StringToken(prefix),
            new CallbackReference("\\", _ => { calls++; return "unexpected"; }),
            new CallbackReference("u0061", _ => { calls++; return "unexpected"; }),
            new StringToken(prefix == "'" ? "'" : "")
        });
        string original = instruction.ToString();

        string result = instruction.ResolveVariables('\\', options: new ResolutionOptions
        {
            UpdateInline = true, RemoveEscapeCharacters = removeEscapes
        })!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
        Assert.Equal(prefix == "'" ? "'a'" : removeEscapes ? "a" : "\\a", json.RootElement[0].GetString());
        Assert.Equal(0, calls);
        Assert.Equal(original, instruction.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonModifierCallbacksFormatPrimitiveChildrenInAggregateOrder(bool initiallyRemove)
    {
        string Resolve(bool json)
        {
            ResolutionOptions options = new() { RemoveEscapeCharacters = initiallyRemove };
            CallbackReference callback = new("callback", _ =>
            {
                options.RemoveEscapeCharacters = !initiallyRemove;
                return "";
            });
            TokenSequenceLiteral modifier = new(
                new StringToken(json ? "\\\\left/" : "\\left/"), callback,
                new StringToken(json ? "/\\\\right" : "/\\right"));
            VariableRefToken reference = new("MISSING", ":-", "placeholder") { ModifierValueToken = modifier };
            if (!json)
            {
                return reference.ResolveVariables('\\', options: options)!;
            }
            VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
            instruction.PathTokens[0].ReplaceWithToken(reference);
            string result = instruction.ResolveVariables('\\', options: options)!;
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(result.Substring(7));
            return document.RootElement[0].GetString()!;
        }

        Assert.Equal(Resolve(false), Resolve(true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("placeholder")]
    public void JsonNullCustomRequiredModifiersRetainEmptyDiagnostic(string rendering)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${MISSING:?placeholder}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        NullResolvingLiteral modifier = new(rendering);
        reference.ModifierValueToken = modifier;
        string expected = Assert.Throws<VariableSubstitutionException>(() =>
            reference.ResolveVariables('\\')).Message;

        Assert.Equal(expected, Assert.Throws<VariableSubstitutionException>(() =>
            instruction.ResolveVariables('\\')).Message);
    }

    private sealed class NullResolvingLiteral(string rendering) : LiteralToken(rendering)
    {
        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null) => null;
    }

    [Fact]
    public void JsonNestedNullCustomRequiredModifiersRetainAggregateDiagnostic()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${MISSING:?placeholder}\"]");
        VariableRefToken reference = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        reference.ModifierValueToken = new TokenSequenceLiteral(new NullResolvingLiteral("placeholder"));
        string expected = Assert.Throws<VariableSubstitutionException>(() =>
            reference.ResolveVariables('\\')).Message;

        Assert.Contains("Error detail: ''.", expected);
        Assert.Equal(expected, Assert.Throws<VariableSubstitutionException>(() =>
            instruction.ResolveVariables('\\')).Message);
    }

    [Fact]
    public void JsonNonInlineEmptyReferencesAvoidSourceTreeReconstruction()
    {
        Token nested = new StringToken("/");
        for (int index = 0; index < 4096; index++)
        {
            nested = new TokenSequenceLiteral(nested);
        }
        VolumeInstruction empty = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        VolumeInstruction visible = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        empty.PathTokens[0].ReplaceWithTokens(new[] { nested, new CallbackReference("", _ => "custom") });
        visible.PathTokens[0].ReplaceWithTokens(new[] { nested, new CallbackReference("x", _ => "custom") });
        empty.ResolveVariables('\\');
        visible.ResolveVariables('\\');
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal("VOLUME [\"/custom\"]", visible.ResolveVariables('\\'));
        long baseline = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal("VOLUME [\"/custom\"]", empty.ResolveVariables('\\'));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated - baseline < 100_000,
            $"Non-inline zero-width reconstruction allocated {allocated - baseline} extra bytes.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonFoldedCustomOccurrencesRemainEvaluable(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        CallbackReference custom = new("\\\n", _ => { calls++; return "custom"; });
        instruction.PathTokens[0].ReplaceWithToken(custom);
        string original = instruction.ToString();

        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(1, calls);
        Assert.Equal(inline ? "VOLUME [\"custom\"]" : original, instruction.ToString());
        if (inline)
        {
            Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\'));
            Assert.Equal(1, calls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonInheritedReferenceRenderingRetainsModifierOverrides(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        ResolvingLiteralToken custom = new("placeholder");
        InheritedReference reference = new() { ModifierValueToken = custom };
        instruction.PathTokens[0].ReplaceWithToken(reference);
        string original = instruction.ToString();

        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(1, custom.ResolutionCount);
        Assert.Equal(inline ? "VOLUME [\"custom\"]" : original, instruction.ToString());
    }

    private sealed class InheritedReference() : VariableRefToken("MISSING", ":-", "placeholder");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonInheritedReferenceRenderingRetainsNestedLiteralPolicies(bool nested)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        InheritedReference reference = new()
        {
            ModifierValueToken = new LiteralToken("$ROOT", canContainVariables: false)
        };
        Token child = nested
            ? new VariableRefToken("OUTER", ":-", "placeholder")
            {
                ModifierValueToken = new TokenSequenceLiteral(reference)
            }
            : reference;
        instruction.PathTokens[0].ReplaceWithToken(child);

        Assert.Equal("VOLUME [\"$ROOT\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
            new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("VOLUME [\"$ROOT\"]", instruction.ResolveVariables('\\'));
    }

    [Theory]
    [InlineData("\\\n", "", "after", '\\')]
    [InlineData("\\\r\n", "before", "", '\\')]
    [InlineData("\\\n# comment\n", "before", "after", '\\')]
    [InlineData("`\r\n", "before", "after", '`')]
    public void JsonFoldedCustomOccurrencesRetainPhysicalReplacementBoundaries(
        string rendering, string prefix, string suffix, char escapeChar)
    {
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]", escapeChar);
            int calls = 0;
            CallbackReference custom = new(rendering, _ => { calls++; return "custom"; });
            instruction.PathTokens[0].ReplaceWithToken(new ReferenceLiteral(custom, prefix, suffix));
            string original = instruction.ToString();
            string expected = $"VOLUME [\"{prefix}custom{suffix}\"]";

            Assert.Equal(expected, instruction.ResolveVariables(escapeChar,
                options: new ResolutionOptions { UpdateInline = inline }));
            Assert.Equal(1, calls);
            Assert.Equal(inline ? expected : original, instruction.ToString());
            if (inline)
            {
                Assert.Equal(expected, instruction.ResolveVariables(escapeChar));
                Assert.Equal(1, calls);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonFoldedCustomModifiersRemainLazy(bool selected, bool wholeModifier)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${ROOT:-placeholder}\"]");
        ResolvingLiteralToken custom = new("placeholder");
        custom.ReplaceWithToken(new StringToken("\\\n"));
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
            wholeModifier ? custom : new TokenSequenceLiteral(new StringToken("before"), custom, new StringToken("after"));
        Dictionary<string, string?> variables = new();
        if (!selected)
        {
            variables["ROOT"] = "present";
        }
        string expected = selected ? wholeModifier ? "custom" : "beforecustomafter" : "present";

        Assert.Equal($"VOLUME [\"{expected}\"]", instruction.ResolveVariables('\\', variables,
            new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(selected ? 1 : 0, custom.ResolutionCount);
        Assert.Equal($"VOLUME [\"{expected}\"]", instruction.ResolveVariables('\\'));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonOpaqueNameBoundariesRemainLiteral(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        LiteralToken opaque = new("ROOT}", canContainVariables: false);
        instruction.PathTokens[0].ReplaceWithTokens(new Token[] { new StringToken("${"), opaque });
        string original = instruction.ToString();

        Assert.Equal(original, instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
            new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(original, instruction.ToString());
        Assert.Contains(opaque, instruction.PathTokens[0].Tokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonEmptyCustomOccurrencesInterruptVariableNames(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        CallbackReference custom = new("", _ => { calls++; return "custom"; });
        instruction.PathTokens[0].ReplaceWithTokens(
            new Token[] { new StringToken("$RO"), custom, new StringToken("OT") });
        string original = instruction.ToString();

        Assert.Equal("VOLUME [\"shortcustomOT\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected", ["RO"] = "short" },
            new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(1, calls);
        Assert.Equal(inline ? "VOLUME [\"shortcustomOT\"]" : original, instruction.ToString());
    }

    [Theory]
    [InlineData("$", "ROOT", "$customROOT")]
    [InlineData("$", "{ROOT}", "$custom{ROOT}")]
    [InlineData("${RO", "OT}", "${ROcustomOT}")]
    [InlineData("${ROOT", "}", "${ROOTcustom}")]
    [InlineData("${ROOT:", "-fallback}", "${ROOT:custom-fallback}")]
    public void JsonEmptyCustomOccurrencesInterruptReferenceSyntax(string prefix, string suffix, string expected)
    {
        foreach (bool inline in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
            int calls = 0;
            CallbackReference custom = new("", _ => { calls++; return "custom"; });
            instruction.PathTokens[0].ReplaceWithToken(new ReferenceLiteral(custom, prefix, suffix));
            string original = instruction.ToString();

            Assert.Equal($"VOLUME [\"{expected}\"]", instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
                new ResolutionOptions { UpdateInline = inline }));
            Assert.Equal(1, calls);
            Assert.Equal(inline ? $"VOLUME [\"{expected}\"]" : original, instruction.ToString());
            if (inline)
            {
                Assert.DoesNotContain(custom, instruction.PathTokens[0].Tokens);
            }
        }
    }

    [Fact]
    public void JsonOpaqueNameBoundariesDoNotHideLaterQuotedReferences()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        instruction.PathTokens[0].ReplaceWithTokens(new Token[]
        {
            new StringToken("\\u0022${"),
            new LiteralToken("ROOT", canContainVariables: false),
            new StringToken("'$NEXT\\u0022")
        });

        Assert.Equal("VOLUME [\"\\u0022${ROOT'value\\u0022\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["NEXT"] = "value" }));
    }

    [Fact]
    public void JsonInlineReplacementAvoidsUnrelatedSourceTreeReconstruction()
    {
        Token nested = new StringToken("/");
        for (int index = 0; index < 4096; index++)
        {
            nested = new TokenSequenceLiteral(nested);
        }
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        LiteralToken path = instruction.PathTokens[0];
        path.ReplaceWithTokens(new[] { nested, new CallbackReference("x", _ => "custom") });
        instruction.ResolveVariables('\\');
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal("VOLUME [\"/custom\"]", instruction.ResolveVariables('\\'));
        long baseline = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal("VOLUME [\"/custom\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Same(nested, path.Tokens.First());
        Assert.True(allocated - baseline < 100_000,
            $"Inline reconstruction allocated {allocated - baseline} bytes for an unrelated subtree.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonEmptyCustomOccurrencesInsideEscapesPreserveValidJson(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        CallbackReference callback = new("", _ => { calls++; return "custom"; });
        instruction.PathTokens[0].ReplaceWithTokens(new Token[]
        {
            new StringToken("\\u"), callback, new StringToken("0061")
        });
        string original = instruction.ToString();

        string result = instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline })!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
        Assert.Equal("customa", json.RootElement[0].GetString());
        Assert.Equal(1, calls);
        Assert.Equal(inline ? result : original, instruction.ToString());
        if (inline)
        {
            Assert.Equal(result, instruction.ResolveVariables('\\'));
            Assert.Equal(1, calls);
        }
    }

    [Theory]
    [InlineData("\\u0061", 1, "a")]
    [InlineData("\\u0061", 2, "a")]
    [InlineData("\\u0061", 3, "a")]
    [InlineData("\\u0061", 4, "a")]
    [InlineData("\\u0061", 5, "a")]
    [InlineData("\\n", 1, "\n")]
    [InlineData("\\\\", 1, "\\")]
    [InlineData("\\\"x\\\"", 1, "\"x\"")]
    public void JsonEmptyCustomOccurrencesUseWholeEscapeInsertionBoundaries(
        string escape, int split, string decoded)
    {
        foreach (bool nested in new[] { false, true })
        {
            VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
            int calls = 0;
            CallbackReference callback = new("", _ => { calls++; return "custom"; });
            Token[] children = { new StringToken(escape.Substring(0, split)), callback,
                new StringToken(escape.Substring(split)) };
            instruction.PathTokens[0].ReplaceWithTokens(nested
                ? new Token[] { new TokenSequenceLiteral(children) } : children);

            string result = instruction.ResolveVariables('\\',
                options: new ResolutionOptions { UpdateInline = true })!;
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
            Assert.Equal("custom" + decoded, json.RootElement[0].GetString());
            Assert.Equal(result, instruction.ResolveVariables('\\'));
            Assert.Equal(1, calls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonMultipleEmptyOccurrencesInsideOneEscapeRetainOrder(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        List<string> calls = new();
        CallbackReference first = new("", _ => { calls.Add("first"); return "first"; });
        CallbackReference second = new("", _ => { calls.Add("second"); return "second"; });
        instruction.PathTokens[0].ReplaceWithToken(new TokenSequenceLiteral(
            new StringToken("\\u"), first, new StringToken("00"), second, new StringToken("61")));

        string original = instruction.ToString();
        Assert.Equal("VOLUME [\"firstsecond\\u0061\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(new[] { "second", "first" }, calls);
        Assert.Equal(inline ? "VOLUME [\"firstsecond\\u0061\"]" : original, instruction.ToString());
        if (inline)
        {
            Assert.Equal(instruction.ToString(), instruction.ResolveVariables('\\'));
            Assert.Equal(2, calls.Count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonCallbacksUpdateOriginalLeftHandReferences(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        VariableRefToken reference = new("ROOT");
        CallbackReference callback = new("callback", _ =>
        {
            reference.VariableName = "NEXT";
            return "";
        });
        instruction.PathTokens[0].ReplaceWithTokens(new Token[] { reference, callback });

        Assert.Equal("VOLUME [\"next\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "old", ["NEXT"] = "next" },
            new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(inline ? "VOLUME [\"next\"]" : "VOLUME [\"$NEXTcallback\"]", instruction.ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonCallbacksUpdateOriginalModifierReferences(bool inline, bool nested)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        VariableRefToken reference = new("MISSING", ":-", "before");
        CallbackReference callback = new("callback", _ =>
        {
            reference.ModifierValue = "after";
            return "";
        });
        TokenSequenceLiteral sequence = new(reference, callback);
        if (nested)
        {
            instruction.PathTokens[0].ReplaceWithToken(new VariableRefToken("OUTER", ":-", "placeholder")
            {
                ModifierValueToken = sequence
            });
        }
        else
        {
            instruction.PathTokens[0].ReplaceWithToken(sequence);
        }

        Assert.Equal("VOLUME [\"after\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal("after", reference.ModifierValue);
    }

    [Fact]
    public void JsonLiveReferencesDoNotBypassCustomModifierRendering()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        VariableRefToken reference = new("MISSING", ":-", "placeholder")
        {
            ModifierValueToken = new RenderedOpaqueLiteralToken()
        };
        instruction.PathTokens[0].ReplaceWithTokens(new Token[]
        {
            reference, new CallbackReference("callback", _ => "")
        });

        Assert.Equal("VOLUME [\"$ROOT\"]", instruction.ResolveVariables('\\'));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonOtherAggregateOverridesRemainEvaluable(bool inline)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        ResolvingAggregate custom = new();
        instruction.PathTokens[0].ReplaceWithToken(custom);
        TaggedResolutionOptions options = new() { Label = "custom", UpdateInline = inline };

        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\', options: options));
        Assert.Equal(1, custom.Calls);
        Assert.Same(options, custom.SeenOptions);
        Assert.Equal(inline ? "VOLUME [\"custom\"]" : "VOLUME [\"placeholder\"]", instruction.ToString());
    }

    [Theory]
    [InlineData(false, "placeholder")]
    [InlineData(true, "placeholder")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public void JsonOtherAggregateOverridesRemainLazyInModifiers(bool selected, string rendering)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"${ROOT:-placeholder}\"]");
        ResolvingAggregate custom = new(rendering);
        Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>()).ModifierValueToken =
            new TokenSequenceLiteral(new StringToken("before"), custom, new StringToken("after"));
        Dictionary<string, string?> variables = new();
        if (!selected)
        {
            variables["ROOT"] = "present";
        }
        TaggedResolutionOptions options = new() { Label = "custom", UpdateInline = true };

        Assert.Equal(selected ? "VOLUME [\"beforecustomafter\"]" : "VOLUME [\"present\"]",
            instruction.ResolveVariables('\\', variables, options));
        Assert.Equal(selected ? 1 : 0, custom.Calls);
        if (selected)
        {
            Assert.Same(options, custom.SeenOptions);
        }
        Assert.Equal(instruction.ToString(), instruction.ResolveVariables('\\'));
    }

    [Theory]
    [InlineData("'", "'")]
    [InlineData("\\\\", "")]
    public void JsonOtherAggregateOverridesRetainProtection(string prefix, string suffix)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        ResolvingAggregate custom = new();
        instruction.PathTokens[0].ReplaceWithToken(new TokenSequenceLiteral(
            new StringToken(prefix), custom, new StringToken(suffix)));
        string original = instruction.ToString();

        Assert.Equal(original, instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(0, custom.Calls);
    }

    private sealed class ResolvingAggregate(string rendering = "placeholder")
        : AggregateToken(new[] { new StringToken(rendering) })
    {
        internal int Calls { get; private set; }
        internal ResolutionOptions? SeenOptions { get; private set; }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null)
        {
            Calls++;
            SeenOptions = options;
            return "custom";
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonInlineEmptyAnchorsAvoidUnrelatedSourceTreeReconstruction(bool prefix)
    {
        Token nested = new StringToken("/");
        for (int index = 0; index < 4096; index++)
        {
            nested = new TokenSequenceLiteral(nested);
        }
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        LiteralToken path = instruction.PathTokens[0];
        Token callback = new CallbackReference("", _ => "custom");
        path.ReplaceWithTokens(prefix ? new[] { callback, nested } : new[] { nested, callback });
        string expected = prefix ? "VOLUME [\"custom/\"]" : "VOLUME [\"/custom\"]";
        instruction.ResolveVariables('\\');
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(expected, instruction.ResolveVariables('\\'));
        long baseline = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(expected, instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Same(nested, prefix ? path.Tokens.Last() : path.Tokens.First());
        Assert.True(allocated - baseline < 100_000,
            $"Empty-anchor reconstruction allocated {allocated - baseline} bytes for an unrelated subtree.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonInlineUsesOriginalNestedOccurrenceLengths(bool shrink, bool modifier)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        VariableRefToken reference = modifier
            ? new("MISSING", ":-", shrink ? "longvalue" : "x")
            : new(shrink ? "LONGER" : "ROOT");
        CallbackReference callback = new("callback", _ =>
        {
            if (modifier)
            {
                reference.ModifierValue = shrink ? "x" : "longvalue";
            }
            else
            {
                reference.VariableName = shrink ? "ROOT" : "LONGER";
            }
            return "";
        });
        StringToken suffix = new("/tail");
        instruction.PathTokens[0].ReplaceWithToken(new TokenSequenceLiteral(reference, callback, suffix));
        string value = modifier ? shrink ? "x" : "longvalue" : "value";
        string expected = $"VOLUME [\"{value}/tail\"]";

        Assert.Equal(expected, instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "value", ["LONGER"] = "value" },
            new ResolutionOptions { UpdateInline = true }));
        Assert.Equal(expected, instruction.ToString());
        Assert.Same(suffix, instruction.PathTokens[0].Tokens.Last());
        Assert.DoesNotContain(callback, instruction.PathTokens[0].Tokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("placeholder")]
    public void JsonDefaultAggregateContainersRetainNestedCallbacks(string rendering)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        CallbackReference callback = new(rendering, _ => { calls++; return "custom"; });
        instruction.PathTokens[0].ReplaceWithToken(new DefaultAggregate(callback));

        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        Assert.Equal("VOLUME [\"custom\"]", instruction.ResolveVariables('\\'));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonDefaultAggregateContainersPreservePolicyAndProtection(bool modifier, bool protect)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        int calls = 0;
        LiteralToken opaque = new("$ROOT", canContainVariables: false);
        DefaultAggregate container = new(opaque, new StringToken("/"),
            new CallbackReference("placeholder", _ => { calls++; return "custom"; }));
        TokenSequenceLiteral sequence = new(new StringToken(protect ? "'" : ""), container,
            new StringToken(protect ? "'" : ""));
        instruction.PathTokens[0].ReplaceWithToken(modifier
            ? new VariableRefToken("MISSING", ":-", "placeholder") { ModifierValueToken = sequence }
            : sequence);

        string result = instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
            new ResolutionOptions { UpdateInline = true })!;
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(result.Substring(7));
        Assert.Equal(protect ? "'$ROOT/placeholder'" : "$ROOT/custom", json.RootElement[0].GetString());
        Assert.Equal(protect ? 0 : 1, calls);
        Assert.Equal(result, instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["ROOT"] = "unexpected" }));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void JsonInlineEligibilityUsesEachOccurrencesEvaluationOptions(bool initiallyInline, bool nested, bool empty)
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        ResolutionOptions options = new() { UpdateInline = initiallyInline };
        CallbackReference callback = new(empty ? "" : "callback", _ =>
        {
            options.UpdateInline = !initiallyInline;
            return "";
        });
        VariableRefToken left = new("LEFT");
        VariableRefToken right = new("RIGHT");
        Token[] children =
        {
            left, new StringToken("/"), callback, new StringToken("/"), right
        };
        instruction.PathTokens[0].ReplaceWithTokens(nested ? new Token[] { new TokenSequenceLiteral(children) } : children);

        Assert.Equal("VOLUME [\"left//right\"]", instruction.ResolveVariables('\\',
            new Dictionary<string, string?> { ["LEFT"] = "left", ["RIGHT"] = "right" }, options));
        Assert.Equal(initiallyInline ? $"VOLUME [\"$LEFT/{(empty ? "" : "callback")}/right\"]" : "VOLUME [\"left//$RIGHT\"]",
            instruction.ToString());
        Assert.Equal(initiallyInline, instruction.PathTokens[0].Tokens.Contains(left));
        Assert.Equal(!initiallyInline, instruction.PathTokens[0].Tokens.Contains(right));
        Assert.Equal(initiallyInline, instruction.PathTokens[0].Tokens.Contains(callback));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonInlinePrunesNestedUnrelatedSiblingOccurrences(bool empty, bool prefix)
    {
        Token nested = new StringToken("/");
        for (int index = 0; index < 4096; index++)
        {
            nested = new TokenSequenceLiteral(nested);
        }
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"$ROOT\"]");
        Token callback = new CallbackReference(empty ? "" : "x", _ => "custom");
        instruction.PathTokens[0].ReplaceWithToken(prefix
            ? new TokenSequenceLiteral(callback, nested) : new TokenSequenceLiteral(nested, callback));
        string expected = prefix ? "VOLUME [\"custom/\"]" : "VOLUME [\"/custom\"]";
        instruction.ResolveVariables('\\');
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(expected, instruction.ResolveVariables('\\'));
        long baseline = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(expected, instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = true }));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Same(nested, prefix ? instruction.PathTokens[0].Tokens.Last() : instruction.PathTokens[0].Tokens.First());
        Assert.True(allocated - baseline < 100_000,
            $"Nested sibling reconstruction allocated {allocated - baseline} extra bytes.");
    }

    private sealed class DefaultAggregate(params Token[] tokens) : AggregateToken(tokens);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonLiveReferenceBindingRetainsDeepNestedCallbacks(bool inline)
    {
        const int depth = 4096;
        string content = string.Concat(Enumerable.Repeat("${X:-", depth)) + "before" + new string('}', depth);
        VolumeInstruction instruction = VolumeInstruction.Parse($"VOLUME [\"{content}\"]");
        VariableRefToken outer = Assert.Single(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
        VariableRefToken inner = outer;
        for (int index = 1; index < depth; index++)
        {
            inner = Assert.Single(inner.ModifierValueToken!.Tokens.OfType<VariableRefToken>());
        }
        int calls = 0;
        instruction.PathTokens[0].ReplaceWithToken(new TokenSequenceLiteral(outer,
            new CallbackReference("callback", _ =>
            {
                calls++;
                inner.ModifierValue = "after";
                return "";
            }), new StringToken("/tail")));

        Assert.Equal("VOLUME [\"after/tail\"]", instruction.ResolveVariables('\\',
            options: new ResolutionOptions { UpdateInline = inline }));
        Assert.Equal(1, calls);
        if (inline)
        {
            Assert.Equal("VOLUME [\"after/tail\"]", instruction.ToString());
        }
    }

    private sealed class TokenSequenceLiteral : LiteralToken
    {
        internal TokenSequenceLiteral(params Token[] tokens) : base("placeholder", canContainVariables: true)
        {
            TokenList.Clear();
            TokenList.AddRange(tokens);
        }
    }

    private sealed class CallbackReference(string rendering, Func<IDictionary<string, string?>?, string?> callback)
        : VariableRefToken("ROOT")
    {
        protected override string GetUnderlyingValue(TokenStringOptions options) => rendering;

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null,
            ResolutionOptions? options = null) => callback(variables);
    }

    [Fact]
    public void Paths()
    {
        // Single-path constructor produces shell form (no JSON array)
        VolumeInstruction result = new("/var/db");
        Assert.Collection(result.Paths, new Action<string>[]
        {
            path => Assert.Equal("/var/db", path)
        });
        Assert.Equal("VOLUME /var/db", result.ToString());

        result.Paths[0] = "/var/db1";
        Assert.Collection(result.Paths, new Action<string>[]
        {
            path => Assert.Equal("/var/db1", path)
        });
        Assert.Equal("VOLUME /var/db1", result.ToString());

        result.PathTokens[0].Value = "/var/db2";
        Assert.Collection(result.Paths, new Action<string>[]
        {
            path => Assert.Equal("/var/db2", path)
        });
        Assert.Equal("VOLUME /var/db2", result.ToString());

        // Multi-path constructor produces JSON array form
        result = new VolumeInstruction(new string[] { "/var/db3", "/var/db4" });
        Assert.Collection(result.Paths, new Action<string>[]
        {
            path => Assert.Equal("/var/db3", path),
            path => Assert.Equal("/var/db4", path)
        });
        Assert.Equal("VOLUME [\"/var/db3\", \"/var/db4\"]", result.ToString());

        result.Paths[1] = "/var/db5";
        Assert.Collection(result.Paths, new Action<string>[]
        {
            path => Assert.Equal("/var/db3", path),
            path => Assert.Equal("/var/db5", path)
        });
        Assert.Equal("VOLUME [\"/var/db3\", \"/var/db5\"]", result.ToString());
    }

    [Fact]
    public void SinglePathListConstructor_ProducesShellForm()
    {
        // When the list constructor receives exactly one path, it should produce
        // shell form (VOLUME /data) not JSON array form (VOLUME ["/data"])
        VolumeInstruction result = new(new string[] { "/data" });
        Assert.Equal("VOLUME /data", result.ToString());
        Assert.Collection(result.Paths, new Action<string>[]
        {
            path => Assert.Equal("/data", path)
        });
    }

    [Fact]
    public void MultiPathListConstructor_ProducesJsonForm()
    {
        // When the list constructor receives multiple paths, it should produce
        // JSON array form (VOLUME ["/data", "/logs"])
        VolumeInstruction result = new(new string[] { "/data", "/logs" });
        Assert.Equal("VOLUME [\"/data\", \"/logs\"]", result.ToString());
        Assert.Collection(result.Paths, new Action<string>[]
        {
            path => Assert.Equal("/data", path),
            path => Assert.Equal("/logs", path)
        });
    }

    [Fact]
    public void SinglePathListConstructor_WithWhitespace_ProducesJsonForm()
    {
        VolumeInstruction result = new(new string[] { "/my data" });
        Assert.Equal("VOLUME [\"/my data\"]", result.ToString());
        Assert.Collection(result.Paths, new Action<string>[]
        {
            path => Assert.Equal("/my data", path)
        });
    }

    [Fact]
    public void SinglePathListConstructor_WithTab_ProducesJsonForm()
    {
        VolumeInstruction result = new(new string[] { "/my\tdata" });
        Assert.Equal("VOLUME [\"/my\tdata\"]", result.ToString());
        Assert.Collection(result.Paths, new Action<string>[]
        {
            path => Assert.Equal("/my\tdata", path)
        });
    }

    [Fact]
    public void SinglePathListConstructor_WithEmptyPath_ProducesJsonForm()
    {
        VolumeInstruction result = new(new string[] { "" });
        Assert.Equal("VOLUME [\"\"]", result.ToString());
        Assert.Collection(result.Paths, new Action<string>[]
        {
            path => Assert.Equal(string.Empty, path)
        });
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<VolumeInstruction>[] testInputs = new ParseTestScenario<VolumeInstruction>[]
        {
            new ParseTestScenario<VolumeInstruction>
            {
                Text = "VOLUME /var/log",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "VOLUME"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/var/log")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("VOLUME", result.InstructionName);
                    Assert.Collection(result.Paths, new Action<string>[]
                    {
                        path => Assert.Equal("/var/log", path)
                    });
                }
            },
            new ParseTestScenario<VolumeInstruction>
            {
                Text = "VOLUME /var/log /var/db",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "VOLUME"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/var/log"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/var/db")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("VOLUME", result.InstructionName);
                    Assert.Collection(result.Paths, new Action<string>[]
                    {
                        path => Assert.Equal("/var/log", path),
                        path => Assert.Equal("/var/db", path)
                    });
                }
            },
            new ParseTestScenario<VolumeInstruction>
            {
                Text = "VOLUME [\"/var/log\"]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "VOLUME"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateSymbol(token, '['),
                    token => ValidateLiteral(token, "/var/log", StringParsers.DoubleQuote),
                    token => ValidateSymbol(token, ']')
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("VOLUME", result.InstructionName);
                    Assert.Collection(result.Paths, new Action<string>[]
                    {
                        path => Assert.Equal("/var/log", path)
                    });
                }
            },
            new ParseTestScenario<VolumeInstruction>
            {
                Text = "VOLUME [\"/var/log\", \"/var/db\"]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "VOLUME"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateSymbol(token, '['),
                    token => ValidateLiteral(token, "/var/log", StringParsers.DoubleQuote),
                    token => ValidateSymbol(token, ','),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/var/db", StringParsers.DoubleQuote),
                    token => ValidateSymbol(token, ']')
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("VOLUME", result.InstructionName);
                    Assert.Collection(result.Paths, new Action<string>[]
                    {
                        path => Assert.Equal("/var/log", path),
                        path => Assert.Equal("/var/db", path)
                    });
                }
            },
            new ParseTestScenario<VolumeInstruction>
            {
                Text = "VOLUME []",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "VOLUME"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateSymbol(token, '['),
                    token => ValidateSymbol(token, ']')
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("VOLUME", result.InstructionName);
                    Assert.Empty(result.Paths);
                }
            },
            new ParseTestScenario<VolumeInstruction>
            {
                Text = "VOLUME [ ]",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "VOLUME"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateSymbol(token, '['),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateSymbol(token, ']')
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("VOLUME", result.InstructionName);
                    Assert.Empty(result.Paths);
                }
            },
            new ParseTestScenario<VolumeInstruction>
            {
                Text = "VOLUME $TEST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "VOLUME"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "$TEST",
                        token => ValidateAggregate<VariableRefToken>(token, "$TEST",
                            token => ValidateString(token, "TEST")))
                }
            },
            new ParseTestScenario<VolumeInstruction>
            {
                Text = "VOLUME /var/log `\n#test comment\n/var/db",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "VOLUME"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/var/log"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateAggregate<CommentToken>(token, "#test comment\n",
                        token => ValidateSymbol(token, '#'),
                        token => ValidateString(token, "test comment"),
                        token => ValidateNewLine(token, "\n")),
                    token => ValidateLiteral(token, "/var/db")
                },
                Validate = result =>
                {
                    Assert.Single(result.Comments);
                    Assert.Equal("test comment", result.Comments.First());
                    Assert.Equal("VOLUME", result.InstructionName);
                    Assert.Collection(result.Paths, new Action<string>[]
                    {
                        path => Assert.Equal("/var/log", path),
                        path => Assert.Equal("/var/db", path)
                    });
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public static IEnumerable<object[]> CreateTestInput()
    {
        CreateTestScenario[] testInputs = new CreateTestScenario[]
        {
            new CreateTestScenario
            {
                Paths = new string[]
                {
                    "/var/log"
                },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "VOLUME"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/var/log")
                },
                Validate = result =>
                {
                    Assert.Equal("VOLUME /var/log", result.ToString());
                    Assert.Collection(result.Paths, new Action<string>[]
                    {
                        path => Assert.Equal("/var/log", path)
                    });
                }
            },
            new CreateTestScenario
            {
                Paths = new string[]
                {
                    "/var/log",
                    "/var/db"
                },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "VOLUME"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateSymbol(token, '['),
                    token => ValidateLiteral(token, "/var/log", StringParsers.DoubleQuote),
                    token => ValidateSymbol(token, ','),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/var/db", StringParsers.DoubleQuote),
                    token => ValidateSymbol(token, ']')
                },
                Validate = result =>
                {
                    Assert.Equal("VOLUME [\"/var/log\", \"/var/db\"]", result.ToString());
                    Assert.Collection(result.Paths, new Action<string>[]
                    {
                        path => Assert.Equal("/var/log", path),
                        path => Assert.Equal("/var/db", path)
                    });
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<VolumeInstruction>
    {
        public required IEnumerable<string> Paths { get; set; }
    }
}
