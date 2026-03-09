using Valleysoft.DockerfileModel.DiffTest;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Tests for the differential test serializer (TokenJsonSerializer).
/// Verifies that C# token trees serialize to the canonical JSON format
/// matching Lean's output.
/// </summary>
public class TokenJsonSerializerTests
{
    /// <summary>
    /// Shell form commands with whitespace before a line continuation must NOT
    /// split the trailing whitespace into a separate whitespace token. Instead,
    /// the whitespace is part of the preceding string token value (matching
    /// Lean's shellFormCommand parser which consumes maximal character runs
    /// including spaces).
    ///
    /// Input: "RUN echo hello \\\nworld"
    /// Correct: string["echo hello "], lineContinuation, string["world"]
    /// Wrong:   string["echo"], ws[" "], string["hello"], ws[" "], lineContinuation, string["world"]
    /// </summary>
    [Fact]
    public void ShellForm_WhitespaceBeforeLineContinuation_NotSplitIntoSeparateToken()
    {
        // "RUN echo hello \\\nworld" — backslash + newline is a line continuation
        string input = "RUN echo hello \\" + "\n" + "world";
        string json = DiffTestRunner.ParseCSharp("RUN", input, '\\');

        // The literal children should contain:
        //   string("echo hello "), lineContinuation[symbol("\\"), newLine("\n")], string("world")
        // NOT:
        //   string("echo"), whitespace(" "), string("hello"), whitespace(" "), lineContinuation[...], string("world")

        // Verify no whitespace token appears inside the literal
        Assert.DoesNotContain("\"kind\":\"whitespace\"", GetLiteralChildrenJson(json));

        // Verify the trailing space is embedded in the string value before the line continuation
        Assert.Contains("\"value\":\"echo hello \"", json);

        // Verify the line continuation token is present
        Assert.Contains("\"kind\":\"lineContinuation\"", json);

        // Verify the continuation text after the line continuation
        Assert.Contains("\"value\":\"world\"", json);
    }

    /// <summary>
    /// Shell form commands with multiple words and a line continuation in the
    /// middle. Verifies no spurious whitespace tokens are emitted.
    ///
    /// Input: "CMD foo bar \\\nbaz"
    /// </summary>
    [Fact]
    public void ShellForm_CMD_WhitespaceBeforeLineContinuation()
    {
        string input = "CMD foo bar \\" + "\n" + "baz";
        string json = DiffTestRunner.ParseCSharp("CMD", input, '\\');

        // The literal children should NOT contain separate whitespace tokens
        Assert.DoesNotContain("\"kind\":\"whitespace\"", GetLiteralChildrenJson(json));

        // The text "foo bar " should be a single string with trailing space
        Assert.Contains("\"value\":\"foo bar \"", json);
    }

    /// <summary>
    /// Shell form commands with multiple line continuations.
    ///
    /// Input: "ENTRYPOINT a b \\\nc d \\\ne"
    /// </summary>
    [Fact]
    public void ShellForm_ENTRYPOINT_MultipleLineContinuations()
    {
        string input = "ENTRYPOINT a b \\" + "\n" + "c d \\" + "\n" + "e";
        string json = DiffTestRunner.ParseCSharp("ENTRYPOINT", input, '\\');

        // No whitespace tokens inside the literal
        Assert.DoesNotContain("\"kind\":\"whitespace\"", GetLiteralChildrenJson(json));

        // First segment includes trailing space
        Assert.Contains("\"value\":\"a b \"", json);

        // Middle segment includes trailing space
        Assert.Contains("\"value\":\"c d \"", json);
    }

    /// <summary>
    /// Shell form command without any line continuation — simple case.
    /// Whitespace between words should be collapsed into the string value.
    /// </summary>
    [Fact]
    public void ShellForm_NoLineContinuation_WhitespaceCollapsed()
    {
        string input = "RUN echo hello world";
        string json = DiffTestRunner.ParseCSharp("RUN", input, '\\');

        // The literal should contain a single string with all text
        Assert.Contains("\"value\":\"echo hello world\"", json);
    }

    /// <summary>
    /// RUN instruction with mount flag and shell form with line continuation.
    /// Verifies the mount workaround and shell form coexist correctly.
    /// </summary>
    [Fact]
    public void RUN_MountAndShellFormWithLineContinuation()
    {
        string input = "RUN --mount=type=secret,id=mysecret echo hello \\" + "\n" + "world";
        string json = DiffTestRunner.ParseCSharp("RUN", input, '\\');

        // Mount flag should be serialized as keyValue
        Assert.Contains("\"kind\":\"keyValue\"", json);

        // The shell form literal should have correct whitespace handling
        Assert.Contains("\"value\":\"echo hello \"", json);
        Assert.Contains("\"kind\":\"lineContinuation\"", json);
        Assert.Contains("\"value\":\"world\"", json);
    }

    /// <summary>
    /// Extracts the JSON for the literal token's children from the full instruction JSON.
    /// Used to check that no whitespace tokens appear inside the shell form literal.
    /// </summary>
    private static string GetLiteralChildrenJson(string json)
    {
        // Find the literal token and extract its children portion
        // The literal appears as: {"type":"aggregate","kind":"literal","quoteChar":null,"children":[...]}
        int literalIdx = json.IndexOf("\"kind\":\"literal\"");
        if (literalIdx < 0) return json;

        int childrenIdx = json.IndexOf("\"children\":[", literalIdx);
        if (childrenIdx < 0) return json;

        int start = childrenIdx + "\"children\":[".Length;
        // Find the matching closing bracket
        int depth = 1;
        int end = start;
        while (end < json.Length && depth > 0)
        {
            if (json[end] == '[') depth++;
            else if (json[end] == ']') depth--;
            end++;
        }

        return json.Substring(start, end - start - 1);
    }
}
