using Valleysoft.DockerfileModel.Parsing;
using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class HealthCheckInstructionTests
{
    [Fact]
    public void Parse_JsonArrayWithLineContinuationAndNonStringElementRejects()
    {
        Assert.Throws<ParseException>(() => HealthCheckInstruction.Parse("HEALTHCHECK CMD [\"echo\", \\\n 1]"));
    }

    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<HealthCheckInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, HealthCheckInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        HealthCheckInstruction result;
            
        if (scenario.Command is not null)
        {
            if (scenario.Args is not null)
            {
                result = new HealthCheckInstruction(scenario.Command, scenario.Args, scenario.Interval, scenario.Timeout, scenario.StartPeriod, scenario.StartInterval, scenario.Retries);
            }
            else
            {
                result = new HealthCheckInstruction(scenario.Command, scenario.Interval, scenario.Timeout, scenario.StartPeriod, scenario.StartInterval, scenario.Retries);
            }
        }
        else
        {
            result = new HealthCheckInstruction();
        }

        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Theory]
    [InlineData("HEALTHCHECK CMD [\"echo\", \"Please, close the brackets when you're done\"\n")]
    [InlineData("HEALTHCHECK CMD [\"echo\", \"look ma, no quote!]\n")]
    [InlineData("HEALTHCHECK CMD ['echo','single quotes are invalid JSON']\n")]
    public void MalformedCmdJsonFallsBackToShellForm(string text)
    {
        HealthCheckInstruction result = HealthCheckInstruction.Parse(text);

        Assert.IsType<ShellFormCommand>(result.Command);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Interval()
    {
        HealthCheckInstruction instruction = new("command", interval: "10s");
        Assert.Equal("10s", instruction.Interval);
        Assert.Equal("10s", Assert.IsType<LiteralToken>(instruction.IntervalToken).Value);
        Assert.Equal("HEALTHCHECK --interval=10s CMD command", instruction.ToString());

        instruction.Interval = "20s";
        Assert.Equal("20s", instruction.Interval);
        Assert.Equal("20s", instruction.IntervalToken.Value);
        Assert.Equal("HEALTHCHECK --interval=20s CMD command", instruction.ToString());

        instruction.Interval = null;
        Assert.Null(instruction.Interval);
        Assert.Null(instruction.IntervalToken);
        Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());

        instruction.IntervalToken = new LiteralToken("40s");
        Assert.Equal("40s", instruction.Interval);
        Assert.Equal("40s", instruction.IntervalToken.Value);
        Assert.Equal("HEALTHCHECK --interval=40s CMD command", instruction.ToString());

        instruction.IntervalToken.Value = "30s";
        Assert.Equal("30s", instruction.Interval);
        Assert.Equal("30s", instruction.IntervalToken.Value);
        Assert.Equal("HEALTHCHECK --interval=30s CMD command", instruction.ToString());

        instruction.IntervalToken = null;
        Assert.Null(instruction.Interval);
        Assert.Null(instruction.IntervalToken);
        Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());
    }

    [Fact]
    public void IntervalWithVariables()
    {
        HealthCheckInstruction instruction = new("command", interval: "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => instruction.IntervalToken, token => instruction.IntervalToken = token, val => instruction.Interval = val, "var", canContainVariables: true);
    }

    [Fact]
    public void Timeout()
    {
        HealthCheckInstruction instruction = new("command", timeout: "10s");
        Assert.Equal("10s", instruction.Timeout);
        Assert.Equal("10s", Assert.IsType<LiteralToken>(instruction.TimeoutToken).Value);
        Assert.Equal("HEALTHCHECK --timeout=10s CMD command", instruction.ToString());

        instruction.Timeout = "20s";
        Assert.Equal("20s", instruction.Timeout);
        Assert.Equal("20s", instruction.TimeoutToken.Value);
        Assert.Equal("HEALTHCHECK --timeout=20s CMD command", instruction.ToString());

        instruction.Timeout = null;
        Assert.Null(instruction.Timeout);
        Assert.Null(instruction.TimeoutToken);
        Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());

        instruction.TimeoutToken = new LiteralToken("40s");
        Assert.Equal("40s", instruction.Timeout);
        Assert.Equal("40s", instruction.TimeoutToken.Value);
        Assert.Equal("HEALTHCHECK --timeout=40s CMD command", instruction.ToString());

        instruction.TimeoutToken.Value = "30s";
        Assert.Equal("30s", instruction.Timeout);
        Assert.Equal("30s", instruction.TimeoutToken.Value);
        Assert.Equal("HEALTHCHECK --timeout=30s CMD command", instruction.ToString());

        instruction.TimeoutToken = null;
        Assert.Null(instruction.Timeout);
        Assert.Null(instruction.TimeoutToken);
        Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());
    }

    [Fact]
    public void TimeoutWithVariables()
    {
        HealthCheckInstruction instruction = new("command", timeout: "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => instruction.TimeoutToken, token => instruction.TimeoutToken = token, val => instruction.Timeout = val, "var", canContainVariables: true);
    }

    [Fact]
    public void StartPeriod()
    {
        HealthCheckInstruction instruction = new("command", startPeriod: "10s");
        Assert.Equal("10s", instruction.StartPeriod);
        Assert.Equal("10s", Assert.IsType<LiteralToken>(instruction.StartPeriodToken).Value);
        Assert.Equal("HEALTHCHECK --start-period=10s CMD command", instruction.ToString());

        instruction.StartPeriod = "20s";
        Assert.Equal("20s", instruction.StartPeriod);
        Assert.Equal("20s", instruction.StartPeriodToken.Value);
        Assert.Equal("HEALTHCHECK --start-period=20s CMD command", instruction.ToString());

        instruction.StartPeriod = null;
        Assert.Null(instruction.StartPeriod);
        Assert.Null(instruction.StartPeriodToken);
        Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());

        instruction.StartPeriodToken = new LiteralToken("40s");
        Assert.Equal("40s", instruction.StartPeriod);
        Assert.Equal("40s", instruction.StartPeriodToken.Value);
        Assert.Equal("HEALTHCHECK --start-period=40s CMD command", instruction.ToString());

        instruction.StartPeriodToken.Value = "30s";
        Assert.Equal("30s", instruction.StartPeriod);
        Assert.Equal("30s", instruction.StartPeriodToken.Value);
        Assert.Equal("HEALTHCHECK --start-period=30s CMD command", instruction.ToString());

        instruction.StartPeriodToken = null;
        Assert.Null(instruction.StartPeriod);
        Assert.Null(instruction.StartPeriodToken);
        Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());
    }

    [Fact]
    public void StartPeriodWithVariables()
    {
        HealthCheckInstruction instruction = new("command", startPeriod: "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => instruction.StartPeriodToken, token => instruction.StartPeriodToken = token, val => instruction.StartPeriod = val, "var", canContainVariables: true);
    }

    [Fact]
    public void StartInterval()
    {
        HealthCheckInstruction instruction = new("command", startInterval: "10s");
        Assert.Equal("10s", instruction.StartInterval);
        Assert.Equal("10s", Assert.IsType<LiteralToken>(instruction.StartIntervalToken).Value);
        Assert.Equal("HEALTHCHECK --start-interval=10s CMD command", instruction.ToString());

        instruction.StartInterval = "20s";
        Assert.Equal("20s", instruction.StartInterval);
        Assert.Equal("20s", instruction.StartIntervalToken.Value);
        Assert.Equal("HEALTHCHECK --start-interval=20s CMD command", instruction.ToString());

        instruction.StartInterval = null;
        Assert.Null(instruction.StartInterval);
        Assert.Null(instruction.StartIntervalToken);
        Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());

        instruction.StartIntervalToken = new LiteralToken("40s");
        Assert.Equal("40s", instruction.StartInterval);
        Assert.Equal("40s", instruction.StartIntervalToken.Value);
        Assert.Equal("HEALTHCHECK --start-interval=40s CMD command", instruction.ToString());

        instruction.StartIntervalToken.Value = "30s";
        Assert.Equal("30s", instruction.StartInterval);
        Assert.Equal("30s", instruction.StartIntervalToken.Value);
        Assert.Equal("HEALTHCHECK --start-interval=30s CMD command", instruction.ToString());

        instruction.StartIntervalToken = null;
        Assert.Null(instruction.StartInterval);
        Assert.Null(instruction.StartIntervalToken);
        Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());
    }

    [Fact]
    public void StartIntervalWithVariables()
    {
        HealthCheckInstruction instruction = new("command", startInterval: "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => instruction.StartIntervalToken, token => instruction.StartIntervalToken = token, val => instruction.StartInterval = val, "var", canContainVariables: true);
    }

    [Fact]
    public void Retries()
    {
        HealthCheckInstruction instruction = new("command", retries: "10s");
        Assert.Equal("10s", instruction.Retries);
        Assert.Equal("10s", Assert.IsType<LiteralToken>(instruction.RetriesToken).Value);
        Assert.Equal("HEALTHCHECK --retries=10s CMD command", instruction.ToString());

        instruction.Retries = "20s";
        Assert.Equal("20s", instruction.Retries);
        Assert.Equal("20s", instruction.RetriesToken.Value);
        Assert.Equal("HEALTHCHECK --retries=20s CMD command", instruction.ToString());

        instruction.Retries = null;
        Assert.Null(instruction.Retries);
        Assert.Null(instruction.RetriesToken);
        Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());

        instruction.RetriesToken = new LiteralToken("40s");
        Assert.Equal("40s", instruction.Retries);
        Assert.Equal("40s", instruction.RetriesToken.Value);
        Assert.Equal("HEALTHCHECK --retries=40s CMD command", instruction.ToString());

        instruction.RetriesToken.Value = "30s";
        Assert.Equal("30s", instruction.Retries);
        Assert.Equal("30s", instruction.RetriesToken.Value);
        Assert.Equal("HEALTHCHECK --retries=30s CMD command", instruction.ToString());

        instruction.RetriesToken = null;
        Assert.Null(instruction.Retries);
        Assert.Null(instruction.RetriesToken);
        Assert.Equal("HEALTHCHECK CMD command", instruction.ToString());
    }

    [Fact]
    public void RetriesWithVariables()
    {
        HealthCheckInstruction instruction = new("command", retries: "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => instruction.RetriesToken, token => instruction.RetriesToken = token, val => instruction.Retries = val, "var", canContainVariables: true);
    }

    [Theory]
    [InlineData("HEALTHCHECK")]
    [InlineData("HEALTHCHECK CONNECT TCP 7000")]
    [InlineData("HEALTHCHECK CMD")]
    public void MalformedInstructionsRejectInsteadOfCrashing(string text)
    {
        Assert.Throws<ParseException>(() => HealthCheckInstruction.Parse(text));

        DockerfileParseResult result = Dockerfile.TryParse(text + "\n");
        Assert.False(result.Success);
        Assert.Null(result.Dockerfile);
        Assert.Equal(DockerfileDiagnosticCodes.InvalidSyntax, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void CommandProperty()
    {
        HealthCheckInstruction instruction = new("command1");
        Assert.Equal("HEALTHCHECK CMD command1", instruction.ToString());

        instruction.Command = new ExecFormCommand(new string[] { "command", "arg" });
        Assert.Equal("HEALTHCHECK CMD [\"command\", \"arg\"]", instruction.ToString());

        instruction.Command = null;
        Assert.Equal("HEALTHCHECK NONE", instruction.ToString());

        instruction.Command = new ShellFormCommand("cmd");
        Assert.Equal("HEALTHCHECK CMD cmd", instruction.ToString());
    }

    [Fact]
    public void CommandProperty_SetNullWithFlags()
    {
        // When flags exist, setting Command to null should preserve the flags
        // and replace CMD + command with NONE.
        HealthCheckInstruction instruction = new("command1", interval: "30s", timeout: "10s");
        Assert.Equal("HEALTHCHECK --interval=30s --timeout=10s CMD command1", instruction.ToString());
        Assert.NotNull(instruction.Command);
        Assert.Equal("30s", instruction.Interval);
        Assert.Equal("10s", instruction.Timeout);

        instruction.Command = null;
        Assert.Null(instruction.Command);
        Assert.Equal("30s", instruction.Interval);
        Assert.Equal("10s", instruction.Timeout);
        Assert.Equal("HEALTHCHECK --interval=30s --timeout=10s NONE", instruction.ToString());
    }

    [Theory]
    [InlineData("true", "\n")]
    [InlineData("[\"true\"]", "\n")]
    [InlineData("true", "\r\n")]
    [InlineData("[\"true\"]", "\r\n")]
    public void CommandProperty_DisablePreservesFollowingInstruction(string command, string newline)
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            $"FROM alpine{newline}HEALTHCHECK CMD {command}{newline}RUN echo ok{newline}");
        HealthCheckInstruction instruction = dockerfile.Items.OfType<HealthCheckInstruction>().Single();

        instruction.Command = null;

        string expected = $"FROM alpine{newline}HEALTHCHECK NONE{newline}RUN echo ok{newline}";
        Assert.Equal(expected, dockerfile.ToString());
        Assert.Equal(expected, Dockerfile.Parse(dockerfile.ToString()).ToString());
    }

    [Theory]
    [MemberData(nameof(CommandTransitionInputs))]
    public void CommandProperty_TransitionsRetainTerminalTrivia(
        string command, string flags, string suffix, char escapeChar)
    {
        HealthCheckInstruction instruction = HealthCheckInstruction.Parse(
            $"HEALTHCHECK {flags}CMD {command}{suffix}", escapeChar);
        Command original = instruction.Command!;
        Token[] flagTokens = instruction.Tokens.Where(token =>
            token is IntervalFlag or TimeoutFlag or StartPeriodFlag or StartIntervalFlag or RetriesFlag).ToArray();

        for (int i = 0; i < 2; i++)
        {
            instruction.Command = null;
            Assert.Null(instruction.Command);
            Assert.Equal($"HEALTHCHECK {flags}NONE{suffix}", instruction.ToString());
            Assert.Equal(instruction.ToString(), HealthCheckInstruction.Parse(instruction.ToString(), escapeChar).ToString());
            AssertUniqueTokenTree(instruction);

            instruction.Command = null;
            Assert.Equal($"HEALTHCHECK {flags}NONE{suffix}", instruction.ToString());

            instruction.Command = original;
            Assert.Same(original, instruction.Command);
            Assert.Equal($"HEALTHCHECK {flags}CMD {command}{suffix}", instruction.ToString());
            Assert.Equal(flagTokens, instruction.Tokens.Where(token =>
                token is IntervalFlag or TimeoutFlag or StartPeriodFlag or StartIntervalFlag or RetriesFlag));
            AssertUniqueTokenTree(instruction);
        }

        instruction.Command = null;
        instruction.Command = new ShellFormCommand("restored", escapeChar);
        Assert.Equal($"HEALTHCHECK {flags}CMD restored{suffix}", instruction.ToString());
        instruction.Command = null;
        instruction.Command = new ExecFormCommand(new[] { "restored" }, escapeChar);
        Assert.Equal($"HEALTHCHECK {flags}CMD [\"restored\"]{suffix}", instruction.ToString());
        AssertUniqueTokenTree(instruction);
    }

    public static IEnumerable<object[]> CommandTransitionInputs()
    {
        foreach (string newline in new[] { "\n", "\r\n" })
        foreach (char escapeChar in new[] { '\\', '`' })
        foreach (string command in new[]
        {
            "true",
            "[\"true\"]",
            "echo " + escapeChar + newline + "  ok",
            "[\"echo\"," + escapeChar + newline + " \"ok\"]"
        })
        foreach (string flags in new[]
        {
            "",
            "--interval=30s --timeout=10s --start-period=5s --start-interval=1s --retries=3 "
        })
        foreach (string suffix in new[] { "", " \t", newline, " \t" + newline, " " + escapeChar + newline + "# terminal comment" + newline })
        {
            yield return new object[] { command, flags, suffix, escapeChar };
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("\n")]
    [InlineData(" \t\r\n")]
    [InlineData("\\\n# terminal comment\n")]
    public void CommandProperty_ParsedNoneRetainsTerminalTrivia(string suffix)
    {
        HealthCheckInstruction instruction = HealthCheckInstruction.Parse($"HEALTHCHECK NONE{suffix}");

        instruction.Command = new ShellFormCommand("true");
        Assert.Equal($"HEALTHCHECK CMD true{suffix}", instruction.ToString());
        instruction.Command = null;
        Assert.Equal($"HEALTHCHECK NONE{suffix}", instruction.ToString());

        instruction.Command = new ExecFormCommand(new[] { "true" });
        Assert.Equal($"HEALTHCHECK CMD [\"true\"]{suffix}", instruction.ToString());
        instruction.Command = null;
        Assert.Equal($"HEALTHCHECK NONE{suffix}", instruction.ToString());
        AssertUniqueTokenTree(instruction);
    }

    [Theory]
    [InlineData(" \t")]
    [InlineData(" \t\n")]
    [InlineData(" \t\r\n")]
    public void CommandProperty_ExecFormRetainsTerminalWhitespace(string suffix)
    {
        HealthCheckInstruction instruction = HealthCheckInstruction.Parse(
            $"HEALTHCHECK CMD [\"true\"]{suffix}");

        instruction.Command = null;

        Assert.Equal($"HEALTHCHECK NONE{suffix}", instruction.ToString());
        AssertUniqueTokenTree(instruction);
    }

    private static void AssertUniqueTokenTree(AggregateToken root)
    {
        HashSet<Token> seen = new(ReferenceEqualityComparer.Instance);
        Visit(root);

        void Visit(Token token)
        {
            Assert.True(seen.Add(token), "A token must not occur twice in the live tree.");
            if (token is AggregateToken aggregate)
            {
                string contents = string.Concat(aggregate.Tokens.Select(child => child.ToString()));
                if (aggregate is IQuotableToken quotable)
                {
                    contents = $"{quotable.QuoteChar}{contents}{quotable.QuoteChar}";
                }
                Assert.Equal(contents, aggregate.ToString());
                foreach (Token child in aggregate.Tokens)
                {
                    Visit(child);
                }
            }
        }
    }

    [Fact]
    public void CommandProperty_SetNullWithSingleFlag()
    {
        // Single flag: setting Command to null should produce well-formed output.
        HealthCheckInstruction instruction = HealthCheckInstruction.Parse("HEALTHCHECK --interval=30s CMD /bin/check");
        Assert.Equal("HEALTHCHECK --interval=30s CMD /bin/check", instruction.ToString());

        instruction.Command = null;
        Assert.Null(instruction.Command);
        Assert.Equal("30s", instruction.Interval);
        Assert.Equal("HEALTHCHECK --interval=30s NONE", instruction.ToString());
    }

    [Fact]
    public void CommandProperty_SetNullWithMultipleFlags()
    {
        // Multiple flags: setting Command to null should produce well-formed output.
        HealthCheckInstruction instruction = HealthCheckInstruction.Parse("HEALTHCHECK --interval=30s --timeout=10s --retries=3 CMD /bin/check");
        Assert.Equal("HEALTHCHECK --interval=30s --timeout=10s --retries=3 CMD /bin/check", instruction.ToString());

        instruction.Command = null;
        Assert.Null(instruction.Command);
        Assert.Equal("30s", instruction.Interval);
        Assert.Equal("10s", instruction.Timeout);
        Assert.Equal("3", instruction.Retries);
        Assert.Equal("HEALTHCHECK --interval=30s --timeout=10s --retries=3 NONE", instruction.ToString());
    }

    [Fact]
    public void CommandProperty_SetNullNoFlags()
    {
        // No flags: setting Command to null should produce well-formed output.
        HealthCheckInstruction instruction = HealthCheckInstruction.Parse("HEALTHCHECK CMD /bin/check");
        Assert.Equal("HEALTHCHECK CMD /bin/check", instruction.ToString());

        instruction.Command = null;
        Assert.Null(instruction.Command);
        Assert.Equal("HEALTHCHECK NONE", instruction.ToString());
    }

    [Fact]
    public void CommandProperty_CmdToNoneRoundTrip()
    {
        // Test full round-trip: CMD -> NONE -> CMD
        HealthCheckInstruction instruction = new("original");
        Assert.Equal("HEALTHCHECK CMD original", instruction.ToString());

        // CMD -> NONE
        instruction.Command = null;
        Assert.Equal("HEALTHCHECK NONE", instruction.ToString());
        Assert.Null(instruction.Command);

        // NONE -> CMD
        instruction.Command = new ShellFormCommand("restored");
        Assert.Equal("HEALTHCHECK CMD restored", instruction.ToString());
        Assert.NotNull(instruction.Command);
        Assert.IsType<ShellFormCommand>(instruction.Command);
        Assert.Equal("restored", ((ShellFormCommand)instruction.Command).Value);

        // CMD -> NONE again
        instruction.Command = null;
        Assert.Equal("HEALTHCHECK NONE", instruction.ToString());

        // NONE -> CMD with exec form
        instruction.Command = new ExecFormCommand(new string[] { "cmd", "arg1" });
        Assert.Contains("[\"cmd\", \"arg1\"]", instruction.ToString());
        Assert.IsType<ExecFormCommand>(instruction.Command);
    }

    [Fact]
    public void CommandProperty_CmdToNoneWithFlagsRoundTrip()
    {
        // Test round-trip with flags: CMD -> NONE -> CMD, verifying flags survive
        HealthCheckInstruction instruction = new("check", interval: "5s", retries: "3");
        Assert.Equal("HEALTHCHECK --interval=5s --retries=3 CMD check", instruction.ToString());

        // CMD -> NONE (flags should be preserved)
        instruction.Command = null;
        Assert.Null(instruction.Command);
        Assert.Equal("5s", instruction.Interval);
        Assert.Equal("3", instruction.Retries);
        Assert.Equal("HEALTHCHECK --interval=5s --retries=3 NONE", instruction.ToString());

        // NONE -> CMD (flags should still be preserved)
        instruction.Command = new ShellFormCommand("newcheck");
        Assert.NotNull(instruction.Command);
        Assert.Equal("5s", instruction.Interval);
        Assert.Equal("3", instruction.Retries);
        Assert.Equal("newcheck", ((ShellFormCommand)instruction.Command).Value);
        Assert.Equal("HEALTHCHECK --interval=5s --retries=3 CMD newcheck", instruction.ToString());
    }

    [Fact]
    public void CommandProperty_NoneToCmd()
    {
        // Start with NONE, set Command to a value
        HealthCheckInstruction instruction = new();
        Assert.Equal("HEALTHCHECK NONE", instruction.ToString());
        Assert.Null(instruction.Command);

        instruction.Command = new ShellFormCommand("mycommand");
        Assert.Equal("HEALTHCHECK CMD mycommand", instruction.ToString());
        Assert.NotNull(instruction.Command);
        Assert.IsType<ShellFormCommand>(instruction.Command);

        // Replace with exec form
        instruction.Command = new ExecFormCommand(new string[] { "exec" });
        Assert.Contains("[\"exec\"]", instruction.ToString());
        Assert.IsType<ExecFormCommand>(instruction.Command);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<HealthCheckInstruction>[] testInputs = new ParseTestScenario<HealthCheckInstruction>[]
        {
            new ParseTestScenario<HealthCheckInstruction>
            {
                Text = "HEALTHCHECK NONE",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "NONE")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("HEALTHCHECK", result.InstructionName);
                    Assert.Null(result.Command);
                }
            },
            new ParseTestScenario<HealthCheckInstruction>
            {
                Text = "HEALTHCHECK CMD /bin/check-running",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                        token => ValidateLiteral(token, "/bin/check-running"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("HEALTHCHECK", result.InstructionName);
                    Assert.IsType<ShellFormCommand>(result.Command);
                    Assert.Equal("/bin/check-running", ((ShellFormCommand)result.Command).Value);
                }
            },
            new ParseTestScenario<HealthCheckInstruction>
            {
                Text = "HEALTHCHECK --start-period=10s CMD /bin/check-running",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "10s"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                        token => ValidateLiteral(token, "/bin/check-running"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("HEALTHCHECK", result.InstructionName);
                    Assert.IsType<ShellFormCommand>(result.Command);
                    Assert.Equal("/bin/check-running", ((ShellFormCommand)result.Command).Value);
                    Assert.Equal("10s", result.StartPeriod);
                }
            },
            new ParseTestScenario<HealthCheckInstruction>
            {
                Text = "HEALTHCHECK --start-interval=5s CMD /bin/check-running",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<StartIntervalFlag>(token, "start-interval", "5s"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                        token => ValidateLiteral(token, "/bin/check-running"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("HEALTHCHECK", result.InstructionName);
                    Assert.IsType<ShellFormCommand>(result.Command);
                    Assert.Equal("/bin/check-running", ((ShellFormCommand)result.Command).Value);
                    Assert.Equal("5s", result.StartInterval);
                }
            },
            new ParseTestScenario<HealthCheckInstruction>
            {
                Text = "HEALTHCHECK --interval=1m --start-period=10s --start-interval=5s --retries=5 --timeout=1h CMD /bin/check-running",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<IntervalFlag>(token, "interval", "1m"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "10s"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<StartIntervalFlag>(token, "start-interval", "5s"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<RetriesFlag>(token, "retries", "5"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<TimeoutFlag>(token, "timeout", "1h"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                        token => ValidateLiteral(token, "/bin/check-running"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("HEALTHCHECK", result.InstructionName);
                    Assert.IsType<ShellFormCommand>(result.Command);
                    Assert.Equal("/bin/check-running", ((ShellFormCommand)result.Command).Value);
                    Assert.Equal("10s", result.StartPeriod);
                    Assert.Equal("5s", result.StartInterval);
                    Assert.Equal("5", result.Retries);
                    Assert.Equal("1m", result.Interval);
                    Assert.Equal("1h", result.Timeout);
                }
            },
            new ParseTestScenario<HealthCheckInstruction>
            {
                Text = "HEALTHCHECK --interval=1m --start-period=10s --retries=5 --timeout=1h CMD /bin/check-running",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<IntervalFlag>(token, "interval", "1m"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "10s"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<RetriesFlag>(token, "retries", "5"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<TimeoutFlag>(token, "timeout", "1h"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                        token => ValidateLiteral(token, "/bin/check-running"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("HEALTHCHECK", result.InstructionName);
                    Assert.IsType<ShellFormCommand>(result.Command);
                    Assert.Equal("/bin/check-running", ((ShellFormCommand)result.Command).Value);
                    Assert.Equal("10s", result.StartPeriod);
                    Assert.Equal("5", result.Retries);
                    Assert.Equal("1m", result.Interval);
                    Assert.Equal("1h", result.Timeout);
                }
            },
            new ParseTestScenario<HealthCheckInstruction>
            {
                Text = "HEALTHCHECK `\n--start-period=10s `\nCMD /bin/check-running",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "10s"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                        token => ValidateLiteral(token, "/bin/check-running"))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("HEALTHCHECK", result.InstructionName);
                    Assert.IsType<ShellFormCommand>(result.Command);
                    Assert.Equal("/bin/check-running", ((ShellFormCommand)result.Command).Value);
                    Assert.Equal("10s", result.StartPeriod);
                }
            },
            new ParseTestScenario<HealthCheckInstruction>
            {
                Text = "HEALTHCHECK CMD \\\necho hello",
                EscapeChar = '\\',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '\\', "\n"),
                    token => ValidateAggregate<ShellFormCommand>(token, "echo hello")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("HEALTHCHECK", result.InstructionName);
                    Assert.IsType<ShellFormCommand>(result.Command);
                    Assert.Equal("HEALTHCHECK CMD \\\necho hello", result.ToString());
                }
            },
            new ParseTestScenario<HealthCheckInstruction>
            {
                Text = "HEALTHCHECK CMD \\\n[\"echo\", \"hello\"]",
                EscapeChar = '\\',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLineContinuation(token, '\\', "\n"),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"echo\", \"hello\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "echo", '\"'),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "hello", '\"'),
                        token => ValidateSymbol(token, ']'))
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("HEALTHCHECK", result.InstructionName);
                    Assert.IsType<ExecFormCommand>(result.Command);
                    Assert.Equal("HEALTHCHECK CMD \\\n[\"echo\", \"hello\"]", result.ToString());
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public static IEnumerable<object[]> CreateTestInput()
    {
        CreateTestScenario[] testInputs = new CreateTestScenario[]
        {
            new CreateTestScenario
            {
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "NONE")
                }
            },
            new CreateTestScenario
            {
                Command = "/bin/check-running",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                        token => ValidateLiteral(token, "/bin/check-running"))
                }
            },
            new CreateTestScenario
            {
                Command = "/bin/check-running",
                Args = new string[]
                {
                    "-f"
                },
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ExecFormCommand>(token, "[\"/bin/check-running\", \"-f\"]",
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/bin/check-running", '\"'),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "-f", '\"'),
                        token => ValidateSymbol(token, ']'))
                }
            },
            new CreateTestScenario
            {
                Command = "/bin/check-running",
                Interval = "1s",
                StartPeriod = "2s",
                StartInterval = "10s",
                Timeout = "3s",
                Retries = "10",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "HEALTHCHECK"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<IntervalFlag>(token, "interval", "1s"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<TimeoutFlag>(token, "timeout", "3s"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<StartPeriodFlag>(token, "start-period", "2s"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<StartIntervalFlag>(token, "start-interval", "10s"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyValueFlag<RetriesFlag>(token, "retries", "10"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateKeyword(token, "CMD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<ShellFormCommand>(token, "/bin/check-running",
                        token => ValidateLiteral(token, "/bin/check-running"))
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<HealthCheckInstruction>
    {
        public string? Command { get; set; }
        public IEnumerable<string>? Args { get; set; }
        public string? Interval { get; set; }
        public string? Timeout { get; set; }
        public string? StartPeriod { get; set; }
        public string? StartInterval { get; set; }
        public string? Retries { get; set; }
    }
}
