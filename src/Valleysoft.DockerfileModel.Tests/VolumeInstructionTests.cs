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
                    token => ValidateLiteral(token, "/var/log", ParseHelper.DoubleQuote),
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
                    token => ValidateLiteral(token, "/var/log", ParseHelper.DoubleQuote),
                    token => ValidateSymbol(token, ','),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/var/db", ParseHelper.DoubleQuote),
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
                    token => ValidateLiteral(token, "/var/log", ParseHelper.DoubleQuote),
                    token => ValidateSymbol(token, ','),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/var/db", ParseHelper.DoubleQuote),
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
        public IEnumerable<string> Paths { get; set; }
    }
}
