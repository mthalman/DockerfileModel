using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class ExposeInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<ExposeInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, ExposeInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        ExposeInstruction result = new(scenario.PortSpec);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Ports_SinglePort()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80");
        Assert.Collection(result.Ports, new Action<string>[]
        {
            port => Assert.Equal("80", port)
        });
        Assert.Collection(result.PortTokens, new Action<LiteralToken>[]
        {
            token => Assert.Equal("80", token.Value)
        });
    }

    [Fact]
    public void Ports_SinglePortWithProtocol()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80/tcp");
        Assert.Collection(result.Ports, new Action<string>[]
        {
            port => Assert.Equal("80/tcp", port)
        });
        Assert.Collection(result.PortTokens, new Action<LiteralToken>[]
        {
            token => Assert.Equal("80/tcp", token.Value)
        });
    }

    [Fact]
    public void Ports_MultiplePorts()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80 443");
        Assert.Collection(result.Ports, new Action<string>[]
        {
            port => Assert.Equal("80", port),
            port => Assert.Equal("443", port)
        });
        Assert.Collection(result.PortTokens, new Action<LiteralToken>[]
        {
            token => Assert.Equal("80", token.Value),
            token => Assert.Equal("443", token.Value)
        });
    }

    [Fact]
    public void Ports_MultiplePortsWithProtocols()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80/tcp 443/udp");
        Assert.Collection(result.Ports, new Action<string>[]
        {
            port => Assert.Equal("80/tcp", port),
            port => Assert.Equal("443/udp", port)
        });
        Assert.Collection(result.PortTokens, new Action<LiteralToken>[]
        {
            token => Assert.Equal("80/tcp", token.Value),
            token => Assert.Equal("443/udp", token.Value)
        });
    }

    [Fact]
    public void Ports_MultiplePortsMixedProtocols()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80/tcp 443 8080/udp");
        Assert.Collection(result.Ports, new Action<string>[]
        {
            port => Assert.Equal("80/tcp", port),
            port => Assert.Equal("443", port),
            port => Assert.Equal("8080/udp", port)
        });
    }

    [Fact]
    public void Ports_ModifyViaProjectedList()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80 443");
        result.Ports[0] = "8080";
        Assert.Equal("8080", result.Ports[0]);
        Assert.Equal("EXPOSE 8080 443", result.ToString());

        result.Ports[1] = "9090";
        Assert.Equal("EXPOSE 8080 9090", result.ToString());
    }

    [Fact]
    public void Ports_ModifyViaProjectedList_WithProtocol()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80/tcp 443/udp");
        result.Ports[0] = "8080/tcp";
        Assert.Equal("8080/tcp", result.Ports[0]);
        Assert.Equal("EXPOSE 8080/tcp 443/udp", result.ToString());
    }

    [Fact]
    public void Ports_InvalidValue_Throws()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80 443");
        Assert.Throws<ArgumentNullException>(() => result.Ports[0] = null!);
        Assert.Throws<ArgumentException>(() => result.Ports[0] = "");
    }

    [Fact]
    public void Ports_PortTokenValue_Roundtrip()
    {
        ExposeInstruction result = new("23/protocol");
        Assert.Equal("23/protocol", result.Ports[0]);
        Assert.Equal("23/protocol", result.PortTokens[0].Value);
        Assert.Equal("EXPOSE 23/protocol", result.ToString());

        result.Ports[0] = "45/protocol";
        Assert.Equal("45/protocol", result.Ports[0]);
        Assert.Equal("45/protocol", result.PortTokens[0].Value);
        Assert.Equal("EXPOSE 45/protocol", result.ToString());

        result.PortTokens[0].Value = "67/protocol";
        Assert.Equal("67/protocol", result.Ports[0]);
        Assert.Equal("67/protocol", result.PortTokens[0].Value);
        Assert.Equal("EXPOSE 67/protocol", result.ToString());
    }

    [Fact]
    public void PortWithVariables()
    {
        ExposeInstruction result = new("$var");
        TestHelper.TestVariablesWithLiteral(() => result.PortTokens[0], "var", canContainVariables: true);
    }

    [Fact]
    public void PortSpecWithVariables()
    {
        ExposeInstruction result = new("$var/tcp");
        Assert.Equal("$var/tcp", result.Ports[0]);
        Assert.Equal("EXPOSE $var/tcp", result.ToString());
        Assert.Collection(result.PortTokens[0].Tokens, new Action<Token>[]
        {
            token => ValidateAggregate<VariableRefToken>(token, "$var",
                token => ValidateString(token, "var")),
            token => ValidateString(token, "/tcp")
        });
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<ExposeInstruction>[] testInputs = new ParseTestScenario<ExposeInstruction>[]
        {
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 80",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "80")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("EXPOSE", result.InstructionName);
                    Assert.Equal("80", result.Ports[0]);
                }
            },
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 433/tcp",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "433/tcp")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("EXPOSE", result.InstructionName);
                    Assert.Equal("433/tcp", result.Ports[0]);
                }
            },
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE $TEST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "$TEST",
                        token => ValidateAggregate<VariableRefToken>(token, "$TEST",
                            token => ValidateString(token, "TEST")))
                }
            },
            // Multi-port: two ports without protocols
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 80 443",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "80"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "443")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("EXPOSE", result.InstructionName);
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("80", port),
                        port => Assert.Equal("443", port)
                    });
                }
            },
            // Multi-port: three ports without protocols
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 80 443 8080",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "80"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "443"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "8080")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("EXPOSE", result.InstructionName);
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("80", port),
                        port => Assert.Equal("443", port),
                        port => Assert.Equal("8080", port)
                    });
                }
            },
            // Multi-port: two ports with protocols — each spec is a single opaque LiteralToken
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 80/tcp 443/udp",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "80/tcp"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "443/udp")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("EXPOSE", result.InstructionName);
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("80/tcp", port),
                        port => Assert.Equal("443/udp", port)
                    });
                }
            },
            // Multi-port: mixed protocols (some with, some without)
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 80/tcp 443 8080/udp",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "80/tcp"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "443"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "8080/udp")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("EXPOSE", result.InstructionName);
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("80/tcp", port),
                        port => Assert.Equal("443", port),
                        port => Assert.Equal("8080/udp", port)
                    });
                }
            },
            // Multi-port with extra whitespace
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE  80  443",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateLiteral(token, "80"),
                    token => ValidateWhitespace(token, "  "),
                    token => ValidateLiteral(token, "443")
                },
                Validate = result =>
                {
                    Assert.Equal("EXPOSE  80  443", result.ToString());
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("80", port),
                        port => Assert.Equal("443", port)
                    });
                }
            },
            // Multi-port with line continuation between ports
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 80 \\\n443",
                EscapeChar = '\\',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "80"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '\\', "\n"),
                    token => ValidateLiteral(token, "443")
                },
                Validate = result =>
                {
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("80", port),
                        port => Assert.Equal("443", port)
                    });
                }
            },
            // Multi-port with variables
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE $PORT1 $PORT2",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "$PORT1",
                        token => ValidateAggregate<VariableRefToken>(token, "$PORT1",
                            token => ValidateString(token, "PORT1"))),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "$PORT2",
                        token => ValidateAggregate<VariableRefToken>(token, "$PORT2",
                            token => ValidateString(token, "PORT2")))
                },
                Validate = result =>
                {
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("$PORT1", port),
                        port => Assert.Equal("$PORT2", port)
                    });
                }
            },
            // Port range
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 8000-8010",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "8000-8010")
                },
                Validate = result =>
                {
                    Assert.Equal("8000-8010", result.Ports[0]);
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("8000-8010", port)
                    });
                }
            },
            // Port range with protocol — single opaque token
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 8000-8010/tcp",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "8000-8010/tcp")
                },
                Validate = result =>
                {
                    Assert.Equal("8000-8010/tcp", result.Ports[0]);
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
                PortSpec = "8080",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "8080")
                },
                Validate = result =>
                {
                    Assert.Equal("8080", result.Ports[0]);
                }
            },
            new CreateTestScenario
            {
                PortSpec = "80/udp",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "80/udp")
                },
                Validate = result =>
                {
                    Assert.Equal("80/udp", result.Ports[0]);
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<ExposeInstruction>
    {
        public string PortSpec { get; set; }
    }
}
