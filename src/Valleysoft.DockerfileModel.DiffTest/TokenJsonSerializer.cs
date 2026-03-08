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
///   - BooleanFlag: C# AggregateToken with no kind mapping; Lean uses keyValue
///   - Shell form whitespace: C# collapses to single StringToken; Lean splits
///   - EXPOSE port/protocol: C# uses flat tokens; Lean wraps in keyValue
///   - ONBUILD trigger: C# recursively parses; Lean uses opaque LiteralToken
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
        if (token is OnBuildInstruction onBuild)
        {
            SerializeOnBuild(sb, onBuild);
            return;
        }

        if (token is ExposeInstruction expose)
        {
            SerializeExpose(sb, expose);
            return;
        }

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

        if (token is LiteralToken)
        {
            SerializeAggregate(sb, "literal", token);
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

        // BooleanFlag: C# has BooleanFlag as AggregateToken but Lean treats it as keyValue.
        // Must check before generic KeyValueToken and AggregateToken checks.
        if (token is BooleanFlag)
        {
            SerializeAggregate(sb, "keyValue", token);
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

        bool first = true;
        foreach (Token child in instruction.Tokens)
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
    // Workaround: EXPOSE instruction
    // C# has flat tokens (literal, symbol('/'), literal) for port/protocol.
    // Lean wraps port/protocol in a KeyValueToken.
    // We detect the flat pattern and re-wrap during serialization.
    // ===================================================================

    private static void SerializeExpose(StringBuilder sb, ExposeInstruction expose)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"instruction\",\"quoteChar\":null,\"children\":[");

        List<Token> tokens = expose.Tokens.ToList();
        bool first = true;

        for (int i = 0; i < tokens.Count; i++)
        {
            Token child = tokens[i];

            // Detect the port/protocol pattern: LiteralToken, SymbolToken('/'), LiteralToken
            if (child is LiteralToken
                && i + 1 < tokens.Count && tokens[i + 1] is SymbolToken slashSym && slashSym.Value == "/"
                && i + 2 < tokens.Count && tokens[i + 2] is LiteralToken)
            {
                if (!first) sb.Append(',');
                first = false;

                // Wrap the three tokens as a keyValue
                sb.Append("{\"type\":\"aggregate\",\"kind\":\"keyValue\",\"quoteChar\":null,\"children\":[");
                SerializeToken(sb, tokens[i]);     // port literal
                sb.Append(',');
                SerializeToken(sb, tokens[i + 1]); // slash symbol
                sb.Append(',');
                SerializeToken(sb, tokens[i + 2]); // protocol literal
                sb.Append("]}");

                i += 2; // skip the next two tokens, already consumed
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
    // Workaround: ONBUILD inner instruction
    // C# recursively parses the inner instruction as a full Instruction node.
    // Lean treats the trigger text as an opaque LiteralToken containing a
    // single opaque StringToken (plus LineContinuationTokens if any).
    // We detect the inner Instruction and convert it to a LiteralToken.
    // ===================================================================

    private static void SerializeOnBuild(StringBuilder sb, OnBuildInstruction onBuild)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"instruction\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        foreach (Token child in onBuild.Tokens)
        {
            if (child is Instruction innerInst)
            {
                // Convert the inner instruction to an opaque literal token
                // matching Lean's format: LiteralToken containing a single opaque string
                if (!first) sb.Append(',');
                first = false;
                SerializeInstructionAsLiteral(sb, innerInst);
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
    /// Convert an Instruction token tree to a flat LiteralToken containing
    /// a single opaque StringToken (plus LineContinuationTokens if any),
    /// matching Lean's opaque text representation for ONBUILD triggers.
    /// </summary>
    private static void SerializeInstructionAsLiteral(StringBuilder sb, Instruction instruction)
    {
        // Get the full text of the instruction
        string text = instruction.ToString();

        // Build the literal token with a single opaque StringToken (plus any
        // LineContinuationTokens), matching Lean's shell form parser output.
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":null,\"children\":[");

        bool firstChild = true;
        EmitOpaqueStringWithLineContinuations(sb, text, ref firstChild);

        sb.Append("]}");
    }

    /// <summary>
    /// Emit a string as a single opaque StringToken, except for line
    /// continuations (escape char + optional whitespace + newline) which
    /// become LineContinuationTokens.
    /// Handles both backslash and backtick escape chars before newlines.
    /// </summary>
    private static void EmitOpaqueStringWithLineContinuations(StringBuilder sb, string text, ref bool first)
    {
        int i = 0;
        StringBuilder pending = new();

        while (i < text.Length)
        {
            // Check for line continuation: escape char + optional whitespace + newline
            if ((text[i] == '\\' || text[i] == '`') && i + 1 < text.Length)
            {
                char escChar = text[i];
                // Scan past optional whitespace (spaces/tabs) after escape char
                int j = i + 1;
                while (j < text.Length && (text[j] == ' ' || text[j] == '\t'))
                    j++;

                string trailingWs = text.Substring(i + 1, j - (i + 1));
                string? newLine = null;
                int consumed = 0;

                if (j < text.Length && text[j] == '\n')
                {
                    newLine = "\n";
                    consumed = j + 1 - i;
                }
                else if (j + 1 < text.Length && text[j] == '\r' && text[j + 1] == '\n')
                {
                    newLine = "\r\n";
                    consumed = j + 2 - i;
                }

                if (newLine != null)
                {
                    // Flush pending string
                    if (pending.Length > 0)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        SerializePrimitive(sb, "string", pending.ToString());
                        pending.Clear();
                    }

                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"type\":\"aggregate\",\"kind\":\"lineContinuation\",\"quoteChar\":null,\"children\":[");
                    SerializePrimitive(sb, "symbol", escChar.ToString());
                    if (trailingWs.Length > 0)
                    {
                        sb.Append(',');
                        SerializePrimitive(sb, "whitespace", trailingWs);
                    }
                    sb.Append(',');
                    SerializePrimitive(sb, "newLine", newLine);
                    sb.Append("]}");
                    i += consumed;
                    continue;
                }
            }

            // Regular character: accumulate into pending string
            pending.Append(text[i]);
            i++;
        }

        // Flush any remaining pending text
        if (pending.Length > 0)
        {
            if (!first) sb.Append(',');
            first = false;
            SerializePrimitive(sb, "string", pending.ToString());
        }
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

        bool first = true;
        foreach (Token child in instruction.Tokens)
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
    /// </summary>
    private static void SerializeMountFlag(StringBuilder sb, MountFlag mountFlag)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"keyValue\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        foreach (Token child in mountFlag.Tokens)
        {
            if (child is Mount mount)
            {
                // Flatten the Mount aggregate to an opaque literal
                if (!first) sb.Append(',');
                first = false;
                string mountText = mount.ToString();
                sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":null,\"children\":[");
                SerializePrimitive(sb, "string", mountText);
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
