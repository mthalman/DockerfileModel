using System.Text;
using Valleysoft.DockerfileModel;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.DiffTest;

/// <summary>
/// Hand-written recursive JSON serializer for the C# Token hierarchy.
/// Produces compact, deterministic JSON matching the canonical format
/// used by the Lean differential test harness.
///
/// Type-checking order is critical due to inheritance:
///   - NewLineToken before WhitespaceToken (NewLineToken : WhitespaceToken)
///   - Instruction before DockerfileConstruct (Instruction : DockerfileConstruct)
///   - Concrete types before abstract base types
///
/// Transparent wrappers (Command):
///   C# uses OOP wrapper classes that extend AggregateToken but have no
///   corresponding token kind in the Lean spec. These wrappers are "transparent"
///   — their children are inlined into the parent's children array during
///   serialization, as if the wrapper did not exist.
///
/// Workarounds for known tokenization differences (C# vs Lean):
///   Where C# and Lean genuinely tokenize differently, this serializer applies
///   workarounds to suppress the diff so the test harness can detect NEW bugs
///   rather than re-reporting known issues. Each workaround is tagged with the
///   GitHub issue tracking the underlying C# fix.
///
/// Known differences with workarounds:
///   - COPY/ADD unrecognized flags (issues #238, #239, #240, #241): C# does not recognize
///     --parents, --exclude (COPY), --unpack, --exclude (ADD) as named flags, so it treats
///     them as opaque literal file-path tokens. Lean recognizes them and emits keyValue tokens.
///     Workaround converts literal["--flagname[=value]"] → keyValue[-, -, keyword["flagname"],
///     optionally =, literal["value"]] when the literal starts with "--".
///   - #264 (trailing whitespace on instructions): FIXED. Both C# and Lean now emit trailing
///     instruction whitespace as a standalone WhitespaceToken sibling at instruction level.
///     (The Lean argTokens fix removed the guard that prevented capturing trailing whitespace
///     without a line continuation; C# already emitted it the same way.)
///   - #266 (flag line continuation): FIXED. Lean now parses line continuations inside
///     flag values as structured keyValue tokens, matching C#'s behavior.
/// </summary>
public static class TokenJsonSerializer
{
    public static string Serialize(Token token)
    {
        StringBuilder sb = new();
        SerializeToken(sb, token);
        return sb.ToString();
    }

    /// <summary>
    /// Check whether a token is a "transparent wrapper" — a C# OOP type that
    /// has no corresponding kind in the Lean token model. When encountered as
    /// a child, its own children are inlined into the parent's children list.
    /// </summary>
    private static bool IsTransparentWrapper(Token token) =>
        token is Command;       // ShellFormCommand, ExecFormCommand

    private static void SerializeToken(StringBuilder sb, Token token)
    {
        // Primitives — check most-specific first
        // NewLineToken : WhitespaceToken : PrimitiveToken
        if (token is NewLineToken newLine)
        {
            SerializePrimitive(sb, "newLine", newLine.Value);
            return;
        }

        if (token is WhitespaceToken ws)
        {
            SerializePrimitive(sb, "whitespace", ws.Value);
            return;
        }

        if (token is SymbolToken sym)
        {
            SerializePrimitive(sb, "symbol", sym.Value);
            return;
        }

        if (token is StringToken str)
        {
            SerializePrimitive(sb, "string", str.Value);
            return;
        }

        // Aggregates — check most-specific first
        // Instruction : DockerfileConstruct : AggregateToken

        // Special instruction handlers for workarounds

        // RUN needs mount value flattening (issue #200) + shell form VariableRefToken validation (fail-fast)
        if (token is RunInstruction)
        {
            SerializeRunInstruction(sb, (RunInstruction)token);
            return;
        }

        // CMD, ENTRYPOINT, HEALTHCHECK need shell form VariableRefToken validation (fail-fast)
        if (token is CmdInstruction || token is EntrypointInstruction || token is HealthCheckInstruction)
        {
            SerializeShellFormInstruction(sb, (Instruction)token);
            return;
        }

        // COPY and ADD: normalize unrecognized flags (#238, #239, #240, #241) and
        // boolean flags with explicit =true/=false (#246)
        if (token is CopyInstruction || token is AddInstruction)
        {
            SerializeCopyOrAddInstruction(sb, (Instruction)token);
            return;
        }

        if (token is Instruction)
        {
            SerializeAggregate(sb, "instruction", token);
            return;
        }

        if (token is DockerfileConstruct)
        {
            SerializeAggregate(sb, "construct", token);
            return;
        }

        if (token is KeywordToken)
        {
            SerializeAggregate(sb, "keyword", token);
            return;
        }

        if (token is VariableRefToken)
        {
            SerializeAggregate(sb, "variableRef", token);
            return;
        }

        if (token is LiteralToken literal)
        {
            SerializeAggregate(sb, "literal", literal);
            return;
        }

        if (token is CommentToken)
        {
            SerializeAggregate(sb, "comment", token);
            return;
        }

        if (token is LineContinuationToken)
        {
            SerializeAggregate(sb, "lineContinuation", token);
            return;
        }

        // IdentifierToken is abstract — StageName, Variable extend it
        if (token is IdentifierToken)
        {
            SerializeAggregate(sb, "identifier", token);
            return;
        }

        // KeyValueToken<,> is generic — check via base-type walk
        if (IsKeyValueToken(token))
        {
            SerializeAggregate(sb, "keyValue", token);
            return;
        }

        // Fallback for any other AggregateToken subtype (e.g., ArgDeclaration)
        if (token is AggregateToken)
        {
            // ArgDeclaration implements IKeyValuePair and extends AggregateToken
            // It functions as a key-value pair in the token tree
            if (token is IKeyValuePair)
            {
                SerializeAggregate(sb, "keyValue", token);
                return;
            }

            // Unknown aggregate — shouldn't happen in well-formed trees
            SerializeAggregate(sb, "construct", token);
            return;
        }

        // Fallback for unknown primitive types
        if (token is PrimitiveToken prim)
        {
            SerializePrimitive(sb, "string", prim.Value);
            return;
        }

        throw new InvalidOperationException($"Unknown token type: {token.GetType().FullName}");
    }

