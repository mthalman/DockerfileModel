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
///   - Shell form whitespace: C# collapses to single StringToken; Lean splits
///   - LABEL keys: C# uses LiteralToken; Lean uses IdentifierToken
///   - EXPOSE port/protocol: C# splits into literal+symbol+literal; Lean uses one flat literal.
///     Workaround merges the three tokens back into a single literal during serialization.
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
    // Workaround: EXPOSE port/protocol merging
    // C# tokenizes port/protocol specs as separate tokens at the instruction
    // level: LiteralToken(port) + SymbolToken('/') + LiteralToken(protocol).
    // BuildKit (and hence Lean) treats the entire port/protocol spec as a
    // single opaque literal value, e.g., "80/tcp" becomes one LiteralToken
    // containing StringToken("80/tcp"). This serializer detects the
    // literal + '/' + literal pattern and merges them into a single literal,
    // appending "/" and the protocol text into the port literal's children.
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
            // with optional LineContinuationTokens between them (e.g., "80\\\n/tcp").
            if (child is LiteralToken portLiteral)
            {
                // Scan forward past any LineContinuationTokens to find the slash
                int slashIdx = i + 1;
                while (slashIdx < tokens.Count && tokens[slashIdx] is LineContinuationToken)
                    slashIdx++;

                if (slashIdx < tokens.Count
                    && tokens[slashIdx] is SymbolToken slashSym && slashSym.Value == "/")
                {
                    // Scan forward past any LineContinuationTokens to find the protocol literal
                    int protoIdx = slashIdx + 1;
                    while (protoIdx < tokens.Count && tokens[protoIdx] is LineContinuationToken)
                        protoIdx++;

                    if (protoIdx < tokens.Count && tokens[protoIdx] is LiteralToken protoLiteral)
                    {
                        if (!first) sb.Append(',');
                        first = false;

                        // Collect LineContinuationTokens separately for before
                        // the slash (port→slash) and after the slash (slash→protocol)
                        // so their relative position is preserved in the merged literal.
                        var preSlashLCs = new List<LineContinuationToken>();
                        for (int k = i + 1; k < slashIdx; k++)
                        {
                            if (tokens[k] is LineContinuationToken lc)
                                preSlashLCs.Add(lc);
                        }
                        var postSlashLCs = new List<LineContinuationToken>();
                        for (int k = slashIdx + 1; k < protoIdx; k++)
                        {
                            if (tokens[k] is LineContinuationToken lc)
                                postSlashLCs.Add(lc);
                        }

                        // Merge the port/slash/protocol tokens into a single literal,
                        // preserving relative positions of LineContinuationTokens
                        SerializeMergedPortProtocolLiteral(sb, portLiteral, protoLiteral, preSlashLCs, postSlashLCs);

                        i = protoIdx; // skip all consumed tokens (including line continuations)
                        continue;
                    }
                }
            }

            if (!first) sb.Append(',');
            SerializeToken(sb, child);
            first = false;
        }

        sb.Append("]}");
    }

    /// <summary>
    /// Merge a port LiteralToken and protocol LiteralToken (separated by '/') into
    /// a single literal, matching Lean's flat representation.
    ///
    /// The merge behavior for the boundary tokens depends on their types:
    /// - Both strings: merge into one (e.g., string["80"] + "/" + string["tcp"] -> string["80/tcp"])
    /// - Port ends with string, proto starts with non-string (e.g., variable): append "/" to port string
    /// - Port ends with non-string, proto starts with string: prepend "/" to proto string
    /// - Neither is string: add a separate "/" string node between them
    ///
    /// LineContinuationTokens are tracked separately for before vs after the slash
    /// to preserve their relative position. Lean's literalStringWithoutSpaces parses
    /// char-by-char: "80/\\\ntcp" -> string["80/"], LC, string["tcp"] while
    /// "80\\\n/tcp" -> string["80"], LC, string["/tcp"]. The slash is part of
    /// whichever string segment it is adjacent to, and LCs stay in their original
    /// position relative to it.
    /// </summary>
    private static void SerializeMergedPortProtocolLiteral(
        StringBuilder sb, LiteralToken portLiteral, LiteralToken protoLiteral,
        List<LineContinuationToken> preSlashLCs, List<LineContinuationToken> postSlashLCs)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":");

        if (portLiteral.QuoteChar.HasValue)
        {
            sb.Append('"');
            JsonEscapeString(sb, portLiteral.QuoteChar.Value.ToString());
            sb.Append('"');
        }
        else
        {
            sb.Append("null");
        }

        sb.Append(",\"children\":[");

        List<Token> portChildren = portLiteral.Tokens.ToList();
        List<Token> protoChildren = protoLiteral.Tokens.ToList();

        bool first = true;
        bool hasPreSlashLCs = preSlashLCs.Count > 0;
        bool hasPostSlashLCs = postSlashLCs.Count > 0;
        bool hasLineContinuations = hasPreSlashLCs || hasPostSlashLCs;

        // Check if we can merge the last port child with the first proto child
        bool lastPortIsString = portChildren.Count > 0 && portChildren[^1] is StringToken;
        bool firstProtoIsString = protoChildren.Count > 0 && protoChildren[0] is StringToken;

        if (!hasLineContinuations && lastPortIsString && firstProtoIsString)
        {
            // No line continuations: merge lastPortString + "/" + firstProtoString
            for (int j = 0; j < portChildren.Count - 1; j++)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, portChildren[j]);
                first = false;
            }

            string mergedValue = ((StringToken)portChildren[^1]).Value
                + "/"
                + ((StringToken)protoChildren[0]).Value;
            if (!first) sb.Append(',');
            SerializePrimitive(sb, "string", mergedValue);
            first = false;

            for (int j = 1; j < protoChildren.Count; j++)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, protoChildren[j]);
                first = false;
            }
        }
        else if (!hasLineContinuations && lastPortIsString && !firstProtoIsString)
        {
            // No line continuations: append "/" to the last port string
            for (int j = 0; j < portChildren.Count - 1; j++)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, portChildren[j]);
                first = false;
            }

            string portWithSlash = ((StringToken)portChildren[^1]).Value + "/";
            if (!first) sb.Append(',');
            SerializePrimitive(sb, "string", portWithSlash);
            first = false;

            foreach (Token protoChild in protoChildren)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, protoChild);
                first = false;
            }
        }
        else if (!hasLineContinuations && firstProtoIsString)
        {
            // No line continuations: prepend "/" to the first proto string
            foreach (Token portChild in portChildren)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, portChild);
                first = false;
            }

            string slashProto = "/" + ((StringToken)protoChildren[0]).Value;
            if (!first) sb.Append(',');
            SerializePrimitive(sb, "string", slashProto);
            first = false;

            for (int j = 1; j < protoChildren.Count; j++)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, protoChildren[j]);
                first = false;
            }
        }
        else if (!hasLineContinuations)
        {
            // No line continuations, no mergeable strings: add "/" as separate node
            foreach (Token portChild in portChildren)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, portChild);
                first = false;
            }

            if (!first) sb.Append(',');
            SerializePrimitive(sb, "string", "/");
            first = false;

            foreach (Token protoChild in protoChildren)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, protoChild);
                first = false;
            }
        }
        else
        {
            // Line continuations present: preserve their position relative to
            // the slash. Lean parses char-by-char, so the slash is part of
            // whichever string segment it is adjacent to:
            //   "80\\\n/tcp" -> port["80"], preSlashLC, string["/tcp"]
            //   "80/\\\ntcp" -> port["80/"], postSlashLC, string["tcp"]
            //   "80\\\n/\\\ntcp" -> port["80"], preSlashLC, string["/"], postSlashLC, string["tcp"]

            // Emit port children. When post-slash LCs exist (slash is adjacent
            // to port text), append "/" to the last port string.
            if (hasPostSlashLCs && !hasPreSlashLCs && lastPortIsString)
            {
                // Slash is adjacent to port: append "/" to last port string
                for (int j = 0; j < portChildren.Count - 1; j++)
                {
                    if (!first) sb.Append(',');
                    SerializeToken(sb, portChildren[j]);
                    first = false;
                }

                string portWithSlash = ((StringToken)portChildren[^1]).Value + "/";
                if (!first) sb.Append(',');
                SerializePrimitive(sb, "string", portWithSlash);
                first = false;
            }
            else if (hasPostSlashLCs && !hasPreSlashLCs)
            {
                // Slash is adjacent to port but last port child is not a string
                foreach (Token portChild in portChildren)
                {
                    if (!first) sb.Append(',');
                    SerializeToken(sb, portChild);
                    first = false;
                }

                if (!first) sb.Append(',');
                SerializePrimitive(sb, "string", "/");
                first = false;
            }
            else
            {
                // Pre-slash LCs exist: slash is separated from port by LCs.
                // Emit port children, then pre-slash LCs, then "/" merged
                // with protocol content.
                foreach (Token portChild in portChildren)
                {
                    if (!first) sb.Append(',');
                    SerializeToken(sb, portChild);
                    first = false;
                }

                // Emit pre-slash LineContinuationTokens
                foreach (LineContinuationToken lc in preSlashLCs)
                {
                    if (!first) sb.Append(',');
                    SerializeToken(sb, lc);
                    first = false;
                }
            }

            // Emit protocol content. When pre-slash LCs exist and no post-slash
            // LCs, the slash is adjacent to the protocol: prepend "/" to first
            // proto string.
            if (hasPreSlashLCs && !hasPostSlashLCs && firstProtoIsString)
            {
                string slashProto = "/" + ((StringToken)protoChildren[0]).Value;
                if (!first) sb.Append(',');
                SerializePrimitive(sb, "string", slashProto);
                first = false;

                for (int j = 1; j < protoChildren.Count; j++)
                {
                    if (!first) sb.Append(',');
                    SerializeToken(sb, protoChildren[j]);
                    first = false;
                }
            }
            else if (hasPreSlashLCs && !hasPostSlashLCs)
            {
                // Pre-slash LCs, no post-slash LCs, first proto is not string
                if (!first) sb.Append(',');
                SerializePrimitive(sb, "string", "/");
                first = false;

                foreach (Token protoChild in protoChildren)
                {
                    if (!first) sb.Append(',');
                    SerializeToken(sb, protoChild);
                    first = false;
                }
            }
            else if (hasPreSlashLCs && hasPostSlashLCs)
            {
                // Both pre-slash and post-slash LCs: the "/" is a separate
                // string segment between the two LC groups. Emit "/" first,
                // then post-slash LCs, then protocol children.
                if (!first) sb.Append(',');
                SerializePrimitive(sb, "string", "/");
                first = false;

                foreach (LineContinuationToken lc in postSlashLCs)
                {
                    if (!first) sb.Append(',');
                    SerializeToken(sb, lc);
                    first = false;
                }

                foreach (Token protoChild in protoChildren)
                {
                    if (!first) sb.Append(',');
                    SerializeToken(sb, protoChild);
                    first = false;
                }
            }
            else
            {
                // Only post-slash LCs (slash already emitted with port above).
                // Emit post-slash LCs then protocol directly.
                foreach (LineContinuationToken lc in postSlashLCs)
                {
                    if (!first) sb.Append(',');
                    SerializeToken(sb, lc);
                    first = false;
                }

                foreach (Token protoChild in protoChildren)
                {
                    if (!first) sb.Append(',');
                    SerializeToken(sb, protoChild);
                    first = false;
                }
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
    // RUN instruction — mount value flattening for diff test normalization.
    // C# parses mount flag values into structured KeyValueToken children
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
