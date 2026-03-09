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
        ExposeInstruction result = new(scenario.Port, scenario.Protocol);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Port()
    {
        ExposeInstruction result = new("23", "protocol");
        Assert.Equal("23", result.Port);
        Assert.Equal("23", result.PortToken.Value);
        Assert.Equal("EXPOSE 23/protocol", result.ToString());

        result.Port = "45";
        Assert.Equal("45", result.Port);
        Assert.Equal("45", result.PortToken.Value);
        Assert.Equal("EXPOSE 45/protocol", result.ToString());

        result.PortToken.Value = "67";
        Assert.Equal("67", result.Port);
        Assert.Equal("67", result.PortToken.Value);
        Assert.Equal("EXPOSE 67/protocol", result.ToString());

        result.PortToken = new LiteralToken("78");
        Assert.Equal("78", result.Port);
        Assert.Equal("78", result.PortToken.Value);
        Assert.Equal("EXPOSE 78/protocol", result.ToString());

        Assert.Throws<ArgumentNullException>(() => result.Port = null);
        Assert.Throws<ArgumentException>(() => result.Port = "");
        Assert.Throws<ArgumentNullException>(() => result.PortToken = null);
    }

    [Fact]
    public void Protocol()
    {
        ExposeInstruction result = new("23", "test");
        Assert.Equal("test", result.Protocol);
        Assert.Equal("test", result.ProtocolToken.Value);
        Assert.Equal("EXPOSE 23/test", result.ToString());

        result.Protocol = "test2";
        Assert.Equal("test2", result.Protocol);
        Assert.Equal("test2", result.ProtocolToken.Value);
        Assert.Equal("EXPOSE 23/test2", result.ToString());

        result.Protocol = null;
        Assert.Null(result.Protocol);
        Assert.Null(result.ProtocolToken);
        Assert.Equal("EXPOSE 23", result.ToString());

        result.ProtocolToken = new LiteralToken("test3");
        Assert.Equal("test3", result.Protocol);
        Assert.Equal("test3", result.ProtocolToken.Value);
        Assert.Equal("EXPOSE 23/test3", result.ToString());

        result.ProtocolToken.Value = "test4";
        Assert.Equal("test4", result.Protocol);
        Assert.Equal("test4", result.ProtocolToken.Value);
        Assert.Equal("EXPOSE 23/test4", result.ToString());

        result.ProtocolToken = null;
        Assert.Null(result.Protocol);
        Assert.Null(result.ProtocolToken);
        Assert.Equal("EXPOSE 23", result.ToString());
    }

    [Fact]
    public void PortWithVariables()
    {
        ExposeInstruction result = new("$var", "test");
        TestHelper.TestVariablesWithLiteral(() => result.PortToken, "var", canContainVariables: true);
    }

    [Fact]
    public void ProtocolWithVariables()
    {
        ExposeInstruction result = new("23", "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => result.ProtocolToken, token => result.ProtocolToken = token, val => result.Protocol = val, "var", canContainVariables: true);
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
        // Verify Port (backward compat) returns first port
        Assert.Equal("80", result.Port);
    }

    [Fact]
    public void Ports_MultiplePortsWithProtocols()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80/tcp 443/udp");
        Assert.Collection(result.Ports, new Action<string>[]
        {
            port => Assert.Equal("80", port),
            port => Assert.Equal("443", port)
        });
        // Protocol returns the first port's protocol
        Assert.Equal("tcp", result.Protocol);
    }

    [Fact]
    public void Ports_MultiplePortsMixedProtocols()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80/tcp 443 8080/udp");
        Assert.Collection(result.Ports, new Action<string>[]
        {
            port => Assert.Equal("80", port),
            port => Assert.Equal("443", port),
            port => Assert.Equal("8080", port)
        });
        Assert.Equal("tcp", result.Protocol);
    }

    [Fact]
    public void Ports_ModifyViaProjectedList()
    {
        ExposeInstruction result = ExposeInstruction.Parse("EXPOSE 80 443");
        result.Ports[0] = "8080";
        Assert.Equal("8080", result.Port);
        Assert.Equal("EXPOSE 8080 443", result.ToString());

        result.Ports[1] = "9090";
        Assert.Equal("EXPOSE 8080 9090", result.ToString());
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
                    Assert.Equal("80", result.Port);
                }
            },
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 433/tcp",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "433"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateLiteral(token, "tcp")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("EXPOSE", result.InstructionName);
                    Assert.Equal("433", result.Port);
                    Assert.Equal("tcp", result.Protocol);
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
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE`\n 80`\n/`\ntcp",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "80"),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateLiteral(token, "tcp")
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
                    Assert.Equal("80", result.Port);
                    Assert.Null(result.Protocol);
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
                    Assert.Equal("80", result.Port);
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("80", port),
                        port => Assert.Equal("443", port),
                        port => Assert.Equal("8080", port)
                    });
                }
            },
            // Multi-port: two ports with protocols
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 80/tcp 443/udp",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "80"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateLiteral(token, "tcp"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "443"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateLiteral(token, "udp")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("EXPOSE", result.InstructionName);
                    Assert.Equal("80", result.Port);
                    Assert.Equal("tcp", result.Protocol);
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("80", port),
                        port => Assert.Equal("443", port)
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
                    token => ValidateLiteral(token, "80"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateLiteral(token, "tcp"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "443"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "8080"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateLiteral(token, "udp")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("EXPOSE", result.InstructionName);
                    Assert.Equal("80", result.Port);
                    Assert.Equal("tcp", result.Protocol);
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("80", port),
                        port => Assert.Equal("443", port),
                        port => Assert.Equal("8080", port)
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
                    Assert.Equal("8000-8010", result.Port);
                    Assert.Collection(result.Ports, new Action<string>[]
                    {
                        port => Assert.Equal("8000-8010", port)
                    });
                }
            },
            // Port range with protocol
            new ParseTestScenario<ExposeInstruction>
            {
                Text = "EXPOSE 8000-8010/tcp",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "8000-8010"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateLiteral(token, "tcp")
                },
                Validate = result =>
                {
                    Assert.Equal("8000-8010", result.Port);
                    Assert.Equal("tcp", result.Protocol);
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
                Port = "8080",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "8080")
                }
            },
            new CreateTestScenario
            {
                Port = "80",
                Protocol = "udp",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "EXPOSE"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "80"),
                    token => ValidateSymbol(token, '/'),
                    token => ValidateLiteral(token, "udp")
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<ExposeInstruction>
    {
        public string Port { get; set; }
        public string Protocol { get; set; }
    }
}
