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

        if (token is LabelInstruction labelInst)
        {
            SerializeLabel(sb, labelInst);
            return;
        }

        // RUN needs whitespace splitting + mount value flattening (issue #200)
        if (token is RunInstruction)
        {
            SerializeRunInstruction(sb, (RunInstruction)token);
            return;
        }

        // CMD, ENTRYPOINT, HEALTHCHECK need whitespace splitting
        if (token is CmdInstruction || token is EntrypointInstruction || token is HealthCheckInstruction)
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
    // Shell form instructions (RUN, CMD, ENTRYPOINT, HEALTHCHECK)
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
    // Workaround: LABEL instruction
    // C# uses LiteralToken for label keys, Lean uses IdentifierToken.
    // We remap the key's kind from "literal" to "identifier" during serialization.
    // ===================================================================

    private static void SerializeLabel(StringBuilder sb, LabelInstruction label)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"instruction\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        foreach (Token child in label.Tokens)
        {
            if (IsKeyValueToken(child))
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeLabelKeyValue(sb, (AggregateToken)child);
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

    private static void SerializeLabelKeyValue(StringBuilder sb, AggregateToken kvToken)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"keyValue\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        bool isFirstChild = true;
        foreach (Token child in kvToken.Tokens)
        {
            if (!first) sb.Append(',');
            first = false;

            // The first child of a LABEL KeyValueToken is the key.
            // C# uses LiteralToken, Lean uses IdentifierToken. Remap.
            if (isFirstChild && child is LiteralToken)
            {
                SerializeAggregate(sb, "identifier", child);
            }
            else
            {
                SerializeToken(sb, child);
            }
            isFirstChild = false;
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

            // Emit post-slash LineContinuationTokens (between slash and protocol)
            foreach (LineContinuationToken lc in postSlashLCs)
            {
                if (!first) sb.Append(',');
                SerializeToken(sb, lc);
                first = false;
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
                // string segment between the two LC groups. Emit it explicitly.
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
                // Only post-slash LCs (slash already emitted with port above).
                // Emit protocol directly.
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
