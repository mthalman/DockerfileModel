using System.Text.Encodings.Web;
using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class WorkdirInstructionTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<WorkdirInstruction> scenario) =>
        TestHelper.RunParseTest(scenario, WorkdirInstruction.Parse);

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        WorkdirInstruction result = new(scenario.Path);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Path()
    {
        WorkdirInstruction result = new("/test");
        Assert.Equal("/test", result.Path);
        Assert.Equal("/test", result.PathToken.Value);
        Assert.Equal("WORKDIR /test", result.ToString());

        result.Path = "/test2";
        Assert.Equal("/test2", result.Path);
        Assert.Equal("/test2", result.PathToken.Value);
        Assert.Equal("WORKDIR /test2", result.ToString());

        result.PathToken.Value = "/test3";
        Assert.Equal("/test3", result.Path);
        Assert.Equal("/test3", result.PathToken.Value);
        Assert.Equal("WORKDIR /test3", result.ToString());

        result.PathToken = new LiteralToken("/test4");
        Assert.Equal("/test4", result.Path);
        Assert.Equal("/test4", result.PathToken.Value);
        Assert.Equal("WORKDIR /test4", result.ToString());

        Assert.Throws<ArgumentNullException>(() => result.Path = null);
        Assert.Throws<ArgumentException>(() => result.Path = "");
        Assert.Throws<ArgumentNullException>(() => result.PathToken = null);
    }

    [Fact]
    public void PathWithVariables()
    {
        WorkdirInstruction result = new("$var");
        TestHelper.TestVariablesWithLiteral(() => result.PathToken, "var", canContainVariables: true);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ParseTestScenario<WorkdirInstruction>[] testInputs = new ParseTestScenario<WorkdirInstruction>[]
        {
            new ParseTestScenario<WorkdirInstruction>
            {
                Text = "WORKDIR /test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "WORKDIR"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/test")
                },
                Validate = result =>
                {
                    Assert.Empty(result.Comments);
                    Assert.Equal("WORKDIR", result.InstructionName);
                    Assert.Equal("/test", result.Path);
                }
            },
            new ParseTestScenario<WorkdirInstruction>
            {
                Text = "WORKDIR $TEST",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "WORKDIR"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "$TEST",
                        token => ValidateAggregate<VariableRefToken>(token, "$TEST",
                            token => ValidateString(token, "TEST")))
                }
            },
            new ParseTestScenario<WorkdirInstruction>
            {
                Text = "WORKDIR`\n /test",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "WORKDIR"),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/test")
                }
            },
            // Variable reference with path default value — slash must not be split into a separate symbol token
            new ParseTestScenario<WorkdirInstruction>
            {
                Text = "WORKDIR ${BASE:-/opt}/${APP:-myapp}",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "WORKDIR"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateAggregate<LiteralToken>(token, "${BASE:-/opt}/${APP:-myapp}",
                        token => ValidateAggregate<VariableRefToken>(token, "${BASE:-/opt}",
                            token => ValidateSymbol(token, '{'),
                            token => ValidateString(token, "BASE"),
                            token => ValidateSymbol(token, ':'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateLiteral(token, "/opt"),
                            token => ValidateSymbol(token, '}')),
                        token => ValidateString(token, "/"),
                        token => ValidateAggregate<VariableRefToken>(token, "${APP:-myapp}",
                            token => ValidateSymbol(token, '{'),
                            token => ValidateString(token, "APP"),
                            token => ValidateSymbol(token, ':'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateLiteral(token, "myapp"),
                            token => ValidateSymbol(token, '}')))
                },
                Validate = result =>
                {
                    Assert.Equal("${BASE:-/opt}/${APP:-myapp}", result.Path);
                    Assert.Equal("WORKDIR ${BASE:-/opt}/${APP:-myapp}", result.ToString());
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
                Path = "/test",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateKeyword(token, "WORKDIR"),
                    token => ValidateWhitespace(token, " "),
                    token => ValidateLiteral(token, "/test")
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class CreateTestScenario : TestScenario<WorkdirInstruction>
    {
        public string Path { get; set; }
    }

    [Fact]
    public void WorkdirInstruction_Simple_RoundTrips()
    {
        string text = "WORKDIR /app\n";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
        Assert.Equal("/app", inst.Path);
    }

    [Fact]
    public void WorkdirInstruction_WithVariable_RoundTrips()
    {
        string text = "WORKDIR $APP_HOME\n";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
        Assert.Equal("$APP_HOME", inst.Path);
    }

    [Fact]
    public void VariableRef_ColonDash_WithPath_RoundTrips()
    {
        // ${var:-/default/path} — default value containing a slash
        string text = "WORKDIR ${dir:-/usr/local}\n";
        var inst = WorkdirInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
    }

    /// <summary>
    /// Bug: WorkdirInstruction.Path includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/282
    /// </summary>
    [Fact]
    public void WorkdirInstruction_Path_InspectsTokenStructure()
    {
        // Parse with trailing newline
        string text = "WORKDIR /app\n";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);

        // Verify round-trip is fine
        Assert.Equal(text, inst.ToString());

        // Inspect what tokens make up the PathToken
        LiteralToken pathToken = inst.PathToken;
        string pathValue = pathToken.Value;

        // Document what we see: does Path include the trailing newline?
        // If this is a bug, it should show /app\n, not /app
        System.Console.WriteLine($"PathToken.Value = [{pathValue}] (len={pathValue.Length})");

        // Print all children of the PathToken
        foreach (var child in pathToken.Tokens)
        {
            System.Console.WriteLine($"  Child type={child.GetType().Name} value=[{child}]");
        }

        // Also print all tokens of the instruction
        System.Console.WriteLine("Instruction tokens:");
        foreach (var tok in inst.Tokens)
        {
            System.Console.WriteLine($"  Token type={tok.GetType().Name} value=[{tok}]");
        }
    }

    /// <summary>
    /// Bug: WorkdirInstruction.Path includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/282
    /// </summary>
    [Fact]
    public void WorkdirInstruction_Path_DoesNotIncludeNewline()
    {
        // This test verifies the EXPECTED behavior: Path should NOT include the newline
        string text = "WORKDIR /app\n";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        // THIS IS THE BUG: the Path property returns "/app\n" instead of "/app"
        // We confirm the bug by checking what it actually returns
        string path = inst.Path;
        Assert.Equal("/app", path);  // This will FAIL if bug exists
    }

    [Fact]
    public void WorkdirInstruction_Path_NoTrailingNewline_ReturnsCorrectly()
    {
        // Without trailing newline in input, does Path work correctly?
        string text = "WORKDIR /app";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        string path = inst.Path;
        Assert.Equal("/app", path);
    }

    [Fact]
    public void WorkdirInstruction_Path_WithoutNewlineInInput()
    {
        // Parse without trailing newline — Path should be just the path
        string text = "WORKDIR /app";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        Assert.Equal(text, inst.ToString());
        // Does Path work correctly when no newline is in input?
        Assert.Equal("/app", inst.Path);
    }

    /// <summary>
    /// Bug: WorkdirInstruction.Path includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/282
    /// </summary>
    [Fact]
    public void WorkdirInstruction_Path_IncludesNewlineWhenInputHasNewline()
    {
        // This test documents the BUG: Path includes '\n' when input text has trailing newline
        string text = "WORKDIR /app\n";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        // Check what the LiteralToken's children are
        var tokens = inst.PathToken.Tokens.ToList();
        foreach (var t in tokens)
        {
            System.Console.WriteLine($"PathToken child: type={t.GetType().Name} val=[{System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(t.ToString())}]");
        }
        // The Value property should exclude newlines but currently doesn't
        // because NewLineToken is not in the exclusion list in TokenStringOptions
        string path = inst.Path;
        // The test EXPECTS the bug: path == "/app\n"
        // When the bug is fixed, path should be "/app"
        // For now just record what we see
        System.Console.WriteLine($"inst.Path = [{path}]");
    }

    [Fact]
    public void WorkdirInstruction_Path_NoNewlineInInput_Works()
    {
        string text = "WORKDIR /app";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        Assert.Equal("/app", inst.Path);
    }

    /// <summary>
    /// Bug: WorkdirInstruction.Path includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/282
    /// </summary>
    [Fact]
    public void WorkdirInstruction_Path_WithNewlineInInput_IncludesNewline_BUG()
    {
        // BUG: Path includes trailing newline when input ends with \n
        string text = "WORKDIR /app\n";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        // This currently returns "/app\n" — should return "/app"
        string path = inst.Path;
        // Document the bug:
        Assert.Contains("\n", path); // Bug confirmed: newline is in Path value
    }

    /// <summary>
    /// Bug: WorkdirInstruction.Path includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/282
    /// </summary>
    [Fact]
    public void WorkdirInstruction_Path_WithCRLFInInput_IncludesCRLF_BUG()
    {
        // Same bug but with CRLF line ending
        string text = "WORKDIR /app\r\n";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        string path = inst.Path;
        // Bug: contains \r\n or \n
        Assert.True(path.Contains('\n') || path.Contains('\r'),
            $"Expected newline in path but got: [{path}]");
    }

    /// <summary>
    /// Bug: WorkdirInstruction.Path includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/282
    /// </summary>
    [Fact]
    public void WorkdirInstruction_Path_WithVariable_WithNewline_IncludesNewline_BUG()
    {
        string text = "WORKDIR $APP_HOME\n";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        string path = inst.Path;
        // BUG: path contains \n
        Assert.Contains("\n", path);
    }

    /// <summary>
    /// Bug: WorkdirInstruction.Path includes trailing newline character
    /// See https://github.com/mthalman/DockerfileModel/issues/282
    /// </summary>
    [Fact]
    public void WorkdirInstruction_RoundTrip_WorksButPathPropertyBroken()
    {
        string text = "WORKDIR /app\n";
        WorkdirInstruction inst = WorkdirInstruction.Parse(text);
        // Round-trip is fine
        Assert.Equal(text, inst.ToString());
        // But Path includes the trailing newline — that's the bug
        // A caller doing `var path = inst.Path; File.Exists(path)` would get incorrect results
        Assert.NotEqual("/app", inst.Path); // BUG: not equal because of trailing \n
        Assert.Equal("/app\n", inst.Path);  // Confirm exact bug behavior
    }
}
