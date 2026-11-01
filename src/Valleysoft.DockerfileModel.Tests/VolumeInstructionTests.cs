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
    public void JsonResolutionSerializesSourceTwice()
    {
        VolumeInstruction instruction = VolumeInstruction.Parse("VOLUME [\"/data\"]");
        CountingStringToken source = new("/data");
        instruction.PathTokens[0].ReplaceWithToken(source);
        Assert.Equal("VOLUME [\"/data\"]", instruction.ResolveVariables('\\'));
        Assert.Equal(2, source.RenderCount);
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
    public void JsonOwnerResolutionOptionsReuseMatchingContexts()
    {
        ResolutionOptions options = new() { UpdateInline = true, RemoveEscapeCharacters = true };
        ResolutionOptions nonJson = options.WithJsonStringResolution(false);
        ResolutionOptions json = nonJson.WithJsonStringResolution(true);

        Assert.Same(nonJson, nonJson.WithJsonStringResolution(false));
        Assert.Same(json, json.WithJsonStringResolution(true));
        Assert.NotSame(options, nonJson);
        Assert.NotSame(nonJson, json);
        Assert.Null(options.JsonStringResolution);
        Assert.True(json.UpdateInline);
        Assert.True(json.RemoveEscapeCharacters);
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
