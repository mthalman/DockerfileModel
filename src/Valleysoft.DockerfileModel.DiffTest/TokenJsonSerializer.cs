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
///   - HEALTHCHECK CMD: C# nests CmdInstruction; Lean uses flat tokens
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

        if (token is HealthCheckInstruction healthCheck)
        {
            SerializeHealthCheck(sb, healthCheck);
            return;
        }

        if (token is ExposeInstruction expose)
        {
            SerializeExpose(sb, expose);
            return;
        }

        // RUN needs whitespace splitting + mount value flattening (issue #200)
        if (token is RunInstruction)
        {
            SerializeRunInstruction(sb, (RunInstruction)token);
            return;
        }

        // CMD, ENTRYPOINT need whitespace splitting
        if (token is CmdInstruction || token is EntrypointInstruction)
        {
            SerializeShellFormInstruction(sb, (Instruction)token);
            return;
        }

        // STOPSIGNAL: C# doesn't parse variable refs (issue #199)
        if (token is StopSignalInstruction)
        {
            SerializeStopSignal(sb, (StopSignalInstruction)token);
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
    // Shell form whitespace splitting
    // C# collapses shell form command text into a single StringToken inside
    // a LiteralToken ("echo hello"). Lean preserves whitespace as separate
    // WhitespaceToken children inside the LiteralToken.
    // Workaround: split StringToken children that contain whitespace.
    // ===================================================================

    /// <summary>
    /// Serialize a LiteralToken, splitting any StringToken children that contain
    /// whitespace into alternating string/whitespace runs (matching Lean behavior).
    /// Also flattens VariableRefTokens back to plain text — Lean's shell form parser
    /// treats $VAR as opaque text (matching BuildKit), while C# still decomposes it.
    /// Only applied to unquoted literals with embedded whitespace.
    /// </summary>
    private static void SerializeLiteralWithWhitespaceSplitting(StringBuilder sb, LiteralToken literal)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":");

        if (literal.QuoteChar.HasValue)
        {
            sb.Append('"');
            JsonEscapeString(sb, literal.QuoteChar.Value.ToString());
            sb.Append('"');
        }
        else
        {
            sb.Append("null");
        }

        sb.Append(",\"children\":[");

        bool first = true;
        foreach (Token child in literal.Tokens)
        {
            if (literal.QuoteChar.HasValue)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, child);
                first = false;
            }
            // Flatten VariableRefTokens to plain text (Lean treats $VAR as opaque text
            // in shell form commands, matching BuildKit behavior)
            else if (child is VariableRefToken varRef)
            {
                SplitStringByWhitespace(sb, varRef.ToString(), ref first);
            }
            // Split StringTokens that contain whitespace (spaces/tabs)
            else if (child is StringToken strTok && ContainsWhitespace(strTok.Value))
            {
                SplitStringByWhitespace(sb, strTok.Value, ref first);
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

    private static bool ContainsWhitespace(string s) =>
        s.IndexOfAny(new[] { ' ', '\t' }) >= 0;

    /// <summary>
    /// Split a string value into alternating string/whitespace primitive tokens.
    /// </summary>
    private static void SplitStringByWhitespace(StringBuilder sb, string text, ref bool first)
    {
        int i = 0;
        while (i < text.Length)
        {
            bool isWs = text[i] == ' ' || text[i] == '\t';
            int start = i;
            while (i < text.Length)
            {
                bool curIsWs = text[i] == ' ' || text[i] == '\t';
                if (curIsWs != isWs) break;
                i++;
            }

            string segment = text[start..i];

            if (!first) sb.Append(',');
            first = false;

            if (isWs)
            {
                SerializePrimitive(sb, "whitespace", segment);
            }
            else
            {
                SerializePrimitive(sb, "string", segment);
            }
        }
    }

    // ===================================================================
    // Shell form instructions (RUN, CMD, ENTRYPOINT)
    // These need whitespace splitting inside their shell form LiteralTokens.
    // The Command wrapper (ShellFormCommand) is transparent, so the LiteralToken
    // appears after inlining.
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
                    // Apply whitespace splitting to LiteralTokens inside the command
                    if (cmdChild is LiteralToken lit)
                    {
                        SerializeLiteralWithWhitespaceSplitting(sb, lit);
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
    // Workaround: HEALTHCHECK CMD nesting
    // C# nests CmdInstruction as a child Instruction inside HealthCheckInstruction.
    // Lean keeps everything flat: flags, KeywordToken("CMD"), whitespace, command tokens.
    // We detect the nested CmdInstruction and inline its children, also applying
    // shell form whitespace splitting.
    // ===================================================================

    private static void SerializeHealthCheck(StringBuilder sb, HealthCheckInstruction healthCheck)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"instruction\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        foreach (Token child in healthCheck.Tokens)
        {
            // When we hit the nested CmdInstruction, inline its children
            if (child is CmdInstruction cmdInst)
            {
                foreach (Token cmdChild in cmdInst.Tokens)
                {
                    // Handle Command wrapper (ShellFormCommand is transparent)
                    if (cmdChild is Command cmd)
                    {
                        foreach (Token cmdGrandchild in cmd.Tokens)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            // Apply whitespace splitting to LiteralTokens
                            if (cmdGrandchild is LiteralToken lit)
                            {
                                SerializeLiteralWithWhitespaceSplitting(sb, lit);
                            }
                            else
                            {
                                SerializeToken(sb, cmdGrandchild);
                            }
                        }
                    }
                    else
                    {
                        if (!first) sb.Append(',');
                        SerializeToken(sb, cmdChild);
                        first = false;
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
    // Workaround: ONBUILD inner instruction
    // C# recursively parses the inner instruction as a full Instruction node.
    // Lean treats the trigger text as an opaque LiteralToken containing
    // string and whitespace primitives.
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
                // matching Lean's format: LiteralToken containing string/whitespace primitives
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
    /// string, whitespace, and lineContinuation tokens, matching Lean's opaque
    /// text representation for ONBUILD triggers.
    /// </summary>
    private static void SerializeInstructionAsLiteral(StringBuilder sb, Instruction instruction)
    {
        // Get the full text of the instruction
        string text = instruction.ToString();

        // Build the literal token with string, whitespace, and lineContinuation children,
        // matching Lean's shell form parser output
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":null,\"children\":[");

        bool firstChild = true;
        SplitByWhitespaceAndLineContinuation(sb, text, ref firstChild);

        sb.Append("]}");
    }

    /// <summary>
    /// Split a string value into alternating string/whitespace/lineContinuation tokens.
    /// Handles both backslash and backtick escape chars before newlines.
    /// </summary>
    private static void SplitByWhitespaceAndLineContinuation(StringBuilder sb, string text, ref bool first)
    {
        int i = 0;
        while (i < text.Length)
        {
            // Check for line continuation: escape char + newline
            if ((text[i] == '\\' || text[i] == '`') && i + 1 < text.Length)
            {
                char escChar = text[i];
                // \n or \r\n following the escape char
                if (text[i + 1] == '\n')
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"type\":\"aggregate\",\"kind\":\"lineContinuation\",\"quoteChar\":null,\"children\":[");
                    SerializePrimitive(sb, "symbol", escChar.ToString());
                    sb.Append(',');
                    SerializePrimitive(sb, "newLine", "\n");
                    sb.Append("]}");
                    i += 2;
                    continue;
                }
                else if (text[i + 1] == '\r' && i + 2 < text.Length && text[i + 2] == '\n')
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"type\":\"aggregate\",\"kind\":\"lineContinuation\",\"quoteChar\":null,\"children\":[");
                    SerializePrimitive(sb, "symbol", escChar.ToString());
                    sb.Append(',');
                    SerializePrimitive(sb, "newLine", "\r\n");
                    sb.Append("]}");
                    i += 3;
                    continue;
                }
            }

            // Check for whitespace (spaces and tabs)
            if (text[i] == ' ' || text[i] == '\t')
            {
                int start = i;
                while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
                    i++;

                if (!first) sb.Append(',');
                first = false;
                SerializePrimitive(sb, "whitespace", text[start..i]);
                continue;
            }

            // Regular text: collect until next whitespace, line continuation, or end
            {
                int start = i;
                while (i < text.Length)
                {
                    if (text[i] == ' ' || text[i] == '\t')
                        break;
                    // Check for escape char + newline
                    if ((text[i] == '\\' || text[i] == '`') && i + 1 < text.Length &&
                        (text[i + 1] == '\n' || text[i + 1] == '\r'))
                        break;
                    i++;
                }

                if (i > start)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    SerializePrimitive(sb, "string", text[start..i]);
                }
            }
        }
    }

    // ===================================================================
    // Workaround: RUN instruction — mount value flattening (see issue #200)
    // C# over-parses mount flag values into structured KeyValueToken children
    // (type=secret, id=x, etc.), but Lean (and BuildKit) treat the mount
    // value as an opaque literal string. This serializer flattens the Mount
    // aggregate back to a single LiteralToken containing the opaque text.
    // Also applies shell form whitespace splitting (same as CMD/ENTRYPOINT).
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
                    // Apply whitespace splitting to LiteralTokens inside the command
                    if (cmdChild is LiteralToken lit)
                    {
                        SerializeLiteralWithWhitespaceSplitting(sb, lit);
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

    // ===================================================================
    // Workaround: STOPSIGNAL variable ref parsing (see issue #199)
    // C# doesn't parse variable refs ($VAR, ${VAR}) in STOPSIGNAL — it
    // treats them as plain StringToken text inside a LiteralToken. Lean
    // DOES parse them as variableRef tokens (since STOPSIGNAL supports
    // variable substitution per Docker docs). This serializer scans
    // StringToken values for variable ref patterns and emits the Lean-
    // style variableRef aggregate tokens.
    // ===================================================================

    private static void SerializeStopSignal(StringBuilder sb, StopSignalInstruction instruction)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"instruction\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        foreach (Token child in instruction.Tokens)
        {
            if (child is LiteralToken literal)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeLiteralWithVariableRefParsing(sb, literal);
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
    /// Serialize a LiteralToken, parsing $VAR and ${VAR} patterns out of StringToken
    /// children and emitting them as variableRef aggregate tokens (matching Lean behavior).
    /// </summary>
    private static void SerializeLiteralWithVariableRefParsing(StringBuilder sb, LiteralToken literal)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":");

        if (literal.QuoteChar.HasValue)
        {
            sb.Append('"');
            JsonEscapeString(sb, literal.QuoteChar.Value.ToString());
            sb.Append('"');
        }
        else
        {
            sb.Append("null");
        }

        sb.Append(",\"children\":[");

        bool first = true;
        foreach (Token child in literal.Tokens)
        {
            if (child is StringToken strTok)
            {
                EmitStringWithVariableRefs(sb, strTok.Value, ref first);
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
    /// Scan a string value for $VAR and ${VAR...} patterns and emit
    /// alternating string primitives and variableRef aggregates.
    /// Supports all modifier types: default-value (:-, :+, :?, -, +, ?)
    /// and POSIX pattern (##, #, %%, %, //, /).
    /// Lean's variableRef structure:
    ///   Simple $VAR -> variableRef [ string("VAR") ]
    ///   Braced ${VAR} -> variableRef [ symbol("{"), string("VAR"), symbol("}") ]
    ///   Modified ${VAR:-value} -> variableRef [ symbol("{"), string("VAR"),
    ///     symbol(":"), symbol("-"), literal [ string("value") ], symbol("}") ]
    ///   POSIX ${VAR##pattern} -> variableRef [ symbol("{"), string("VAR"),
    ///     symbol("#"), symbol("#"), literal [ string("pattern") ], symbol("}") ]
    /// </summary>
    private static void EmitStringWithVariableRefs(StringBuilder sb, string text, ref bool first)
    {
        int i = 0;
        while (i < text.Length)
        {
            // Look for $ that starts a variable reference
            if (text[i] == '$' && i + 1 < text.Length)
            {
                char next = text[i + 1];

                // Braced variable reference: ${...}
                if (next == '{')
                {
                    // Find the closing brace
                    int braceStart = i + 2;
                    int braceEnd = text.IndexOf('}', braceStart);
                    if (braceEnd > braceStart)
                    {
                        // Extract the content between braces
                        string braceContent = text[braceStart..braceEnd];

                        // Parse variable name (alphanumeric + underscore)
                        int nameEnd = 0;
                        while (nameEnd < braceContent.Length &&
                               (char.IsLetterOrDigit(braceContent[nameEnd]) || braceContent[nameEnd] == '_'))
                        {
                            nameEnd++;
                        }

                        if (nameEnd > 0)
                        {
                            string varName = braceContent[..nameEnd];
                            string remainder = braceContent[nameEnd..];

                            if (!first) sb.Append(',');
                            first = false;

                            // Emit variableRef aggregate
                            sb.Append("{\"type\":\"aggregate\",\"kind\":\"variableRef\",\"quoteChar\":null,\"children\":[");
                            SerializePrimitive(sb, "symbol", "{");
                            sb.Append(',');
                            SerializePrimitive(sb, "string", varName);

                            // If there's a modifier (e.g., :-, :+, :?, -, +, ?, ##, #, %%, %, //, /)
                            if (remainder.Length > 0)
                            {
                                // Emit each modifier character as a symbol
                                int modEnd = 0;
                                while (modEnd < remainder.Length &&
                                       (remainder[modEnd] == ':' || remainder[modEnd] == '-' ||
                                        remainder[modEnd] == '+' || remainder[modEnd] == '?' ||
                                        remainder[modEnd] == '#' || remainder[modEnd] == '%' ||
                                        remainder[modEnd] == '/'))
                                {
                                    sb.Append(',');
                                    SerializePrimitive(sb, "symbol", remainder[modEnd].ToString());
                                    modEnd++;
                                }

                                // Everything after the modifier chars is the modifier value (e.g., default, pattern, replacement)
                                if (modEnd < remainder.Length)
                                {
                                    string modValue = remainder[modEnd..];
                                    sb.Append(',');
                                    // Wrap in a literal token (matching Lean behavior)
                                    sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":null,\"children\":[");
                                    SerializePrimitive(sb, "string", modValue);
                                    sb.Append("]}");
                                }
                            }

                            sb.Append(',');
                            SerializePrimitive(sb, "symbol", "}");
                            sb.Append("]}");

                            i = braceEnd + 1;
                            continue;
                        }
                    }
                }
                // Simple variable reference: $IDENTIFIER
                else if (char.IsLetterOrDigit(next) || next == '_')
                {
                    // Collect the identifier
                    int nameStart = i + 1;
                    int nameEnd = nameStart;
                    while (nameEnd < text.Length &&
                           (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] == '_'))
                    {
                        nameEnd++;
                    }

                    string varName = text[nameStart..nameEnd];

                    if (!first) sb.Append(',');
                    first = false;

                    // Emit variableRef aggregate with just the name
                    sb.Append("{\"type\":\"aggregate\",\"kind\":\"variableRef\",\"quoteChar\":null,\"children\":[");
                    SerializePrimitive(sb, "string", varName);
                    sb.Append("]}");

                    i = nameEnd;
                    continue;
                }
            }

            // Regular text: collect until next $ or end
            int textStart = i;
            while (i < text.Length)
            {
                if (text[i] == '$' && i + 1 < text.Length &&
                    (char.IsLetterOrDigit(text[i + 1]) || text[i + 1] == '_' || text[i + 1] == '{'))
                {
                    break;
                }
                i++;
            }

            if (i > textStart)
            {
                string segment = text[textStart..i];
                if (!first) sb.Append(',');
                first = false;
                SerializePrimitive(sb, "string", segment);
            }
        }
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