    private static void SerializePrimitive(StringBuilder sb, string kind, string value)
    {
        sb.Append("{\"type\":\"primitive\",\"kind\":\"");
        sb.Append(kind);
        sb.Append("\",\"value\":\"");
        JsonEscapeString(sb, value);
        sb.Append("\"}");
    }

    private static void SerializeAggregate(StringBuilder sb, string kind, Token token)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"");
        sb.Append(kind);
        sb.Append("\",\"quoteChar\":");

        // Check for IQuotableToken — LiteralToken and IdentifierToken implement it
        if (token is IQuotableToken quotable && quotable.QuoteChar.HasValue)
        {
            sb.Append('"');
            JsonEscapeString(sb, quotable.QuoteChar.Value.ToString());
            sb.Append('"');
        }
        else
        {
            sb.Append("null");
        }

        sb.Append(",\"children\":[");

        AggregateToken aggregate = (AggregateToken)token;
        bool first = true;
        foreach (Token child in aggregate.Tokens)
        {
            EmitChild(sb, child, ref first);
        }

        sb.Append("]}");
    }

    /// <summary>
    /// Emit a single child token, handling transparent wrappers.
    /// </summary>
    private static void EmitChild(StringBuilder sb, Token child, ref bool first)
    {
        // Transparent wrappers: inline their children into parent
        if (IsTransparentWrapper(child))
        {
            foreach (Token grandchild in ((AggregateToken)child).Tokens)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, grandchild);
                first = false;
            }
        }
        else
        {
            if (!first) sb.Append(',');
            SerializeToken(sb, child);
            first = false;
        }
    }

    // ===================================================================
    // Shell form literal serialization
    // Shell form commands are parsed as opaque text without variable
    // expansion — $VAR is treated as a regular character sequence inside
    // a StringToken, not decomposed into a VariableRefToken. If a
    // VariableRefToken is ever encountered here, it indicates a parsing
    // regression that should be investigated.
    // ===================================================================

    /// <summary>
    /// Serialize a shell form LiteralToken. Shell form commands should never
    /// contain VariableRefToken children; encountering one is a fail-fast error.
    /// </summary>
    private static void SerializeShellFormLiteral(StringBuilder sb, LiteralToken literal)
    {
        // Fail fast if a VariableRefToken is encountered — shell form commands
        // are parsed as opaque text and should never produce variable ref nodes.
        foreach (Token child in literal.Tokens)
        {
            if (child is VariableRefToken)
            {
                throw new InvalidOperationException(
                    "Unexpected VariableRefToken in shell form LiteralToken. " +
                    "Shell form commands should be parsed as opaque text without variable expansion.");
            }
        }

        SerializeAggregate(sb, "literal", literal);
    }

    // ===================================================================
    // Shell form instructions (CMD, ENTRYPOINT, HEALTHCHECK)
    // Shell form commands are parsed as opaque text. The Command wrapper
    // (ShellFormCommand) is transparent, so the LiteralToken appears
    // after inlining. Validates shell form LiteralTokens (fail-fast on VariableRefToken).
    // ===================================================================

    private static void SerializeShellFormInstruction(StringBuilder sb, Instruction instruction)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"instruction\",\"quoteChar\":null,\"children\":[");

        List<Token> tokens = instruction.Tokens.ToList();
        bool first = true;
        foreach (Token child in tokens)
        {
            // ShellFormCommand is a transparent wrapper — inline its children
            if (child is Command cmd)
            {
                foreach (Token cmdChild in cmd.Tokens)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    // Validate shell form LiteralTokens (fail-fast on VariableRefToken)
                    if (cmdChild is LiteralToken lit)
                    {
                        SerializeShellFormLiteral(sb, lit);
                    }
                    else
                    {
                        SerializeToken(sb, cmdChild);
                    }
                }
            }
            else
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, child);
                first = false;
            }
        }

        sb.Append("]}");
    }

    // ===================================================================
    // Workaround: COPY/ADD unrecognized flags and boolean flag explicit values
    //
    // Issue #238: COPY --parents — C# treats it as literal["--parents"]; Lean: keyValue
    // Issue #239: COPY --exclude=... — C# treats it as literal["--exclude=..."]; Lean: keyValue
    // Issue #240: ADD --unpack — C# treats it as literal["--unpack"]; Lean: keyValue
    // Issue #241: ADD --exclude=... — C# treats it as literal["--exclude=..."]; Lean: keyValue
    // Strategy:
    //   1. Scan instruction tokens with a look-ahead of 1.
    //   2. A literal whose StringToken value starts with "--" is an unrecognized flag literal:
    //      parse "flagname" (and optional "=flagvalue") from the text and emit as keyValue.
    // ===================================================================

    private static void SerializeCopyOrAddInstruction(StringBuilder sb, Instruction instruction)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"instruction\",\"quoteChar\":null,\"children\":[");

        List<Token> tokens = instruction.Tokens.ToList();
        bool first = true;

        for (int i = 0; i < tokens.Count; i++)
        {
            Token child = tokens[i];

            // Workaround #238/#239/#240/#241: LiteralToken whose value starts with "--" is
            // an unrecognized flag. Emit as keyValue[-, -, keyword["name"], optionally =, literal["value"]].
            if (child is LiteralToken flagLit && IsUnrecognizedFlagLiteral(flagLit, out string? flagName, out string? flagValue))
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeUnrecognizedFlagAsKeyValue(sb, flagName!, flagValue);
                continue;
            }

            if (!first) sb.Append(',');
            SerializeToken(sb, child);
            first = false;
        }

        sb.Append("]}");
    }

    /// <summary>
    /// Returns the concatenated string value of all StringToken children of a LiteralToken.
    /// </summary>
    private static string GetLiteralText(LiteralToken literal)
    {
        var sb = new StringBuilder();
        foreach (Token child in literal.Tokens)
        {
            if (child is StringToken str)
                sb.Append(str.Value);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns true if the literal token holds an unrecognized flag text starting with "--".
    /// Parses the flag name and optional value from the literal text.
    /// For example: "--parents" → flagName="parents", flagValue=null
    ///              "--exclude=*.txt" → flagName="exclude", flagValue="*.txt"
    /// </summary>
    private static bool IsUnrecognizedFlagLiteral(LiteralToken literal, out string? flagName, out string? flagValue)
    {
        string text = GetLiteralText(literal);
        if (text.StartsWith("--") && text.Length > 2)
        {
            string nameAndValue = text.Substring(2); // strip "--"
            int eqIdx = nameAndValue.IndexOf('=');
            if (eqIdx >= 0)
            {
                flagName = nameAndValue.Substring(0, eqIdx);
                flagValue = nameAndValue.Substring(eqIdx + 1);
            }
            else
            {
                flagName = nameAndValue;
                flagValue = null;
            }
            return true;
        }
        flagName = null;
        flagValue = null;
        return false;
    }

    /// <summary>
    /// Serialize an unrecognized flag (originally a LiteralToken with text "--flagname[=value]")
    /// as a keyValue token matching Lean's structure.
    /// Without value: keyValue[symbol[-], symbol[-], keyword["flagname"]]
    /// With value:    keyValue[symbol[-], symbol[-], keyword["flagname"], symbol[=], literal[string["value"]]]
    /// </summary>
    private static void SerializeUnrecognizedFlagAsKeyValue(StringBuilder sb, string flagName, string? flagValue)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"keyValue\",\"quoteChar\":null,\"children\":[");

        // symbol["-"]
        SerializePrimitive(sb, "symbol", "-");
        sb.Append(',');
        // symbol["-"]
        SerializePrimitive(sb, "symbol", "-");
        sb.Append(',');
        // keyword["flagname"]
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"keyword\",\"quoteChar\":null,\"children\":[");
        SerializePrimitive(sb, "string", flagName);
        sb.Append("]}");

        if (flagValue is not null)
        {
            // symbol["="]
            sb.Append(',');
            SerializePrimitive(sb, "symbol", "=");
            sb.Append(',');
            // literal[string["value"]]
            sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":null,\"children\":[");
            SerializePrimitive(sb, "string", flagValue);
            sb.Append("]}");
        }

        sb.Append("]}");
    }

    // ===================================================================
    // Workaround: RUN instruction — mount value flattening (see issue #200)
    // C# over-parses mount flag values into structured KeyValueToken children
    // (type=secret, id=x, etc.), but Lean (and BuildKit) treat the mount
    // value as an opaque literal string. This serializer flattens the Mount
    // aggregate back to a single LiteralToken containing the opaque text.
    // Also validates shell-form LiteralTokens and fails fast on VariableRefToken (same as CMD/ENTRYPOINT).
    // ===================================================================

    private static void SerializeRunInstruction(StringBuilder sb, RunInstruction instruction)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"instruction\",\"quoteChar\":null,\"children\":[");

        List<Token> tokens = instruction.Tokens.ToList();
        bool first = true;
        foreach (Token child in tokens)
        {
            // MountFlag is a KeyValueToken<KeywordToken, Mount>.
            // Its Mount value child is an AggregateToken with structured children
            // that Lean treats as opaque text. Flatten the mount value.
            if (child is MountFlag mountFlag)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeMountFlag(sb, mountFlag);
            }
            // ShellFormCommand is a transparent wrapper — inline its children
            else if (child is Command cmd)
            {
                foreach (Token cmdChild in cmd.Tokens)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    // Validate shell form LiteralTokens (fail-fast on VariableRefToken)
                    if (cmdChild is LiteralToken lit)
                    {
                        SerializeShellFormLiteral(sb, lit);
                    }
                    else
                    {
                        SerializeToken(sb, cmdChild);
                    }
                }
            }
            else
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, child);
                first = false;
            }
        }

        sb.Append("]}");
    }

    /// <summary>
    /// Serialize a MountFlag, flattening its Mount value to an opaque LiteralToken.
    /// C# structure: keyValue [ --, --, keyword("mount"), =, Mount [ keyValue(type=...), comma, keyValue(id=...), ... ] ]
    /// Lean structure: keyValue [ --, --, keyword("mount"), =, literal("type=secret,id=mysecret,...") ]
    /// The parser (excludeTrailingWhitespace: true) ensures the mount value never includes
    /// trailing whitespace. Any whitespace that follows the mount flag at instruction level
    /// (e.g., the space between "--mount=type=ssh" and the command) is a separate WhitespaceToken
    /// and is serialized normally by the caller.
    /// </summary>
    private static void SerializeMountFlag(StringBuilder sb, MountFlag mountFlag)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"keyValue\",\"quoteChar\":null,\"children\":[");

        bool first = true;

        foreach (Token child in mountFlag.Tokens)
        {
            if (child is Mount mount)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":null,\"children\":[");
                SerializePrimitive(sb, "string", mount.ToString());
                sb.Append("]}");
            }
            else
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, child);
                first = false;
            }
        }

        sb.Append("]}");
    }

    /// <summary>
    /// JSON-standard string escaping: \\ \" \n \r \t and control chars &lt; 0x20 as \uXXXX.
    /// </summary>
    private static void JsonEscapeString(StringBuilder sb, string s)
    {
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Check if a token is a KeyValueToken&lt;,&gt; by walking the base types
    /// looking for a generic type definition match.
    /// </summary>
    private static bool IsKeyValueToken(Token token)
    {
        Type? type = token.GetType();
        while (type != null)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValueToken<,>))
            {
                return true;
            }
            type = type.BaseType;
        }
        return false;
    }
}
