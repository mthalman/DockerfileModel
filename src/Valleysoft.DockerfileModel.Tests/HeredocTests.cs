using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class HeredocTests
{
    // ================================================================
    // RUN instruction heredoc parse tests (token validation via Theory)
    // ================================================================

    [Theory]
    [MemberData(nameof(RunParseTestInput))]
    public void RunParse(ParseTestScenario<RunInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, RunInstruction.Parse);

    public static IEnumerable<object[]> RunParseTestInput()
    {
        ParseTestScenario<RunInstruction>[] testInputs = new ParseTestScenario<RunInstruction>[]
        {
            // Basic single heredoc
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<EOF\necho hello\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF")),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nEOF\n",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Equal("RUN", result.InstructionName);
                    Assert.Null(result.Command);
                    Assert.Single(result.HeredocTokens);
                    Assert.Empty(result.Comments);
                }
            },
            // Chomp flag (<<-)
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<-EOF\n\techo hello\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<-EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF")),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "\techo hello\nEOF\n",
                        token => ValidateString(token, "\techo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // Double-quoted delimiter
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<\"EOF\"\necho hello\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<\"EOF\"",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '"'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateSymbol(token, '"')),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nEOF\n",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // Single-quoted delimiter
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<'EOF'\necho hello\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<'EOF'",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '\''),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateSymbol(token, '\'')),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nEOF\n",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // Multi-line body
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<EOF\nline 1\nline 2\nline 3\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF")),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "line 1\nline 2\nline 3\nEOF\n",
                        token => ValidateString(token, "line 1\nline 2\nline 3\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // Empty body
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<EOF\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF")),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "EOF\n",
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // Named custom delimiter (SCRIPT)
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<SCRIPT\n#!/bin/bash\necho hello\nSCRIPT\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<SCRIPT",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "SCRIPT")),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "#!/bin/bash\necho hello\nSCRIPT\n",
                        token => ValidateString(token, "#!/bin/bash\necho hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "SCRIPT"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // No trailing newline after closing delimiter
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<EOF\necho hello\nEOF",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF")),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nEOF",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // Chomp + double-quoted delimiter combined
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<-\"EOF\"\n\techo hello\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<-\"EOF\"",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '-'),
                        token => ValidateSymbol(token, '"'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateSymbol(token, '"')),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "\techo hello\nEOF\n",
                        token => ValidateString(token, "\techo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // Long body (shebang + multiple commands)
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<EOF\n#!/bin/bash\nset -e\napt-get update\napt-get install -y curl\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF")),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "#!/bin/bash\nset -e\napt-get update\napt-get install -y curl\nEOF\n",
                        token => ValidateString(token, "#!/bin/bash\nset -e\napt-get update\napt-get install -y curl\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // Delimiter with numbers
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<EOF123\necho hello\nEOF123\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<EOF123",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF123")),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nEOF123\n",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF123"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // Delimiter with underscores
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<MY_EOF\necho hello\nMY_EOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<MY_EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "MY_EOF")),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nMY_EOF\n",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "MY_EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
            // Quoted delimiter with hyphen
            new ParseTestScenario<RunInstruction>
            {
                Text = "RUN <<'MY-DELIM'\necho hello\nMY-DELIM\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "RUN"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<'MY-DELIM'",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '\''),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "MY-DELIM"),
                        token => ValidateSymbol(token, '\'')),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nMY-DELIM\n",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "MY-DELIM"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    // ================================================================
    // COPY instruction heredoc parse tests (token validation via Theory)
    // ================================================================

    [Theory]
    [MemberData(nameof(CopyParseTestInput))]
    public void CopyParse(ParseTestScenario<CopyInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, CopyInstruction.Parse);

    public static IEnumerable<object[]> CopyParseTestInput()
    {
        ParseTestScenario<CopyInstruction>[] testInputs = new ParseTestScenario<CopyInstruction>[]
        {
            // Basic COPY heredoc with destination
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app/script.sh"),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nEOF\n",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Equal("COPY", result.InstructionName);
                    Assert.Single(result.HeredocTokens);
                    Assert.Equal("/app/script.sh", result.Destination);
                }
            },
            // COPY with double-quoted delimiter
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY <<\"EOF\" /app/script.sh\necho hello\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<\"EOF\"",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '"'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateSymbol(token, '"')),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app/script.sh"),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nEOF\n",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                    Assert.Equal("/app/script.sh", result.Destination);
                }
            },
            // COPY with multi-line body
            new ParseTestScenario<CopyInstruction>
            {
                Text = "COPY <<EOF /app/config.txt\nline 1\nline 2\nline 3\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "COPY"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app/config.txt"),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "line 1\nline 2\nline 3\nEOF\n",
                        token => ValidateString(token, "line 1\nline 2\nline 3\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                    Assert.Equal("/app/config.txt", result.Destination);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    // ================================================================
    // ADD instruction heredoc parse tests (token validation via Theory)
    // ================================================================

    [Theory]
    [MemberData(nameof(AddParseTestInput))]
    public void AddParse(ParseTestScenario<AddInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, AddInstruction.Parse);

    public static IEnumerable<object[]> AddParseTestInput()
    {
        ParseTestScenario<AddInstruction>[] testInputs = new ParseTestScenario<AddInstruction>[]
        {
            // Basic ADD heredoc with destination
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD <<EOF /app/script.sh\necho hello\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app/script.sh"),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nEOF\n",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Equal("ADD", result.InstructionName);
                    Assert.Single(result.HeredocTokens);
                    Assert.Equal("/app/script.sh", result.Destination);
                }
            },
            // ADD with double-quoted delimiter
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD <<\"EOF\" /app/script.sh\necho hello\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<\"EOF\"",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '"'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateSymbol(token, '"')),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app/script.sh"),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "echo hello\nEOF\n",
                        token => ValidateString(token, "echo hello\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                    Assert.Equal("/app/script.sh", result.Destination);
                }
            },
            // ADD with multi-line body
            new ParseTestScenario<AddInstruction>
            {
                Text = "ADD <<EOF /app/config\nline1\nline2\nline3\nEOF\n",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "ADD"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<HeredocMarkerToken>(token, "<<EOF",
                        token => ValidateSymbol(token, '<'),
                        token => ValidateSymbol(token, '<'),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF")),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/app/config"),
                    token => ValidateNewLine(token, "\n"),
                    token => ValidateAggregate<HeredocBodyToken>(token, "line1\nline2\nline3\nEOF\n",
                        token => ValidateString(token, "line1\nline2\nline3\n"),
                        token => ValidateIdentifier<HeredocDelimiterToken>(token, "EOF"),
                        token => ValidateNewLine(token, "\n"))
                },
                Validate = result =>
                {
                    Assert.Single(result.HeredocTokens);
                    Assert.Equal("/app/config", result.Destination);
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    // ================================================================
    // SECTION: RUN instruction round-trip fidelity tests
    // ================================================================

    [Fact]
    public void Run_SimpleHeredoc_RoundTrips()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_SimpleHeredoc_HasHeredocToken()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Null(result.Command);
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_SimpleHeredoc_TokenStructure()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());

        Assert.Equal("RUN", result.InstructionName);
        Assert.Single(result.HeredocTokens);

        HeredocMarkerToken marker = result.HeredocTokens.First();
        Assert.Equal("<<EOF", marker.ToString());

        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("echo hello\nEOF\n", body.ToString());
    }

    [Fact]
    public void Run_HeredocWithChompFlag_RoundTrips()
    {
        string text = "RUN <<-EOF\n\techo hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocWithDoubleQuotedDelimiter_RoundTrips()
    {
        string text = "RUN <<\"EOF\"\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocWithSingleQuotedDelimiter_RoundTrips()
    {
        string text = "RUN <<'EOF'\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocMultipleBodyLines_RoundTrips()
    {
        string text = "RUN <<EOF\nline 1\nline 2\nline 3\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocEmptyBody_RoundTrips()
    {
        string text = "RUN <<EOF\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocWithCustomDelimiter_RoundTrips()
    {
        string text = "RUN <<SCRIPT\n#!/bin/bash\necho hello\nSCRIPT\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocNoTrailingNewline_RoundTrips()
    {
        string text = "RUN <<EOF\necho hello\nEOF";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocChompWithDoubleQuote_RoundTrips()
    {
        string text = "RUN <<-\"EOF\"\n\techo hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocChompWithSingleQuote_RoundTrips()
    {
        string text = "RUN <<-'EOF'\n\techo hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocWithShebangAndMultipleCommands_RoundTrips()
    {
        string text = "RUN <<EOF\n#!/bin/bash\nset -e\napt-get update\napt-get install -y curl\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocWithMountFlag_RoundTrips()
    {
        string text = "RUN --mount=type=secret,id=id <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
        Assert.Single(result.Mounts);
    }

    [Fact]
    public void Run_HeredocWithExtraWhitespaceBeforeMarker_RoundTrips()
    {
        // Extra whitespace between RUN keyword and heredoc marker
        string text = "RUN  <<EOF\ncontent\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    // ================================================================
    // SECTION: COPY instruction round-trip fidelity tests
    // ================================================================

    [Fact]
    public void Copy_SimpleHeredoc_RoundTrips()
    {
        string text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Copy_SimpleHeredoc_HasHeredocToken()
    {
        string text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Copy_HeredocWithQuotedDelimiter_RoundTrips()
    {
        string text = "COPY <<\"EOF\" /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Copy_HeredocWithSingleQuotedDelimiter_RoundTrips()
    {
        string text = "COPY <<'EOF' /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Copy_HeredocMultipleBodyLines_RoundTrips()
    {
        string text = "COPY <<EOF /app/config.txt\nline 1\nline 2\nline 3\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Copy_HeredocEmptyBody_RoundTrips()
    {
        string text = "COPY <<EOF /app/file.txt\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    // ================================================================
    // SECTION: ADD instruction round-trip fidelity tests
    // ================================================================

    [Fact]
    public void Add_SimpleHeredoc_RoundTrips()
    {
        string text = "ADD <<EOF /app/script.sh\necho hello\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Add_SimpleHeredoc_HasHeredocToken()
    {
        string text = "ADD <<EOF /app/script.sh\necho hello\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Add_HeredocWithQuotedDelimiter_RoundTrips()
    {
        string text = "ADD <<\"EOF\" /app/script.sh\necho hello\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Add_HeredocWithSingleQuotedDelimiter_RoundTrips()
    {
        string text = "ADD <<'EOF' /app/script.sh\necho hello\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Add_HeredocMultipleBodyLines_RoundTrips()
    {
        string text = "ADD <<EOF /app/config\nline1\nline2\nline3\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Add_HeredocEmptyBody_RoundTrips()
    {
        string text = "ADD <<EOF /app/file.txt\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    // ================================================================
    // SECTION: HeredocBodyToken.Content property extraction tests
    // ================================================================

    [Fact]
    public void HeredocBodyToken_Content_SingleLine()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("echo hello\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_MultiLine()
    {
        string text = "RUN <<EOF\nline 1\nline 2\nline 3\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("line 1\nline 2\nline 3\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_Empty()
    {
        string text = "RUN <<EOF\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_NoTrailingNewline()
    {
        string text = "RUN <<EOF\necho hello\nEOF";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("echo hello\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_SpecialCharacters()
    {
        string text = "RUN <<EOF\n$HOME=/root\necho \"hello world\"\necho 'single'\npath\\to\\file\necho `backtick`\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("$HOME=/root\necho \"hello world\"\necho 'single'\npath\\to\\file\necho `backtick`\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_WithEmptyLines()
    {
        string text = "RUN <<EOF\nline 1\n\nline 3\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("line 1\n\nline 3\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_WhitespaceOnly()
    {
        string text = "RUN <<EOF\n   \n\t\t\n  \t \nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("   \n\t\t\n  \t \n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_ChompFlag()
    {
        string text = "RUN <<-EOF\n\techo hello\n\techo world\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("\techo hello\n\techo world\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_DoubleQuotedDelimiter()
    {
        string text = "RUN <<\"EOF\"\necho $NOT_EXPANDED\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("echo $NOT_EXPANDED\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_SingleQuotedDelimiter()
    {
        string text = "RUN <<'EOF'\necho $NOT_EXPANDED\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("echo $NOT_EXPANDED\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_ShebangAndCommands()
    {
        string text = "RUN <<EOF\n#!/bin/bash\nset -e\necho done\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("#!/bin/bash\nset -e\necho done\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_CopyHeredoc()
    {
        string text = "COPY <<EOF /app/file.txt\nfile content here\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("file content here\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_AddHeredoc()
    {
        string text = "ADD <<EOF /app/file.txt\nfile content here\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("file content here\n", body.Content);
    }

    [Fact]
    public void HeredocBodyToken_Content_CRLF()
    {
        string text = "RUN <<EOF\r\necho hello\r\nEOF\r\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        Assert.Equal("echo hello\r\n", body.Content);
    }

    // ================================================================
    // SECTION: RunInstruction.Heredocs property tests
    // ================================================================

    [Fact]
    public void Run_Heredocs_SingleHeredoc()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Single(result.Heredocs);
        Assert.Equal("echo hello\n", result.Heredocs.First().Content);
    }

    [Fact]
    public void Run_Heredocs_ShellForm_Empty()
    {
        // Standard RUN with no heredoc returns empty Heredocs
        string text = "RUN echo hello";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Empty(result.Heredocs);
    }

    [Fact]
    public void Run_Heredocs_ExecForm_Empty()
    {
        // Exec-form RUN with no heredoc returns empty Heredocs
        string text = "RUN [\"/bin/bash\", \"-c\", \"echo hello\"]";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Empty(result.Heredocs);
    }

    [Fact]
    public void Run_Heredocs_EmptyBody()
    {
        string text = "RUN <<EOF\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Single(result.Heredocs);
        Assert.Equal("", result.Heredocs.First().Content);
    }

    [Fact]
    public void Run_Heredocs_MultiLineBody()
    {
        string text = "RUN <<EOF\nline 1\nline 2\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Single(result.Heredocs);
        Assert.Equal("line 1\nline 2\n", result.Heredocs.First().Content);
    }

    [Fact]
    public void Run_Heredocs_WithMountFlag()
    {
        string text = "RUN --mount=type=secret,id=id <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Single(result.Heredocs);
        Assert.Equal("echo hello\n", result.Heredocs.First().Content);
    }

    [Fact]
    public void Run_HeredocBodyTokens_MatchesHeredocsContent()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(
            result.HeredocBodyTokens.Select(h => h.Content).ToList(),
            result.Heredocs.Select(h => h.Content).ToList());
    }

    // ================================================================
    // SECTION: FileTransferInstruction.Heredocs property tests (via COPY)
    // ================================================================

    [Fact]
    public void Copy_Heredocs_WithHeredoc()
    {
        string text = "COPY <<EOF /app/file.txt\ncontent\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Single(result.Heredocs);
        Assert.Equal("content\n", result.Heredocs.First().Content);
    }

    [Fact]
    public void Copy_Heredocs_WithoutHeredoc_Empty()
    {
        string text = "COPY src dst";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Empty(result.Heredocs);
    }

    [Fact]
    public void Copy_Heredocs_MultiLineBody()
    {
        string text = "COPY <<EOF /app/file.txt\nline 1\nline 2\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Single(result.Heredocs);
        Assert.Equal("line 1\nline 2\n", result.Heredocs.First().Content);
    }

    [Fact]
    public void Copy_HeredocBodyTokens_MatchesHeredocsContent()
    {
        string text = "COPY <<EOF /app/file.txt\ncontent\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(
            result.HeredocBodyTokens.Select(h => h.Content).ToList(),
            result.Heredocs.Select(h => h.Content).ToList());
    }

    // ================================================================
    // SECTION: FileTransferInstruction.Heredocs property tests (via ADD)
    // ================================================================

    [Fact]
    public void Add_Heredocs_WithHeredoc()
    {
        string text = "ADD <<EOF /app/file.txt\ncontent\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Single(result.Heredocs);
        Assert.Equal("content\n", result.Heredocs.First().Content);
    }

    [Fact]
    public void Add_Heredocs_WithoutHeredoc_Empty()
    {
        string text = "ADD src dst";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Empty(result.Heredocs);
    }

    [Fact]
    public void Add_Heredocs_MultiLineBody()
    {
        string text = "ADD <<EOF /app/file.txt\nline 1\nline 2\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Single(result.Heredocs);
        Assert.Equal("line 1\nline 2\n", result.Heredocs.First().Content);
    }

    [Fact]
    public void Add_Heredocs_SpecialCharacters()
    {
        string text = "ADD <<EOF /app/file.txt\n$VAR\n\"quoted\"\n'single'\n\\backslash\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Single(result.Heredocs);
        Assert.Equal("$VAR\n\"quoted\"\n'single'\n\\backslash\n", result.Heredocs.First().Content);
    }

    // ================================================================
    // SECTION: Heredoc child token inspection
    // ================================================================

    [Fact]
    public void Run_HeredocChildTokens_AreCorrect()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);

        // Marker token children: SymbolToken('<'), SymbolToken('<'), HeredocDelimiterToken("EOF")
        HeredocMarkerToken marker = result.HeredocMarkerTokens.First();
        var markerChildren = marker.Tokens.ToList();
        Assert.Equal(3, markerChildren.Count);
        Assert.IsType<SymbolToken>(markerChildren[0]);
        Assert.Equal("<", ((SymbolToken)markerChildren[0]).Value);
        Assert.IsType<SymbolToken>(markerChildren[1]);
        Assert.Equal("<", ((SymbolToken)markerChildren[1]).Value);
        Assert.IsType<HeredocDelimiterToken>(markerChildren[2]);
        Assert.Equal("EOF", ((HeredocDelimiterToken)markerChildren[2]).Value);

        // Body token children: StringToken("echo hello\n"), HeredocDelimiterToken("EOF"), NewLineToken("\n")
        HeredocBodyToken body = result.HeredocBodyTokens.First();
        var bodyChildren = body.Tokens.ToList();
        Assert.Equal(3, bodyChildren.Count);
        Assert.IsType<StringToken>(bodyChildren[0]);
        Assert.IsType<HeredocDelimiterToken>(bodyChildren[1]);
        Assert.IsType<NewLineToken>(bodyChildren[2]);

        // All children concatenated must equal full text
        Assert.Equal("<<EOF", marker.ToString());
        Assert.Equal("echo hello\nEOF\n", body.ToString());
    }

    [Fact]
    public void Run_HeredocWithChompFlag_ChildTokens()
    {
        string text = "RUN <<-EOF\n\techo hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocMarkerToken marker = result.HeredocMarkerTokens.First();

        var children = marker.Tokens.ToList();
        Assert.Equal(4, children.Count);
        Assert.IsType<SymbolToken>(children[0]);
        Assert.Equal("<", ((SymbolToken)children[0]).Value);
        Assert.IsType<SymbolToken>(children[1]);
        Assert.Equal("<", ((SymbolToken)children[1]).Value);
        Assert.IsType<SymbolToken>(children[2]);
        Assert.Equal("-", ((SymbolToken)children[2]).Value);
        Assert.IsType<HeredocDelimiterToken>(children[3]);
        Assert.Equal("EOF", ((HeredocDelimiterToken)children[3]).Value);
        Assert.Equal("<<-EOF", marker.ToString());
    }

    [Fact]
    public void Run_HeredocQuotedDelimiter_ChildTokens()
    {
        string text = "RUN <<\"EOF\"\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocMarkerToken marker = result.HeredocMarkerTokens.First();

        var children = marker.Tokens.ToList();
        Assert.Equal(5, children.Count);
        Assert.IsType<SymbolToken>(children[0]);
        Assert.Equal("<", ((SymbolToken)children[0]).Value);
        Assert.IsType<SymbolToken>(children[1]);
        Assert.Equal("<", ((SymbolToken)children[1]).Value);
        Assert.IsType<SymbolToken>(children[2]);
        Assert.Equal("\"", ((SymbolToken)children[2]).Value);
        Assert.IsType<HeredocDelimiterToken>(children[3]);
        Assert.Equal("EOF", ((HeredocDelimiterToken)children[3]).Value);
        Assert.IsType<SymbolToken>(children[4]);
        Assert.Equal("\"", ((SymbolToken)children[4]).Value);
        Assert.Equal("<<\"EOF\"", marker.ToString());
    }

    // ================================================================
    // SECTION: Round-trip fidelity edge cases
    // ================================================================

    [Fact]
    public void Run_HeredocPreservesExactWhitespace()
    {
        string text = "RUN <<EOF\n  indented line\n    double indented\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocPreservesEmptyLines()
    {
        string text = "RUN <<EOF\nline 1\n\nline 3\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocPreservesSpecialCharacters()
    {
        string text = "RUN <<EOF\n$HOME=/root\necho \"hello world\"\necho 'single'\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocPreservesBackslashAndBacktick()
    {
        string text = "RUN <<EOF\npath\\to\\file\necho `date`\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocDelimiterWithNumbers_RoundTrips()
    {
        string text = "RUN <<EOF123\necho hello\nEOF123\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocDelimiterWithUnderscore_RoundTrips()
    {
        string text = "RUN <<MY_EOF\necho hello\nMY_EOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocQuotedDelimiterWithHyphen_RoundTrips()
    {
        string text = "RUN <<'MY-DELIM'\necho hello\nMY-DELIM\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocBodyContainsHeredocMarkerSyntax_RoundTrips()
    {
        // Body contains << characters but they are not heredoc markers
        string text = "RUN <<EOF\necho \"use <<EOF to start\"\nvalue << 2\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocBodyWithWhitespaceOnlyLines_RoundTrips()
    {
        string text = "RUN <<EOF\n   \n\t\t\n  \t \nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocDelimiterLooksLikeInstruction_RoundTrips()
    {
        // Delimiter name that is a valid Dockerfile instruction keyword
        string text = "RUN <<RUN\necho inside heredoc\nRUN\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocDelimiterLooksLikeFROM_RoundTrips()
    {
        string text = "RUN <<FROM\necho inside heredoc\nFROM\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocDelimiterLooksLikeCOPY_RoundTrips()
    {
        string text = "RUN <<COPY\necho inside heredoc\nCOPY\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocVeryLongBody_RoundTrips()
    {
        // Build a heredoc with 100 lines of body content
        var lines = new List<string> { "RUN <<EOF" };
        for (int i = 0; i < 100; i++)
        {
            lines.Add($"echo line {i}");
        }
        lines.Add("EOF");
        lines.Add(""); // trailing newline
        string text = TestHelper.ConcatLines(lines);

        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocWithTabsAndSpacesMixed_RoundTrips()
    {
        string text = "RUN <<EOF\n\tline with tab\n  line with spaces\n\t  mixed\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    // ================================================================
    // SECTION: Chomp flag (<<-) delimiter matching edge cases
    // ================================================================

    [Fact]
    public void Run_NonChompHeredoc_TabIndentedDelimiterIsBody()
    {
        // <<EOF (no chomp): "\tEOF" is body text, not a closing delimiter
        string text = "RUN <<EOF\n\tEOF\nactual content\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);

        HeredocMarkerToken marker = result.HeredocTokens.First();
        Assert.Equal("<<EOF", marker.ToString());
    }

    [Fact]
    public void Run_ChompHeredoc_TabIndentedDelimiterClosesHeredoc()
    {
        // <<-EOF (chomp): "\tEOF" SHOULD close the heredoc
        string text = "RUN <<-EOF\n\techo hello\n\tEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_ChompHeredoc_MultipleTabsBeforeDelimiter_RoundTrips()
    {
        string text = "RUN <<-EOF\n\t\techo hello\n\t\tEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    // ================================================================
    // SECTION: CRLF line ending tests
    // ================================================================

    [Fact]
    public void Run_HeredocWithCRLF_RoundTrips()
    {
        string text = "RUN <<EOF\r\necho hello\r\nEOF\r\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Copy_HeredocWithCRLF_RoundTrips()
    {
        string text = "COPY <<EOF /app/file.txt\r\ncontent\r\nEOF\r\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    // ================================================================
    // SECTION: Dockerfile-level round-trip tests
    // ================================================================

    [Fact]
    public void Dockerfile_WithHeredocRun_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<EOF\necho hello\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_WithHeredocCopy_RoundTrips()
    {
        string text = "FROM ubuntu\nCOPY <<EOF /app/script.sh\necho hello\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_WithHeredocAdd_RoundTrips()
    {
        string text = "FROM ubuntu\nADD <<EOF /app/script.sh\nfile content\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_HeredocFollowedByInstruction_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<EOF\necho hello\nEOF\nRUN echo world\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_MultipleHeredocInstructions_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<EOF\necho hello\nEOF\nRUN <<SCRIPT\necho world\nSCRIPT\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_HeredocBetweenInstructions_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN echo before\nRUN <<EOF\necho hello\nEOF\nRUN echo after\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(4, result.Items.Count);
    }

    [Fact]
    public void Dockerfile_HeredocWithMultipleStages_RoundTrips()
    {
        string text = "FROM ubuntu AS builder\nRUN <<EOF\necho building\nEOF\nFROM alpine\nRUN echo done\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_AddHeredocBetweenInstructions_RoundTrips()
    {
        string text = "FROM ubuntu\nADD <<EOF /app/config.txt\nkey=value\nEOF\nRUN echo done\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_CopyHeredocBetweenFromInstructions_RoundTrips()
    {
        // Heredoc COPY between two FROM instructions in multi-stage build
        string text = "FROM ubuntu AS stage1\nCOPY <<EOF /app/config.txt\nconfig data\nEOF\nFROM alpine AS stage2\nRUN echo hello\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_HeredocAtEndOfFile_NoTrailingNewline_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<EOF\necho hello\nEOF";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_MultipleHeredocTypes_RoundTrips()
    {
        // Mix of RUN, COPY, ADD heredocs in a single Dockerfile
        string text = TestHelper.ConcatLines(new List<string>
        {
            "FROM ubuntu",
            "RUN <<EOF",
            "echo hello",
            "EOF",
            "COPY <<CONF /app/config.txt",
            "key=value",
            "CONF",
            "ADD <<DATA /app/data.txt",
            "some data",
            "DATA",
            "RUN echo done",
            ""
        });
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_NonChompHeredoc_TabIndentedDelimiterLineIsBodyNotClose()
    {
        // With non-chomp <<EOF, a body line "\tEOF" should NOT close the heredoc.
        string text = "FROM ubuntu\nRUN <<EOF\n\tEOF\nreal body\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public void Dockerfile_HeredocWithCommentBefore_RoundTrips()
    {
        string text = "FROM ubuntu\n# This is a comment\nRUN <<EOF\necho hello\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_HeredocFollowedByComment_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<EOF\necho hello\nEOF\n# This is a comment\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_ThreeStagesWithHeredocs_RoundTrips()
    {
        string text = TestHelper.ConcatLines(new List<string>
        {
            "FROM ubuntu AS build",
            "RUN <<EOF",
            "apt-get update",
            "apt-get install -y gcc",
            "EOF",
            "FROM alpine AS test",
            "COPY <<EOF /app/test.sh",
            "#!/bin/sh",
            "echo testing",
            "EOF",
            "FROM scratch",
            "COPY --from=build /app /app",
            ""
        });
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    // ================================================================
    // SECTION: RunInstruction.Command setter behavior on heredoc
    // ================================================================

    [Fact]
    public void Run_Heredoc_CommandIsNull()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Null(result.Command);
    }

    [Fact]
    public void Run_Heredoc_SetCommand_ThrowsInvalidOperation()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Null(result.Command);

        Assert.Throws<InvalidOperationException>(() =>
            result.Command = ShellFormCommand.Parse("echo world"));
    }

    // ================================================================
    // SECTION: COPY/ADD heredoc Destination -- now properly tokenized
    // ================================================================

    [Fact]
    public void Copy_Heredoc_SourcesIsEmpty()
    {
        string text = "COPY <<EOF /app/script.sh\necho hello\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void Copy_Heredoc_TrailingComment_DestinationNotPolluted()
    {
        string text = "COPY <<EOF /dest # comment\nfile content\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);

        // The trailing comment should NOT be tokenized as LiteralTokens,
        // so DestinationToken (LastOrDefault LiteralToken) should be "/dest", not "comment"
        Assert.Equal("/dest", result.Destination);

        // Round-trip fidelity must be preserved
        Assert.Equal(text, result.ToString());
    }

    // ================================================================
    // SECTION: Variable resolution with heredoc
    // ================================================================

    [Fact]
    public void Run_HeredocResolveVariables_ReturnsOriginalText()
    {
        // RUN instructions don't resolve variables (shell handles them)
        string text = "RUN <<EOF\necho $HOME\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        string? resolved = result.ResolveVariables('\\');
        Assert.Equal(text, resolved);
    }

    // ================================================================
    // SECTION: DockerfileParser.ExtractHeredocDelimiters tests
    // ================================================================

    [Fact]
    public void ExtractHeredocDelimiters_UnquotedDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<EOF\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_DoubleQuotedDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<\"EOF\"\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_SingleQuotedDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<'EOF'\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_ChompFlagDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<-EOF\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
        Assert.True(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_NoHeredoc()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN echo hello\n");
        Assert.Empty(delimiters);
    }

    [Fact]
    public void ExtractHeredocDelimiters_CustomDelimiterName()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<MY_SCRIPT_123\n");
        Assert.Single(delimiters);
        Assert.Equal("MY_SCRIPT_123", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_ChompQuotedDelimiter_HasChompTrue()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<-\"EOF\"\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
        Assert.True(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_QuotedHyphenatedDelimiter()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<'MY-DELIM'\n");
        Assert.Single(delimiters);
        Assert.Equal("MY-DELIM", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_QuotedDelimiterCharset_MatchesParser()
    {
        // Verify detection AND parsing are aligned for hyphenated quoted delimiters
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<\"MY-DELIM\"\n");
        Assert.Single(delimiters);
        Assert.Equal("MY-DELIM", delimiters[0].Delimiter);

        string text = "RUN <<\"MY-DELIM\"\necho hello\nMY-DELIM\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void ExtractHeredocDelimiters_QuotedDelimiterWithSpaces()
    {
        // Quoted delimiters accept any non-quote characters, including spaces
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<'EOF SPACE'\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF SPACE", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_DelimiterWithDot()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<FILE.TXT\n");
        Assert.Single(delimiters);
        Assert.Equal("FILE.TXT", delimiters[0].Delimiter);
        Assert.False(delimiters[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_CopyInstruction()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("COPY <<EOF /dest\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
    }

    [Fact]
    public void ExtractHeredocDelimiters_AddInstruction()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("ADD <<EOF /dest\n");
        Assert.Single(delimiters);
        Assert.Equal("EOF", delimiters[0].Delimiter);
    }

    // ================================================================
    // SECTION: Trailing comment stripping (prevents false heredoc detection)
    // ================================================================

    [Fact]
    public void ExtractHeredocDelimiters_MarkerInTrailingComment_ReturnsEmpty()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN echo hi # <<EOF\n");
        Assert.Empty(delimiters);
    }

    [Fact]
    public void Dockerfile_RunWithHeredocMarkerInComment_DoesNotEnterHeredocMode()
    {
        string text = "FROM ubuntu\nRUN echo hi # <<EOF\nRUN echo world\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public void StripTrailingComment_NoComment_ReturnsOriginal()
    {
        string line = "RUN echo hello\n";
        Assert.Equal(line, DockerfileParser.StripTrailingComment(line));
    }

    [Fact]
    public void StripTrailingComment_WithComment_ReturnsBeforeHash()
    {
        Assert.Equal("RUN echo hi ", DockerfileParser.StripTrailingComment("RUN echo hi # <<EOF\n"));
    }

    [Fact]
    public void StripTrailingComment_HashInsideSingleQuotes_NotTreatedAsComment()
    {
        string line = "RUN echo '#notacomment' world";
        Assert.Equal(line, DockerfileParser.StripTrailingComment(line));
    }

    [Fact]
    public void StripTrailingComment_HashInsideDoubleQuotes_NotTreatedAsComment()
    {
        string line = "RUN echo \"#notacomment\" world";
        Assert.Equal(line, DockerfileParser.StripTrailingComment(line));
    }

    [Fact]
    public void StripTrailingComment_HashAfterClosingQuote_TreatedAsComment()
    {
        Assert.Equal("RUN echo 'hello' ", DockerfileParser.StripTrailingComment("RUN echo 'hello' # comment <<EOF"));
    }

    // ================================================================
    // SECTION: Edge cases -- body content with unusual patterns
    // ================================================================

    [Fact]
    public void Run_HeredocBodyWithDollarSigns_RoundTrips()
    {
        string text = "RUN <<EOF\necho $HOME\nexport PATH=$PATH:/usr/local/bin\n${VARIABLE:-default}\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocBodyWithDoubleQuotes_RoundTrips()
    {
        string text = "RUN <<EOF\necho \"hello world\"\necho \"nested \\\"quotes\\\"\"\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocBodyWithSingleQuotes_RoundTrips()
    {
        string text = "RUN <<EOF\necho 'hello world'\necho 'it'\\''s fine'\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocBodyWithBackslashes_RoundTrips()
    {
        string text = "RUN <<EOF\npath\\to\\file\necho \\n \\t\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocBodyWithOnlyBlankLines_RoundTrips()
    {
        string text = "RUN <<EOF\n\n\n\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocBodyWithHeredocRedirectionSyntax_RoundTrips()
    {
        // The body itself contains << which should not be misinterpreted
        string text = "RUN <<EOF\ncat <<INNER\nhello\nINNER\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocBodyWithSubstringOfDelimiter_RoundTrips()
    {
        // Body contains a substring of the delimiter but not the exact delimiter
        string text = "RUN <<MYEOF\nMYEO\nEOF\nMYEOFX\nMYEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_HeredocBodySingleCharacterDelimiter_RoundTrips()
    {
        string text = "RUN <<X\nhello\nX\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocDelimiterWithDot_RoundTrips()
    {
        string text = "RUN <<FILE.TXT\ncontent\nFILE.TXT\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Run_HeredocBodyWithLongLines_RoundTrips()
    {
        string longLine = new string('a', 500);
        string text = $"RUN <<EOF\n{longLine}\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    // ================================================================
    // SECTION: Heredoc immediately followed by next instruction (Dockerfile level)
    // ================================================================

    [Fact]
    public void Dockerfile_HeredocImmediatelyFollowedByFrom_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<EOF\necho hello\nEOF\nFROM alpine\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
        // Should parse as 3 items: FROM ubuntu, RUN heredoc, FROM alpine
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public void Dockerfile_HeredocImmediatelyFollowedByCopy_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<EOF\necho hello\nEOF\nCOPY src dst\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(3, result.Items.Count);
    }

    // ================================================================
    // SECTION: Case insensitivity of instruction keyword with heredoc
    // ================================================================

    [Fact]
    public void Run_LowercaseKeyword_HeredocRoundTrips()
    {
        string text = "run <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Copy_LowercaseKeyword_HeredocRoundTrips()
    {
        string text = "copy <<EOF /app/file.txt\ncontent\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    [Fact]
    public void Add_LowercaseKeyword_HeredocRoundTrips()
    {
        string text = "add <<EOF /app/file.txt\ncontent\nEOF\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Single(result.HeredocTokens);
    }

    // ================================================================
    // SECTION: Heredoc with ARG/ENV in Dockerfile context
    // ================================================================

    [Fact]
    public void Dockerfile_HeredocWithArgBefore_RoundTrips()
    {
        string text = "FROM ubuntu\nARG MESSAGE=hello\nRUN <<EOF\necho $MESSAGE\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_HeredocWithEnvBefore_RoundTrips()
    {
        string text = "FROM ubuntu\nENV APP_DIR=/app\nCOPY <<EOF $APP_DIR/config.txt\nkey=value\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    // ================================================================
    // SECTION: Comprehensive integration -- complex Dockerfile with heredocs
    // ================================================================

    [Fact]
    public void Dockerfile_ComplexMultiStageWithHeredocs_RoundTrips()
    {
        string text = TestHelper.ConcatLines(new List<string>
        {
            "ARG BASE_IMAGE=ubuntu:22.04",
            "FROM $BASE_IMAGE AS builder",
            "RUN <<EOF",
            "#!/bin/bash",
            "set -e",
            "apt-get update",
            "apt-get install -y build-essential",
            "EOF",
            "COPY <<EOF /app/Makefile",
            "all:",
            "\tgcc -o app main.c",
            "EOF",
            "RUN <<COMPILE",
            "cd /app",
            "make all",
            "COMPILE",
            "FROM alpine:3.18",
            "COPY --from=builder /app/app /usr/local/bin/app",
            "RUN echo done",
            ""
        });
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    // ================================================================
    // SECTION: Heredoc with parser directive (escape char)
    // ================================================================

    [Fact]
    public void Dockerfile_WithEscapeDirectiveAndHeredoc_RoundTrips()
    {
        string text = "# escape=`\nFROM ubuntu\nRUN <<EOF\necho hello\nEOF\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal('`', result.EscapeChar);
    }

    // ================================================================
    // SECTION: Multi-heredoc per instruction -- RUN
    // ================================================================

    [Fact]
    public void Run_TwoHeredocs_RoundTrips()
    {
        string text = "RUN <<FILE1 cat > /file1 && <<FILE2 cat > /file2\ncontent of file1\nFILE1\ncontent of file2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Run_TwoHeredocs_HeredocTokensCount()
    {
        string text = "RUN <<FILE1 cat > /file1 && <<FILE2 cat > /file2\ncontent of file1\nFILE1\ncontent of file2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(2, result.HeredocTokens.Count());
    }

    [Fact]
    public void Run_TwoHeredocs_DelimiterNames()
    {
        string text = "RUN <<FILE1 cat > /file1 && <<FILE2 cat > /file2\ncontent of file1\nFILE1\ncontent of file2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        var heredocs = result.HeredocTokens.ToList();
        Assert.Equal("FILE1", heredocs[0].DelimiterName);
        Assert.Equal("FILE2", heredocs[1].DelimiterName);
    }

    [Fact]
    public void Run_TwoHeredocs_Bodies()
    {
        string text = "RUN <<FILE1 cat > /file1 && <<FILE2 cat > /file2\ncontent of file1\nFILE1\ncontent of file2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        var bodies = result.HeredocBodyTokens.ToList();
        Assert.Equal("content of file1\n", bodies[0].Content);
        Assert.Equal("content of file2\n", bodies[1].Content);
    }

    [Fact]
    public void Run_TwoHeredocs_HeredocsProperty()
    {
        string text = "RUN <<FILE1 cat > /file1 && <<FILE2 cat > /file2\ncontent of file1\nFILE1\ncontent of file2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        var heredocs = result.Heredocs;
        Assert.Equal(2, heredocs.Count);
        Assert.Equal("content of file1\n", heredocs[0].Content);
        Assert.Equal("content of file2\n", heredocs[1].Content);
    }

    [Fact]
    public void Run_TwoHeredocs_CommandIsNull()
    {
        string text = "RUN <<FILE1 cat > /file1 && <<FILE2 cat > /file2\ncontent of file1\nFILE1\ncontent of file2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Null(result.Command);
    }

    [Fact]
    public void Run_TwoHeredocs_EmptyBodies_RoundTrips()
    {
        string text = "RUN <<FILE1 <<FILE2\nFILE1\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(2, result.HeredocTokens.Count());
        var bodies = result.HeredocBodyTokens.ToList();
        Assert.Equal("", bodies[0].Content);
        Assert.Equal("", bodies[1].Content);
    }

    [Fact]
    public void Run_TwoHeredocs_MultiLineBodies_RoundTrips()
    {
        string text = "RUN <<EOF1 <<EOF2\nline1a\nline1b\nEOF1\nline2a\nline2b\nEOF2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        var bodies = result.HeredocBodyTokens.ToList();
        Assert.Equal("line1a\nline1b\n", bodies[0].Content);
        Assert.Equal("line2a\nline2b\n", bodies[1].Content);
    }

    [Fact]
    public void Run_ThreeHeredocs_RoundTrips()
    {
        string text = "RUN <<A <<B <<C\nbody_a\nA\nbody_b\nB\nbody_c\nC\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(3, result.HeredocTokens.Count());
    }

    [Fact]
    public void Run_ThreeHeredocs_DelimiterNames()
    {
        string text = "RUN <<A <<B <<C\nbody_a\nA\nbody_b\nB\nbody_c\nC\n";
        RunInstruction result = RunInstruction.Parse(text);
        var heredocs = result.HeredocTokens.ToList();
        Assert.Equal("A", heredocs[0].DelimiterName);
        Assert.Equal("B", heredocs[1].DelimiterName);
        Assert.Equal("C", heredocs[2].DelimiterName);
    }

    [Fact]
    public void Run_ThreeHeredocs_Bodies()
    {
        string text = "RUN <<A <<B <<C\nbody_a\nA\nbody_b\nB\nbody_c\nC\n";
        RunInstruction result = RunInstruction.Parse(text);
        var bodies = result.HeredocBodyTokens.ToList();
        Assert.Equal("body_a\n", bodies[0].Content);
        Assert.Equal("body_b\n", bodies[1].Content);
        Assert.Equal("body_c\n", bodies[2].Content);
    }

    [Fact]
    public void Run_TwoHeredocs_WithChompFlag_RoundTrips()
    {
        string text = "RUN <<-FILE1 <<-FILE2\n\tcontent1\nFILE1\n\tcontent2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        var heredocs = result.HeredocTokens.ToList();
        Assert.True(heredocs[0].Chomp);
        Assert.True(heredocs[1].Chomp);
    }

    [Fact]
    public void Run_TwoHeredocs_MixedQuoting_RoundTrips()
    {
        string text = "RUN <<\"FILE1\" <<'FILE2'\ncontent1\nFILE1\ncontent2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        var heredocs = result.HeredocTokens.ToList();
        Assert.True(heredocs[0].IsQuoted);
        Assert.True(heredocs[1].IsQuoted);
        Assert.Equal("FILE1", heredocs[0].DelimiterName);
        Assert.Equal("FILE2", heredocs[1].DelimiterName);
    }

    [Fact]
    public void Run_TwoHeredocs_WithCommandText_RoundTrips()
    {
        string text = "RUN <<EOF1 cat > /out1 && <<EOF2 cat > /out2 && echo done\nfirst\nEOF1\nsecond\nEOF2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(2, result.HeredocTokens.Count());
    }

    [Fact]
    public void Run_TwoHeredocs_WithMountFlag_RoundTrips()
    {
        string text = "RUN --mount=type=secret,id=id <<FILE1 <<FILE2\ncontent1\nFILE1\ncontent2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(2, result.HeredocTokens.Count());
        Assert.Single(result.Mounts);
    }

    // ================================================================
    // SECTION: Multi-heredoc per instruction -- COPY
    // ================================================================

    [Fact]
    public void Copy_TwoHeredocs_RoundTrips()
    {
        string text = "COPY <<file1.txt <<file2.txt /dest/\ncontent1\nfile1.txt\ncontent2\nfile2.txt\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Copy_TwoHeredocs_HeredocTokensCount()
    {
        string text = "COPY <<file1.txt <<file2.txt /dest/\ncontent1\nfile1.txt\ncontent2\nfile2.txt\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(2, result.HeredocTokens.Count());
    }

    [Fact]
    public void Copy_TwoHeredocs_DelimiterNames()
    {
        string text = "COPY <<file1.txt <<file2.txt /dest/\ncontent1\nfile1.txt\ncontent2\nfile2.txt\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        var heredocs = result.HeredocTokens.ToList();
        Assert.Equal("file1.txt", heredocs[0].DelimiterName);
        Assert.Equal("file2.txt", heredocs[1].DelimiterName);
    }

    [Fact]
    public void Copy_TwoHeredocs_Bodies()
    {
        string text = "COPY <<file1.txt <<file2.txt /dest/\ncontent1\nfile1.txt\ncontent2\nfile2.txt\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        var bodies = result.HeredocBodyTokens.ToList();
        Assert.Equal("content1\n", bodies[0].Content);
        Assert.Equal("content2\n", bodies[1].Content);
    }

    [Fact]
    public void Copy_TwoHeredocs_HeredocsProperty()
    {
        string text = "COPY <<file1.txt <<file2.txt /dest/\ncontent1\nfile1.txt\ncontent2\nfile2.txt\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        var heredocs = result.Heredocs;
        Assert.Equal(2, heredocs.Count);
        Assert.Equal("content1\n", heredocs[0].Content);
        Assert.Equal("content2\n", heredocs[1].Content);
    }

    // ================================================================
    // SECTION: Multi-heredoc per instruction -- ADD
    // ================================================================

    [Fact]
    public void Add_TwoHeredocs_RoundTrips()
    {
        string text = "ADD <<file1.txt <<file2.txt /dest/\ncontent1\nfile1.txt\ncontent2\nfile2.txt\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Add_TwoHeredocs_HeredocTokensCount()
    {
        string text = "ADD <<file1.txt <<file2.txt /dest/\ncontent1\nfile1.txt\ncontent2\nfile2.txt\n";
        AddInstruction result = AddInstruction.Parse(text);
        Assert.Equal(2, result.HeredocTokens.Count());
    }

    [Fact]
    public void Add_TwoHeredocs_Bodies()
    {
        string text = "ADD <<file1.txt <<file2.txt /dest/\ncontent1\nfile1.txt\ncontent2\nfile2.txt\n";
        AddInstruction result = AddInstruction.Parse(text);
        var bodies = result.HeredocBodyTokens.ToList();
        Assert.Equal("content1\n", bodies[0].Content);
        Assert.Equal("content2\n", bodies[1].Content);
    }

    // ================================================================
    // SECTION: Multi-heredoc Dockerfile-level round-trip tests
    // ================================================================

    [Fact]
    public void Dockerfile_MultiHeredocRun_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<FILE1 cat > /file1 && <<FILE2 cat > /file2\ncontent1\nFILE1\ncontent2\nFILE2\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_MultiHeredocCopy_RoundTrips()
    {
        string text = "FROM ubuntu\nCOPY <<file1.txt <<file2.txt /dest/\ncontent1\nfile1.txt\ncontent2\nfile2.txt\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    [Fact]
    public void Dockerfile_MultiHeredocFollowedByInstruction_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<A <<B\nbody_a\nA\nbody_b\nB\nRUN echo done\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public void Dockerfile_ThreeHeredocsInRun_RoundTrips()
    {
        string text = "FROM ubuntu\nRUN <<A <<B <<C\nbody_a\nA\nbody_b\nB\nbody_c\nC\n";
        Dockerfile result = Dockerfile.Parse(text);
        Assert.Equal(text, result.ToString());
    }

    // ================================================================
    // SECTION: Multi-heredoc edge cases
    // ================================================================

    [Fact]
    public void Run_TwoHeredocs_NoTrailingNewline_RoundTrips()
    {
        string text = "RUN <<A <<B\nbody_a\nA\nbody_b\nB";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(2, result.HeredocTokens.Count());
    }

    [Fact]
    public void Run_TwoHeredocs_CRLF_RoundTrips()
    {
        string text = "RUN <<A <<B\r\nbody_a\r\nA\r\nbody_b\r\nB\r\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        Assert.Equal(2, result.HeredocTokens.Count());
    }

    [Fact]
    public void Run_TwoHeredocs_ComplexCommandText_RoundTrips()
    {
        // Heredoc markers interleaved with complex shell commands
        string text = "RUN <<SCRIPT1 /bin/bash -c 'process' && <<SCRIPT2 /bin/sh\n#!/bin/bash\necho first\nSCRIPT1\n#!/bin/sh\necho second\nSCRIPT2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        var heredocs = result.HeredocTokens.ToList();
        Assert.Equal("SCRIPT1", heredocs[0].DelimiterName);
        Assert.Equal("SCRIPT2", heredocs[1].DelimiterName);
        var bodies = result.HeredocBodyTokens.ToList();
        Assert.Equal("#!/bin/bash\necho first\n", bodies[0].Content);
        Assert.Equal("#!/bin/sh\necho second\n", bodies[1].Content);
    }

    [Fact]
    public void Run_TwoHeredocs_FirstEmptySecondFull_RoundTrips()
    {
        string text = "RUN <<A <<B\nA\ncontent_b\nB\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        var bodies = result.HeredocBodyTokens.ToList();
        Assert.Equal("", bodies[0].Content);
        Assert.Equal("content_b\n", bodies[1].Content);
    }

    [Fact]
    public void Run_TwoHeredocs_FirstFullSecondEmpty_RoundTrips()
    {
        string text = "RUN <<A <<B\ncontent_a\nA\nB\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());
        var bodies = result.HeredocBodyTokens.ToList();
        Assert.Equal("content_a\n", bodies[0].Content);
        Assert.Equal("", bodies[1].Content);
    }

    [Fact]
    public void ExtractHeredocDelimiters_MultipleMarkers()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<FILE1 <<FILE2\n");
        Assert.Equal(2, delimiters.Count);
        Assert.Equal("FILE1", delimiters[0].Delimiter);
        Assert.Equal("FILE2", delimiters[1].Delimiter);
    }

    [Fact]
    public void ExtractHeredocDelimiters_ThreeMarkers()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<A <<B <<C\n");
        Assert.Equal(3, delimiters.Count);
        Assert.Equal("A", delimiters[0].Delimiter);
        Assert.Equal("B", delimiters[1].Delimiter);
        Assert.Equal("C", delimiters[2].Delimiter);
    }

    [Fact]
    public void ExtractHeredocDelimiters_MultipleWithCommandText()
    {
        var delimiters = DockerfileParser.ExtractHeredocDelimiters("RUN <<FILE1 cat > /f1 && <<FILE2 cat > /f2\n");
        Assert.Equal(2, delimiters.Count);
        Assert.Equal("FILE1", delimiters[0].Delimiter);
        Assert.Equal("FILE2", delimiters[1].Delimiter);
    }

    // ================================================================
    // SECTION: Multi-heredoc token tree structure validation
    // ================================================================

    [Fact]
    public void Run_TwoHeredocs_TokenTreeStructure()
    {
        string text = "RUN <<FILE1 cat > /file1 && <<FILE2 cat > /file2\ncontent of file1\nFILE1\ncontent of file2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());

        // Instruction-level tokens: KeywordToken, WhitespaceToken, HeredocMarkerToken, StringToken (gap),
        // HeredocMarkerToken, then rest-of-line tokenized into WhitespaceToken + LiteralToken segments,
        // NewLineToken, HeredocBodyToken, HeredocBodyToken
        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "RUN"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<HeredocMarkerToken>(token, "<<FILE1",
                t => ValidateSymbol(t, '<'),
                t => ValidateSymbol(t, '<'),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "FILE1")),
            token => ValidateString(token, " cat > /file1 && "),
            token => ValidateAggregate<HeredocMarkerToken>(token, "<<FILE2",
                t => ValidateSymbol(t, '<'),
                t => ValidateSymbol(t, '<'),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "FILE2")),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteral(token, "cat"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteral(token, ">"),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteral(token, "/file2"),
            token => ValidateNewLine(token, "\n"),
            token => ValidateAggregate<HeredocBodyToken>(token,
                "content of file1\nFILE1\n",
                t => ValidateString(t, "content of file1\n"),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "FILE1"),
                t => ValidateNewLine(t, "\n")),
            token => ValidateAggregate<HeredocBodyToken>(token,
                "content of file2\nFILE2\n",
                t => ValidateString(t, "content of file2\n"),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "FILE2"),
                t => ValidateNewLine(t, "\n")));
    }

    [Fact]
    public void Run_TwoHeredocs_EmptyBodies_TokenTreeStructure()
    {
        string text = "RUN <<FILE1 <<FILE2\nFILE1\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());

        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "RUN"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<HeredocMarkerToken>(token, "<<FILE1",
                t => ValidateSymbol(t, '<'),
                t => ValidateSymbol(t, '<'),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "FILE1")),
            token => ValidateString(token, " "),
            token => ValidateAggregate<HeredocMarkerToken>(token, "<<FILE2",
                t => ValidateSymbol(t, '<'),
                t => ValidateSymbol(t, '<'),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "FILE2")),
            token => ValidateNewLine(token, "\n"),
            token => ValidateAggregate<HeredocBodyToken>(token, "FILE1\n",
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "FILE1"),
                t => ValidateNewLine(t, "\n")),
            token => ValidateAggregate<HeredocBodyToken>(token, "FILE2\n",
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "FILE2"),
                t => ValidateNewLine(t, "\n")));
    }

    [Fact]
    public void Copy_TwoHeredocs_TokenTreeStructure()
    {
        string text = "COPY <<file1.txt <<file2.txt /dest/\ncontent1\nfile1.txt\ncontent2\nfile2.txt\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        Assert.Equal(text, result.ToString());

        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "COPY"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<HeredocMarkerToken>(token, "<<file1.txt",
                t => ValidateSymbol(t, '<'),
                t => ValidateSymbol(t, '<'),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "file1.txt")),
            token => ValidateString(token, " "),
            token => ValidateAggregate<HeredocMarkerToken>(token, "<<file2.txt",
                t => ValidateSymbol(t, '<'),
                t => ValidateSymbol(t, '<'),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "file2.txt")),
            token => ValidateWhitespace(token, " "),
            token => ValidateLiteral(token, "/dest/"),
            token => ValidateNewLine(token, "\n"),
            token => ValidateAggregate<HeredocBodyToken>(token, "content1\nfile1.txt\n",
                t => ValidateString(t, "content1\n"),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "file1.txt"),
                t => ValidateNewLine(t, "\n")),
            token => ValidateAggregate<HeredocBodyToken>(token, "content2\nfile2.txt\n",
                t => ValidateString(t, "content2\n"),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "file2.txt"),
                t => ValidateNewLine(t, "\n")));

        // Verify destination is properly tokenized
        Assert.Equal("/dest/", result.Destination);
    }

    [Fact]
    public void Run_ThreeHeredocs_TokenTreeStructure()
    {
        string text = "RUN <<A <<B <<C\nbody_a\nA\nbody_b\nB\nbody_c\nC\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());

        Assert.Collection(result.Tokens,
            token => ValidateKeyword(token, "RUN"),
            token => ValidateWhitespace(token, " "),
            token => ValidateAggregate<HeredocMarkerToken>(token, "<<A",
                t => ValidateSymbol(t, '<'),
                t => ValidateSymbol(t, '<'),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "A")),
            token => ValidateString(token, " "),
            token => ValidateAggregate<HeredocMarkerToken>(token, "<<B",
                t => ValidateSymbol(t, '<'),
                t => ValidateSymbol(t, '<'),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "B")),
            token => ValidateString(token, " "),
            token => ValidateAggregate<HeredocMarkerToken>(token, "<<C",
                t => ValidateSymbol(t, '<'),
                t => ValidateSymbol(t, '<'),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "C")),
            token => ValidateNewLine(token, "\n"),
            token => ValidateAggregate<HeredocBodyToken>(token, "body_a\nA\n",
                t => ValidateString(t, "body_a\n"),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "A"),
                t => ValidateNewLine(t, "\n")),
            token => ValidateAggregate<HeredocBodyToken>(token, "body_b\nB\n",
                t => ValidateString(t, "body_b\n"),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "B"),
                t => ValidateNewLine(t, "\n")),
            token => ValidateAggregate<HeredocBodyToken>(token, "body_c\nC\n",
                t => ValidateString(t, "body_c\n"),
                t => ValidateIdentifier<HeredocDelimiterToken>(t, "C"),
                t => ValidateNewLine(t, "\n")));
    }

    [Fact]
    public void Run_TwoHeredocs_ChildTokenTypesAndValues()
    {
        string text = "RUN <<FILE1 cat > /file1 && <<FILE2 cat > /file2\ncontent of file1\nFILE1\ncontent of file2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        var markers = result.HeredocMarkerTokens.ToList();
        var bodies = result.HeredocBodyTokens.ToList();
        Assert.Equal(2, markers.Count);
        Assert.Equal(2, bodies.Count);

        // Verify marker properties
        Assert.Equal("FILE1", markers[0].DelimiterName);
        Assert.False(markers[0].Chomp);
        Assert.False(markers[0].IsQuoted);

        Assert.Equal("FILE2", markers[1].DelimiterName);
        Assert.False(markers[1].Chomp);
        Assert.False(markers[1].IsQuoted);

        // Verify body content
        Assert.Equal("content of file1\n", bodies[0].Content);
        Assert.Equal("content of file2\n", bodies[1].Content);
    }

    [Fact]
    public void Run_MixedChompQuoted_TokenTreeStructure()
    {
        string text = "RUN <<-FILE1 <<\"FILE2\"\n\tcontent1\nFILE1\ncontent2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        Assert.Equal(text, result.ToString());

        var markers = result.HeredocMarkerTokens.ToList();
        var bodies = result.HeredocBodyTokens.ToList();

        // First marker: chomp, not quoted
        Assert.Equal("FILE1", markers[0].DelimiterName);
        Assert.True(markers[0].Chomp);
        Assert.False(markers[0].IsQuoted);

        // Second marker: not chomp, quoted
        Assert.Equal("FILE2", markers[1].DelimiterName);
        Assert.False(markers[1].Chomp);
        Assert.True(markers[1].IsQuoted);

        // Bodies
        Assert.Equal("\tcontent1\n", bodies[0].Content);
        Assert.Equal("content2\n", bodies[1].Content);
    }

    // ================================================================
    // SECTION: Heredoc semantic wrapper (Heredoc class) tests
    // ================================================================

    [Fact]
    public void Run_Heredocs_Semantic_SingleHeredoc()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        var heredocList = result.Heredocs;
        Assert.Single(heredocList);
        Assert.Equal("EOF", heredocList[0].Name);
        Assert.Equal("echo hello\n", heredocList[0].Content);
        Assert.False(heredocList[0].Chomp);
        Assert.True(heredocList[0].Expand);
    }

    [Fact]
    public void Run_Heredocs_Semantic_TwoHeredocs()
    {
        string text = "RUN <<FILE1 <<FILE2\ncontent1\nFILE1\ncontent2\nFILE2\n";
        RunInstruction result = RunInstruction.Parse(text);
        var heredocList = result.Heredocs;
        Assert.Equal(2, heredocList.Count);
        Assert.Equal("FILE1", heredocList[0].Name);
        Assert.Equal("content1\n", heredocList[0].Content);
        Assert.Equal("FILE2", heredocList[1].Name);
        Assert.Equal("content2\n", heredocList[1].Content);
    }

    [Fact]
    public void Run_Heredocs_Semantic_QuotedMarker_ExpandIsFalse()
    {
        string text = "RUN <<\"EOF\"\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        var heredocList = result.Heredocs;
        Assert.Single(heredocList);
        Assert.False(heredocList[0].Expand);
    }

    [Fact]
    public void Copy_Heredocs_Semantic_SingleHeredoc()
    {
        string text = "COPY <<EOF /app/file.txt\ncontent\nEOF\n";
        CopyInstruction result = CopyInstruction.Parse(text);
        var heredocList = result.Heredocs;
        Assert.Single(heredocList);
        Assert.Equal("EOF", heredocList[0].Name);
        Assert.Equal("content\n", heredocList[0].Content);
    }

    // ================================================================
    // SECTION: HeredocMarkerToken property tests
    // ================================================================

    [Fact]
    public void HeredocMarkerToken_Properties_Unquoted()
    {
        string text = "RUN <<EOF\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocMarkerToken marker = result.HeredocMarkerTokens.First();
        Assert.Equal("EOF", marker.DelimiterName);
        Assert.False(marker.Chomp);
        Assert.False(marker.IsQuoted);
        Assert.Null(marker.QuoteChar);
        Assert.True(marker.Expand);
    }

    [Fact]
    public void HeredocMarkerToken_Properties_Chomp()
    {
        string text = "RUN <<-EOF\n\techo hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocMarkerToken marker = result.HeredocMarkerTokens.First();
        Assert.Equal("EOF", marker.DelimiterName);
        Assert.True(marker.Chomp);
        Assert.False(marker.IsQuoted);
    }

    [Fact]
    public void HeredocMarkerToken_Properties_DoubleQuoted()
    {
        string text = "RUN <<\"EOF\"\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocMarkerToken marker = result.HeredocMarkerTokens.First();
        Assert.Equal("EOF", marker.DelimiterName);
        Assert.False(marker.Chomp);
        Assert.True(marker.IsQuoted);
        Assert.Equal('"', marker.QuoteChar);
        Assert.False(marker.Expand);
    }

    [Fact]
    public void HeredocMarkerToken_Properties_SingleQuoted()
    {
        string text = "RUN <<'EOF'\necho hello\nEOF\n";
        RunInstruction result = RunInstruction.Parse(text);
        HeredocMarkerToken marker = result.HeredocMarkerTokens.First();
        Assert.Equal("EOF", marker.DelimiterName);
        Assert.False(marker.Chomp);
        Assert.True(marker.IsQuoted);
        Assert.Equal('\'', marker.QuoteChar);
        Assert.False(marker.Expand);
    }

    [Fact]
    public void ExtractHeredocDelimiters_EmptyString_ReturnsEmpty()
    {
        var result = DockerfileParser.ExtractHeredocDelimiters("");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractHeredocDelimiters_NoHeredoc_ReturnsEmpty()
    {
        var result = DockerfileParser.ExtractHeredocDelimiters("RUN echo hello");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractHeredocDelimiters_SimpleHeredoc_ReturnsDelimiter()
    {
        var result = DockerfileParser.ExtractHeredocDelimiters("RUN <<EOF");
        Assert.Single(result);
        Assert.Equal("EOF", result[0].Delimiter);
        Assert.False(result[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_ChompHeredoc_ReturnsWithChomp()
    {
        var result = DockerfileParser.ExtractHeredocDelimiters("RUN <<-EOF");
        Assert.Single(result);
        Assert.Equal("EOF", result[0].Delimiter);
        Assert.True(result[0].HasChomp);
    }

    [Fact]
    public void ExtractHeredocDelimiters_MultipleHeredocs_ReturnsBoth()
    {
        var result = DockerfileParser.ExtractHeredocDelimiters("RUN <<FILE1 <<FILE2");
        Assert.Equal(2, result.Count);
        Assert.Equal("FILE1", result[0].Delimiter);
        Assert.Equal("FILE2", result[1].Delimiter);
    }

    [Fact]
    public void ExtractHeredocDelimiters_QuotedHeredoc_ReturnsDelimiter()
    {
        var result = DockerfileParser.ExtractHeredocDelimiters("RUN <<\"EOF\"");
        Assert.Single(result);
        Assert.Equal("EOF", result[0].Delimiter);
    }

    [Fact]
    public void ExtractHeredocDelimiters_HeredocInsideDoubleQuotes_NotRecognized()
    {
        // Inside double quotes, <<EOF should not be treated as heredoc
        var result = DockerfileParser.ExtractHeredocDelimiters("RUN echo \"<<EOF\"");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractHeredocDelimiters_HashAfterHeredocMarker_Stripped()
    {
        // Comment after heredoc marker should be stripped before detection
        // "RUN <<EOF #comment" — the #comment should be stripped, leaving "RUN <<EOF "
        var result = DockerfileParser.ExtractHeredocDelimiters("RUN <<EOF #comment");
        Assert.Single(result);
        Assert.Equal("EOF", result[0].Delimiter);
    }

    [Fact]
    public void ExtractHeredocDelimiters_HeredocInsideSingleQuotes_NotRecognized()
    {
        // Inside single quotes, <<EOF should not be treated as heredoc
        var result = DockerfileParser.ExtractHeredocDelimiters("RUN echo '<<EOF'");
        Assert.Empty(result);
    }

    [Fact]
    public void StripTrailingComment_EmptyString_ReturnsEmpty()
    {
        string result = DockerfileParser.StripTrailingComment("");
        Assert.Equal("", result);
    }

    [Fact]
    public void StripTrailingComment_HashAtStart_StripsEverything()
    {
        // # at position 0 is a comment — strip from there
        string result = DockerfileParser.StripTrailingComment("# this is a comment");
        // '#' preceded by nothing — index == 0, so char.IsWhiteSpace(line[i-1]) won't be checked
        // Based on the condition: i == 0 || char.IsWhiteSpace(line[i-1])
        // When i == 0, the short-circuit means '#' IS treated as comment start
        Assert.Equal("", result);
    }

    [Fact]
    public void StripTrailingComment_HashInMiddle_WithLeadingSpace_Strips()
    {
        // Space before # — treated as comment
        string result = DockerfileParser.StripTrailingComment("echo hello #this is a comment");
        Assert.Equal("echo hello ", result);
    }

    [Fact]
    public void StripTrailingComment_HashInMiddle_NoLeadingSpace_DoesNotStrip()
    {
        // No space before # — NOT treated as comment
        string result = DockerfileParser.StripTrailingComment("echo hello#notacomment");
        Assert.Equal("echo hello#notacomment", result);
    }

    [Fact]
    public void StripTrailingComment_HashInsideSingleQuotes_NotStripped()
    {
        string result = DockerfileParser.StripTrailingComment("echo '#notacomment'");
        Assert.Equal("echo '#notacomment'", result);
    }

    [Fact]
    public void StripTrailingComment_HashInsideDoubleQuotes_NotStripped()
    {
        string result = DockerfileParser.StripTrailingComment("echo \"#notacomment\"");
        Assert.Equal("echo \"#notacomment\"", result);
    }

    [Fact]
    public void StripTrailingComment_UnclosedSingleQuote_StillStripsHash()
    {
        // Unclosed single quote — quote flag stays set, hash not treated as comment
        // This is a tricky edge case
        string result = DockerfileParser.StripTrailingComment("echo 'hello #notacomment");
        // Inside single quotes (unclosed), so # is NOT a comment
        Assert.Equal("echo 'hello #notacomment", result);
    }

    [Fact]
    public void StripTrailingComment_EscapedHashInUnquotedContext_NotStripped()
    {
        // The escape character causes the next character to be skipped.
        // Here '\#' means the '#' is escaped and should not be treated as a comment.
        string result = DockerfileParser.StripTrailingComment("echo \\#notacomment");
        Assert.Equal("echo \\#notacomment", result);
    }

    [Fact]
    public void StripTrailingComment_EscapedHashWithSpaceBefore_NotStripped()
    {
        // A space precedes '\#', but the '#' is escaped so it should NOT be stripped.
        // This case documents the intended escaped-hash behavior without relying on a stale pre-fix note.
        string result = DockerfileParser.StripTrailingComment("echo \\#notacomment rest");
        Assert.Equal("echo \\#notacomment rest", result);
    }

    [Fact]
    public void StripTrailingComment_SpaceThenEscapedHash_NotStripped()
    {
        // "echo \#text" — the backslash escapes the hash, so no comment is detected.
        string result = DockerfileParser.StripTrailingComment("RUN echo \\#text <<EOF");
        Assert.Equal("RUN echo \\#text <<EOF", result);
    }

    [Fact]
    public void StripTrailingComment_EscapedBackslashThenSpaceHash_Stripped()
    {
        // "\\\\ " is two escaped backslashes (each backslash escapes the next),
        // leaving the space and '#' unescaped, so '#' IS a comment.
        string result = DockerfileParser.StripTrailingComment("echo \\\\\\\\ #comment");
        Assert.Equal("echo \\\\\\\\ ", result);
    }

    [Fact]
    public void StripTrailingComment_EscapeCharAtEndOfLine_NoError()
    {
        // Escape character at the very end of the line — no character to skip.
        string result = DockerfileParser.StripTrailingComment("echo test\\");
        Assert.Equal("echo test\\", result);
    }

    [Fact]
    public void StripTrailingComment_EscapedHashInsideSingleQuotes_NotAffected()
    {
        // Inside single quotes, the escape character is not special per shell semantics.
        // The hash inside single quotes is still protected by quoting, not escaping.
        string result = DockerfileParser.StripTrailingComment("echo '\\#text' rest");
        Assert.Equal("echo '\\#text' rest", result);
    }

    [Fact]
    public void StripTrailingComment_CustomEscapeChar_Backtick()
    {
        // With a custom escape character (backtick, used on Windows Dockerfiles),
        // "`#" should be treated as an escaped hash.
        string result = DockerfileParser.StripTrailingComment("echo `#notacomment rest", '`');
        Assert.Equal("echo `#notacomment rest", result);
    }

    [Fact]
    public void StripTrailingComment_CustomEscapeChar_BackslashNotSpecial()
    {
        // When escape char is backtick, backslash is NOT an escape character,
        // so "\ #comment" should still be stripped at the '#'.
        string result = DockerfileParser.StripTrailingComment("echo \\ #comment", '`');
        Assert.Equal("echo \\ ", result);
    }
}
