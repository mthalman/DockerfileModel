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
    [InlineData('\\', "VOLUME [\"$ROOT/a\\\"b\", \"$ROOT/c\\\\d\"]",
        "VOLUME [\"/srv/a\\\"b\", \"/srv/c\\\\d\"]")]
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
            Assert.Equal(text, instruction.ResolveVariables(escapeChar,
                new Dictionary<string, string?> { ["ROOT"] = "/srv" },
                new ResolutionOptions { UpdateInline = inline }));
            Assert.Equal(text, instruction.ToString());
            if (inline)
            {
                Assert.Empty(instruction.PathTokens[0].Tokens.OfType<VariableRefToken>());
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
        Assert.Equal(inline ? expected : text, instruction.ToString());
        if (inline)
        {
            Assert.Equal(expected, instruction.ResolveVariables('\\',
                new Dictionary<string, string?> { ["ROOT"] = "unexpected" },
                new ResolutionOptions { UpdateInline = true }));
        }
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
