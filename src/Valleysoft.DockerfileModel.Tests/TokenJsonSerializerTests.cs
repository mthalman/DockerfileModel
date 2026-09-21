using System.Text.Json;
using Valleysoft.DockerfileModel.TestSupport;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Tests for the differential test serializer (TokenJsonSerializer).
/// Verifies that C# token trees serialize to the canonical JSON format
/// matching Lean's output.
/// </summary>
public class TokenJsonSerializerTests
{
    [Fact]
    public void ShellForm_FinalNewline_RemainsInLiteral()
    {
        string json = InstructionSerializer.ParseCSharp("RUN", "RUN echo hello\n", '\\');
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement[] children = document.RootElement.GetProperty("children").EnumerateArray().ToArray();
        JsonElement literal = children.Single(child => child.GetProperty("kind").GetString() == "literal");

        JsonElement[] literalChildren = literal.GetProperty("children").EnumerateArray().ToArray();
        Assert.Equal("newLine", literalChildren.Last().GetProperty("kind").GetString());
        Assert.NotEqual("newLine", children.Last().GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData("ENV name=\"a\\\"b\"\n", "\"", "a\\\"b", true)]
    [InlineData("ENV name=\"a\\'b\"\n", "\"", "a\\'b", true)]
    [InlineData("ENV name='a\\\"b'\n", "'", "a\\\"b", true)]
    [InlineData("ENV name='a\\'b''\n", "'", "a\\'b", false)]
    public void Env_EscapedQuoteValue_SerializesAsQuotedLiteral(string input, string expectedQuoteChar, string expectedValue, bool expectTrailingNewline)
    {
        string json = InstructionSerializer.ParseCSharp("ENV", input, '\\');
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement keyValue = document.RootElement.GetProperty("children").EnumerateArray()
            .Single(child => child.GetProperty("kind").GetString() == "keyValue");
        JsonElement value = keyValue.GetProperty("children").EnumerateArray().Last();
        JsonElement stringToken = Assert.Single(value.GetProperty("children").EnumerateArray());

        Assert.Equal(expectedQuoteChar, value.GetProperty("quoteChar").GetString());
        Assert.Equal(expectedValue, stringToken.GetProperty("value").GetString());
        Assert.Equal(
            expectTrailingNewline ? "newLine" : "keyValue",
            document.RootElement.GetProperty("children").EnumerateArray().Last().GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData("ENV first=\"a\\\" b\" second=baz\n", "\"", "a\\\" b")]
    [InlineData("ENV first='a\\' b' second=baz\n", "'", "a\\' b")]
    public void Env_EscapedQuoteValueWithSpace_PreservesFollowingAssignments(string input, string expectedQuoteChar, string expectedValue)
    {
        string json = InstructionSerializer.ParseCSharp("ENV", input, '\\');
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement[] keyValues = document.RootElement.GetProperty("children").EnumerateArray()
            .Where(child => child.GetProperty("kind").GetString() == "keyValue")
            .ToArray();

        Assert.Equal(2, keyValues.Length);
        JsonElement firstValue = keyValues[0].GetProperty("children").EnumerateArray().Last();
        JsonElement secondValue = keyValues[1].GetProperty("children").EnumerateArray().Last();

        Assert.Equal(expectedQuoteChar, firstValue.GetProperty("quoteChar").GetString());
        Assert.Equal(expectedValue, Assert.Single(firstValue.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
        Assert.Equal("baz", Assert.Single(secondValue.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
    }

    [Fact]
    public void Copy_UnknownFlag_SerializesAsOpaqueLiteral()
    {
        string json = InstructionSerializer.ParseCSharp("COPY", "COPY --doit=true foo /tmp/\n", '\\');
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement flag = document.RootElement.GetProperty("children").EnumerateArray()
            .First(child => child.GetProperty("kind").GetString() is "literal");

        Assert.Equal("--doit=true", Assert.Single(flag.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
    }

    [Fact]
    public void Copy_LeanRecognizedUnsupportedFlag_StillSerializesAsKeyValue()
    {
        string json = InstructionSerializer.ParseCSharp("COPY", "COPY --parents foo /tmp/\n", '\\');
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement flag = document.RootElement.GetProperty("children").EnumerateArray()
            .First(child => child.GetProperty("kind").GetString() is "keyValue");

        JsonElement keyword = flag.GetProperty("children").EnumerateArray()
            .Single(child => child.ValueKind == JsonValueKind.Object && child.GetProperty("kind").GetString() == "keyword");
        Assert.Equal("parents", Assert.Single(keyword.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("COPY", "COPY --parents=true foo /tmp/\n", "parents", "true")]
    [InlineData("COPY", "COPY --parents=FALSE foo /tmp/\n", "parents", "FALSE")]
    [InlineData("COPY", "COPY --PARENTS=FALSE foo /tmp/\n", "PARENTS", "FALSE")]
    [InlineData("ADD", "ADD --unpack=true src.tar /tmp/\n", "unpack", "true")]
    [InlineData("ADD", "ADD --unpack=FALSE src.tar /tmp/\n", "unpack", "FALSE")]
    [InlineData("ADD", "ADD --UNPACK=FALSE src.tar /tmp/\n", "UNPACK", "FALSE")]
    public void FileTransfer_LeanRecognizedBooleanFlagValues_SerializeAsKeyValue(
        string instruction, string input, string expectedName, string expectedValue)
    {
        string json = InstructionSerializer.ParseCSharp(instruction, input, '\\');
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement flag = document.RootElement.GetProperty("children").EnumerateArray()
            .First(child => child.GetProperty("kind").GetString() is "keyValue");

        JsonElement[] children = flag.GetProperty("children").EnumerateArray().ToArray();
        JsonElement keyword = children.Single(child => child.ValueKind == JsonValueKind.Object && child.GetProperty("kind").GetString() == "keyword");
        JsonElement value = children.Last();

        Assert.Equal(expectedName, Assert.Single(keyword.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
        Assert.Equal(expectedValue, Assert.Single(value.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("COPY", "COPY --parents=unexpected foo /tmp/\n", "--parents=unexpected")]
    [InlineData("COPY", "COPY --parents= foo /tmp/\n", "--parents=")]
    [InlineData("ADD", "ADD --unpack=unexpected src.tar /tmp/\n", "--unpack=unexpected")]
    [InlineData("ADD", "ADD --unpack= src.tar /tmp/\n", "--unpack=")]
    public void FileTransfer_InvalidBooleanFlagValues_SerializeAsOpaqueLiteral(
        string instruction, string input, string expectedValue)
    {
        string json = InstructionSerializer.ParseCSharp(instruction, input, '\\');
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement flag = document.RootElement.GetProperty("children").EnumerateArray()
            .First(child => child.GetProperty("kind").GetString() is "literal");

        Assert.Equal(expectedValue, Assert.Single(flag.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
    }

    [Fact]
    public void Copy_LeanRecognizedValueFlagWithVariable_SerializesAsKeyValue()
    {
        string json = InstructionSerializer.ParseCSharp("COPY", "COPY --exclude=$PATTERN foo /tmp/\n", '\\');
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement flag = document.RootElement.GetProperty("children").EnumerateArray()
            .First(child => child.GetProperty("kind").GetString() is "keyValue");

        JsonElement value = flag.GetProperty("children").EnumerateArray().Last();
        JsonElement variableRef = Assert.Single(value.GetProperty("children").EnumerateArray());

        Assert.Equal("variableRef", variableRef.GetProperty("kind").GetString());
        Assert.Equal("PATTERN", Assert.Single(variableRef.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
    }

    [Fact]
    public void Add_LeanRecognizedValueFlagNameWithDifferentCasing_SerializesAsKeyValue()
    {
        string json = InstructionSerializer.ParseCSharp("ADD", "ADD --EXCLUDE=*.txt foo /tmp/\n", '\\');
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement flag = document.RootElement.GetProperty("children").EnumerateArray()
            .First(child => child.GetProperty("kind").GetString() is "keyValue");

        JsonElement keyword = flag.GetProperty("children").EnumerateArray()
            .Single(child => child.ValueKind == JsonValueKind.Object && child.GetProperty("kind").GetString() == "keyword");

        Assert.Equal("EXCLUDE", Assert.Single(keyword.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("ENV name=\"a\\\\b\"\n", "\"")]
    [InlineData("ENV name=\"a\\$VALUE\"\n", "\"")]
    public void Env_OrdinaryQuotedValues_DoNotSerializeAsRawLiteral(string input, string expectedQuoteChar)
    {
        string json = InstructionSerializer.ParseCSharp("ENV", input, '\\');
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement keyValue = document.RootElement.GetProperty("children").EnumerateArray()
            .Single(child => child.GetProperty("kind").GetString() == "keyValue");
        JsonElement value = keyValue.GetProperty("children").EnumerateArray().Last();

        Assert.Equal(expectedQuoteChar, value.GetProperty("quoteChar").GetString());
    }

    [Fact]
    public void Env_EscapedQuoteValueMutatedAfterParse_SerializesAsCurrentLiteralShape()
    {
        EnvInstruction instruction = EnvInstruction.Parse("ENV name=\"a\\\"b\"");
        instruction.Variables[0].Value = "plain";

        string json = TokenJsonSerializer.Serialize(instruction);
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement keyValue = document.RootElement.GetProperty("children").EnumerateArray()
            .Single(child => child.GetProperty("kind").GetString() == "keyValue");
        JsonElement value = keyValue.GetProperty("children").EnumerateArray().Last();

        Assert.Equal("\"", value.GetProperty("quoteChar").GetString());
        Assert.Equal("plain", Assert.Single(value.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("type=bind,from=build,target=/src", false)]
    [InlineData("from=build,type=bind,target=/src", false)]
    [InlineData("from=build,target=/src", false)]
    [InlineData("type=bind,from=build,target=/src", true)]
    [InlineData("from=build,type=bind,target=/src", true)]
    [InlineData("from=build,target=/src", true)]
    public void MountTypeEntries_SerializeAsOpaqueValues(string spec, bool onBuild)
    {
        string prefix = onBuild ? "ONBUILD " : "";
        string json = InstructionSerializer.ParseCSharp(
            onBuild ? "ONBUILD" : "RUN", $"{prefix}RUN --mount={spec} echo hello", '\\');
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement instruction = document.RootElement;
        if (onBuild)
        {
            instruction = instruction.GetProperty("children").EnumerateArray()
                .Single(child => child.GetProperty("kind").GetString() == "instruction");
        }

        JsonElement mountFlag = instruction.GetProperty("children").EnumerateArray()
            .Single(child => child.GetProperty("kind").GetString() == "keyValue");
        JsonElement mountValue = mountFlag.GetProperty("children").EnumerateArray().Last();
        Assert.Equal("literal", mountValue.GetProperty("kind").GetString());
        JsonElement value = Assert.Single(mountValue.GetProperty("children").EnumerateArray());
        Assert.Equal("string", value.GetProperty("kind").GetString());
        Assert.Equal(spec, value.GetProperty("value").GetString());

        JsonElement command = instruction.GetProperty("children").EnumerateArray()
            .Single(child => child.GetProperty("kind").GetString() == "literal");
        Assert.Equal("echo hello",
            Assert.Single(command.GetProperty("children").EnumerateArray()).GetProperty("value").GetString());
    }

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
        string json = InstructionSerializer.ParseCSharp("RUN", input, '\\');

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
        string json = InstructionSerializer.ParseCSharp("CMD", input, '\\');

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
        string json = InstructionSerializer.ParseCSharp("ENTRYPOINT", input, '\\');

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
        string json = InstructionSerializer.ParseCSharp("RUN", input, '\\');

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
        string json = InstructionSerializer.ParseCSharp("RUN", input, '\\');

        // Mount flag should be serialized as keyValue
        Assert.Contains("\"kind\":\"keyValue\"", json);

        // The shell form literal should have correct whitespace handling
        Assert.Contains("\"value\":\"echo hello \"", json);
        Assert.Contains("\"kind\":\"lineContinuation\"", json);
        Assert.Contains("\"value\":\"world\"", json);
    }

    /// <summary>
    /// Extracts the JSON for the shell-form literal token's children from the full instruction JSON.
    /// Uses <see cref="JsonDocument"/> to structurally traverse the token tree rather than
    /// ad-hoc string slicing, making the search correct even when the instruction contains
    /// '['/']' characters in values or multiple literal tokens (e.g., flags).
    ///
    /// Traversal: instruction.children -> find first aggregate with kind="literal" at the
    /// top level of the instruction's children (not nested inside a keyValue or other aggregate).
    /// Returns the serialized children array of that literal as a JSON string, or the original
    /// json if no literal node is found.
    /// </summary>
    private static string GetLiteralChildrenJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // The root element is the instruction aggregate.
        // Walk its children array looking for the first direct aggregate with kind="literal".
        if (!root.TryGetProperty("children", out JsonElement children))
            return json;

        foreach (JsonElement child in children.EnumerateArray())
        {
            if (!child.TryGetProperty("kind", out JsonElement kindProp))
                continue;

            if (kindProp.GetString() == "literal")
            {
                // Found the shell-form literal. Return its children array serialized as JSON.
                if (child.TryGetProperty("children", out JsonElement literalChildren))
                    return literalChildren.GetRawText();

                return json;
            }
        }

        return json;
    }
}
