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
///   - EXPOSE port/protocol: C# splits into literal+symbol+literal; Lean uses one flat literal.
///     Workaround merges the three tokens back into a single literal during serialization.
///   - COPY/ADD unrecognized flags (issues #238, #239, #240, #241): C# does not recognize
///     --parents, --exclude (COPY), --unpack, --exclude (ADD) as named flags, so it treats
///     them as opaque literal file-path tokens. Lean recognizes them and emits keyValue tokens.
///     Workaround converts literal["--flagname[=value]"] → keyValue[-, -, keyword["flagname"],
///     optionally =, literal["value"]] when the literal starts with "--".
///   - #259 (COPY/ADD empty exec-form arrays): C# produces a literal with value "[]" instead
///     of two symbol tokens. Workaround splits literal["[]"] into symbol["["] + symbol["]"].
///   - #263 (mount value trailing whitespace): mount.ToString() absorbs trailing whitespace
///     into the mount value string. Workaround trims and emits a separate whitespace token.
///   - #264 (trailing whitespace on instructions): FIXED. Both C# and Lean now emit trailing
///     instruction whitespace as a standalone WhitespaceToken sibling at instruction level.
///     (The Lean argTokens fix removed the guard that prevented capturing trailing whitespace
///     without a line continuation; C# already emitted it the same way.)
///   - #265 (hash as comment in shell-form and LABEL values): C# parses # mid-text as a
///     comment aggregate inside a literal; Lean treats it as plain text. Workaround merges
///     the comment children back into the preceding string token.
///   - #266 (flag line continuation): COPY/ADD/RUN keyValue flags with LineContinuation tokens
///     inside their value differ structurally. Workaround flattens the entire keyValue into an
///     opaque literal matching Lean's flat representation.
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
            // Workaround for #265: hash treated as comment in literal values (LABEL, etc.).
            // Workaround for #266: raw symbol("\\") + newLine pairs in literals should be
            // wrapped in lineContinuation aggregates to match Lean's structure.
            bool hasComments = literal.Tokens.Any(t => t is CommentToken);
            bool hasRawLineContinuations = HasRawLineContinuationPair(literal.Tokens);
            if (hasComments || hasRawLineContinuations)
            {
                SerializeLiteralNormalized(sb, literal, hasComments, hasRawLineContinuations);
            }
            else
            {
                SerializeAggregate(sb, "literal", literal);
            }
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
            // Workaround for #265: CommentToken as direct keyValue child (LABEL key=# case).
            // When a CommentToken appears as the value of a key=value pair (after "="),
            // C# emits it as a comment aggregate, but Lean expects a literal.
            if (HasCommentTokenValue((AggregateToken)token))
            {
                SerializeKeyValueWithCommentMerge(sb, (AggregateToken)token);
            }
            else
            {
                SerializeAggregate(sb, "keyValue", token);
            }
            return;
        }

        // Fallback for any other AggregateToken subtype (e.g., ArgDeclaration)
        if (token is AggregateToken)
        {
            // ArgDeclaration implements IKeyValuePair and extends AggregateToken
            // It functions as a key-value pair in the token tree
            if (token is IKeyValuePair)
            {
                // Also apply #265 workaround for ArgDeclaration
                if (HasCommentTokenValue((AggregateToken)token))
                {
                    SerializeKeyValueWithCommentMerge(sb, (AggregateToken)token);
                }
                else
                {
                    SerializeAggregate(sb, "keyValue", token);
                }
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
    // Workaround: #265 — hash treated as comment in shell-form commands
    // C# parses a '#' character mid-command as a CommentToken inside a
    // LiteralToken. Lean (following BuildKit) treats '#' as plain text.
    // Merge: string("echo ") + comment[symbol("#"), string("text")]
    //      → string("echo #text")
    // The merge replaces CommentToken children with their text content
    // appended to the preceding StringToken.
    // ===================================================================

    /// <summary>
    /// Returns true if a token list contains a raw "symbol('\\') + NewLineToken" pair
    /// that should be wrapped in a lineContinuation aggregate (workaround for #266).
    /// </summary>
    private static bool HasRawLineContinuationPair(IEnumerable<Token> tokens)
    {
        Token? prev = null;
        foreach (Token t in tokens)
        {
            if (prev is SymbolToken sym && (sym.Value == "\\" || sym.Value == "`")
                && t is NewLineToken)
            {
                return true;
            }
            prev = t;
        }
        return false;
    }

    // ===================================================================
    // Workaround: #265 — CommentToken as direct keyValue value child
    // When LABEL key=#value is parsed, C# produces:
    //   keyValue[identifier["key"], symbol["="], comment[symbol["#"], string["value"]]]
    // Lean produces:
    //   keyValue[identifier["key"], symbol["="], literal[string["#value"]]]
    // Convert CommentToken to literal when it appears as a keyValue's value.
    // ===================================================================

    /// <summary>
    /// Returns true if an aggregate token (keyValue) has a CommentToken as a direct child
    /// that appears in the value position (after the "=" symbol).
    /// </summary>
    private static bool HasCommentTokenValue(AggregateToken aggregate)
    {
        bool seenEquals = false;
        foreach (Token child in aggregate.Tokens)
        {
            if (child is SymbolToken sym && sym.Value == "=")
            {
                seenEquals = true;
            }
            else if (seenEquals && child is CommentToken)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Serialize a keyValue aggregate where a CommentToken appears in the value position.
    /// Converts the CommentToken to a literal[string[text]] matching Lean's output.
    /// </summary>
    private static void SerializeKeyValueWithCommentMerge(StringBuilder sb, AggregateToken keyValue)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"keyValue\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        bool seenEquals = false;

        foreach (Token child in keyValue.Tokens)
        {
            if (child is SymbolToken sym && sym.Value == "=")
            {
                seenEquals = true;
                if (!first) sb.Append(',');
                SerializeToken(sb, child);
                first = false;
            }
            else if (seenEquals && child is CommentToken comment)
            {
                // Convert comment to literal[string[text]]
                string commentText = ExtractCommentText(comment);
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":null,\"children\":[");
                SerializePrimitive(sb, "string", commentText);
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
    /// Serialize a LiteralToken with normalization for known C# vs Lean differences:
    ///   - #265: merge embedded CommentToken children back into plain string text
    ///   - #266: wrap raw symbol("\\") + NewLineToken pairs into lineContinuation aggregates
    /// </summary>
    private static void SerializeLiteralNormalized(
        StringBuilder sb, LiteralToken literal, bool hasComments, bool hasRawLineContinuations)
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

        List<Token> tokens = literal.Tokens.ToList();

        // Apply normalizations in order
        if (hasComments)
            tokens = FlattenCommentTokens(tokens);
        if (hasRawLineContinuations)
            tokens = WrapRawLineContinuations(tokens);

        bool first = true;
        foreach (Token t in tokens)
        {
            if (!first) sb.Append(',');
            SerializeToken(sb, t);
            first = false;
        }

        sb.Append("]}");
    }

    /// <summary>
    /// Serialize a LiteralToken, merging any embedded CommentToken children back into
    /// plain string text (workaround for #265). Applies to all literal contexts (LABEL, etc.).
    /// </summary>
    private static void SerializeLiteralWithHashMerge(StringBuilder sb, LiteralToken literal)
    {
        SerializeLiteralNormalized(sb, literal, hasComments: true, hasRawLineContinuations: false);
    }

    /// <summary>
    /// Wrap raw symbol("\\") + NewLineToken pairs into lineContinuation aggregates.
    /// C# may emit backslash+newline as separate tokens inside a literal; Lean always
    /// wraps them as lineContinuation[symbol("\\"), newLine(...)].
    ///
    /// Returns a new list where each matched pair is replaced by a
    /// RawLineContinuationMarker sentinel, which SerializeToken handles specially.
    /// </summary>
    private static List<Token> WrapRawLineContinuations(List<Token> tokens)
    {
        var result = new List<Token>();

        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];

            if (token is SymbolToken sym && (sym.Value == "\\" || sym.Value == "`")
                && i + 1 < tokens.Count && tokens[i + 1] is NewLineToken newLine)
            {
                // Build a synthetic LineContinuationToken containing the symbol + newLine.
                // The escape char is the first character of sym.Value.
                char escapeChar = sym.Value[0];
                var lc = LineContinuationToken.Parse(sym.Value + newLine.Value, escapeChar);
                result.Add(lc);
                i++; // skip the newLine token
            }
            else
            {
                result.Add(token);
            }
        }

        return result;
    }

    /// <summary>
    /// Serialize a shell-form LiteralToken, merging any embedded CommentToken
    /// children back into plain string text (workaround for #265).
    /// Also validates that no VariableRefToken children are present.
    /// </summary>
    private static void SerializeShellFormLiteralWithHashMerge(StringBuilder sb, LiteralToken literal)
    {
        foreach (Token child in literal.Tokens)
        {
            if (child is VariableRefToken)
            {
                throw new InvalidOperationException(
                    "Unexpected VariableRefToken in shell form LiteralToken. " +
                    "Shell form commands should be parsed as opaque text without variable expansion.");
            }
        }

        // Check if any CommentToken children exist
        bool hasComments = literal.Tokens.Any(t => t is CommentToken);
        if (!hasComments)
        {
            SerializeAggregate(sb, "literal", literal);
            return;
        }

        // Merge comment tokens back into preceding string content
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

        // Flatten: collect all string content (merging comment text with preceding string)
        // Strategy: build a list of tokens, collapsing CommentToken children into strings.
        List<Token> flatTokens = FlattenCommentTokens(literal.Tokens.ToList());

        bool first = true;
        foreach (Token t in flatTokens)
        {
            if (!first) sb.Append(',');
            SerializeToken(sb, t);
            first = false;
        }

        sb.Append("]}");
    }

    /// <summary>
    /// Flatten a token list by merging CommentToken children (which represent mid-text #...)
    /// back into adjacent StringToken content. A CommentToken contains: symbol("#") + string(...).
    /// The preceding StringToken gets the "#" appended, then the comment's string text appended.
    /// If no preceding StringToken exists, a new StringToken with "#text" is inserted.
    /// </summary>
    private static List<Token> FlattenCommentTokens(List<Token> tokens)
    {
        var result = new List<Token>();

        foreach (Token token in tokens)
        {
            if (token is CommentToken comment)
            {
                // Extract the comment text: "#" + the string content of the comment
                string commentText = "#";
                foreach (Token ct in comment.Tokens)
                {
                    if (ct is StringToken s)
                        commentText += s.Value;
                    else if (ct is SymbolToken sym && sym.Value == "#")
                    {
                        // The "#" symbol is the start of the comment token — skip it
                        // since we're already prepending "#" above.
                        // Actually: don't double-add. The symbol("#") is the "#" itself.
                        // We've already set commentText = "#" above; so strip that and
                        // reconstruct from the symbol.
                    }
                }
                // Re-extract properly: gather the full comment text from all children
                commentText = ExtractCommentText(comment);

                // Merge with preceding StringToken if one exists
                if (result.Count > 0 && result[result.Count - 1] is StringToken prevStr)
                {
                    result[result.Count - 1] = new StringToken(prevStr.Value + commentText);
                }
                else
                {
                    result.Add(new StringToken(commentText));
                }
            }
            else if (token is StringToken str && result.Count > 0 && result[result.Count - 1] is StringToken prevStr2)
            {
                // Merge adjacent StringTokens
                result[result.Count - 1] = new StringToken(prevStr2.Value + str.Value);
            }
            else
            {
                result.Add(token);
            }
        }

        return result;
    }

    /// <summary>
    /// Extract the full text that a CommentToken represents, including the leading "#".
    /// CommentToken structure: symbol("#") + string("text...")
    /// </summary>
    private static string ExtractCommentText(CommentToken comment)
    {
        var sb2 = new StringBuilder();
        foreach (Token child in comment.Tokens)
        {
            if (child is StringToken s)
                sb2.Append(s.Value);
            else if (child is SymbolToken sym)
                sb2.Append(sym.Value);
        }
        return sb2.ToString();
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
                        // Workaround for #265: hash treated as comment in shell-form commands.
                        SerializeShellFormLiteralWithHashMerge(sb, lit);
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

            // Workaround for #266: keyValue flag that contains a LineContinuationToken
            // inside its value. C# parses the flag into a structured keyValue with a
            // lineContinuation child; Lean treats the entire flag as an opaque literal.
            // Flatten the keyValue into a literal[string("--key="), lineContinuation[...], string("value")].
            if (child is AggregateToken && IsKeyValueToken(child) && FlagKeyValueHasLineContinuation((AggregateToken)child))
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeFlagKeyValueAsLiteral(sb, (AggregateToken)child);
                continue;
            }

            // Workaround #238/#239/#240/#241: LiteralToken whose value starts with "--" is
            // an unrecognized flag. Emit as keyValue[-, -, keyword["name"], optionally =, literal["value"]].
            if (child is LiteralToken flagLit && IsUnrecognizedFlagLiteral(flagLit, out string? flagName, out string? flagValue))
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeUnrecognizedFlagAsKeyValue(sb, flagName!, flagValue);
                continue;
            }

            // Workaround for #259: COPY/ADD [] produces a LiteralToken with string value "[]".
            // Lean emits two symbol tokens: symbol["["] + symbol["]"].
            if (child is LiteralToken emptyArrayLit && GetLiteralText(emptyArrayLit) == "[]")
            {
                if (!first) sb.Append(',');
                SerializePrimitive(sb, "symbol", "[");
                sb.Append(',');
                SerializePrimitive(sb, "symbol", "]");
                first = false;
                continue;
            }

            // Workaround for #259: COPY/ADD [ ] (with whitespace) — C# produces
            // literal["["] and literal["]"] as separate tokens. Lean emits them as symbols.
            if (child is LiteralToken bracketLit)
            {
                string bracketText = GetLiteralText(bracketLit);
                if (bracketText == "[" || bracketText == "]")
                {
                    if (!first) sb.Append(',');
                    SerializePrimitive(sb, "symbol", bracketText);
                    first = false;
                    continue;
                }
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
    ///
    /// Returns false if the literal contains non-string tokens (symbols, newlines) that
    /// represent line continuations — those are the #266 case and should not be converted
    /// to keyValue structures.
    /// </summary>
    private static bool IsUnrecognizedFlagLiteral(LiteralToken literal, out string? flagName, out string? flagValue)
    {
        // Workaround for #266: literals that contain non-string tokens (symbol+newLine pairs
        // representing line continuations) should NOT be converted to keyValue — they represent
        // flags with line continuations that C# failed to parse as structured flags.
        // Only convert if all tokens are StringTokens.
        foreach (Token child in literal.Tokens)
        {
            if (child is not StringToken)
            {
                flagName = null;
                flagValue = null;
                return false;
            }
        }

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
    // Workaround: #266 — flag keyValue with LineContinuation in value
    // COPY/ADD/RUN flags like --from=\<newline>builder produce a structured
    // keyValue with a LineContinuationToken inside the value. Lean does not
    // parse this as a structured keyValue at all — it treats the whole flag
    // as an opaque literal. Flatten to: literal[string("--key="), LC[...], string("val")].
    // ===================================================================

    /// <summary>
    /// Returns true if an AggregateToken representing a keyValue flag contains
    /// a LineContinuationToken anywhere inside its value subtree.
    /// </summary>
    private static bool FlagKeyValueHasLineContinuation(AggregateToken keyValue)
    {
        foreach (Token child in keyValue.Tokens)
        {
            if (child is LineContinuationToken)
                return true;
            // Also check inside the value aggregate (the flag value is often a LiteralToken)
            if (child is AggregateToken agg)
            {
                foreach (Token grandchild in agg.Tokens)
                {
                    if (grandchild is LineContinuationToken)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Flatten a flag keyValue that contains LineContinuation tokens into an
    /// opaque literal matching Lean's representation.
    ///
    /// C# structure (example):  keyValue[--, --, keyword("from"), =, literal(lineCont, string("builder"))]
    /// Lean structure:          literal[string("--from="), lineContinuation[...], string("builder")]
    ///
    /// The flatten strategy: collect the raw string of the key (without the leading "--")
    /// then emit: literal[string("--key="), then all value subtokens inline].
    /// </summary>
    private static void SerializeFlagKeyValueAsLiteral(StringBuilder sb, AggregateToken keyValue)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":null,\"children\":[");

        // Reconstruct the "--flagname=" prefix from the keyValue tokens.
        // The key tokens are: symbol[-], symbol[-], keyword[string["name"]]
        // Find the keyword name.
        string flagName = "";
        bool seenEquals = false;
        var valueTokens = new List<Token>();

        foreach (Token child in keyValue.Tokens)
        {
            if (child is KeywordToken kw)
            {
                // Concatenate keyword string tokens
                foreach (Token kwChild in kw.Tokens)
                {
                    if (kwChild is StringToken s)
                        flagName += s.Value;
                }
            }
            else if (child is SymbolToken sym && sym.Value == "=")
            {
                seenEquals = true;
            }
            else if (seenEquals)
            {
                // Everything after the "=" is the value subtree
                if (child is AggregateToken valAgg)
                {
                    // Inline children of the value aggregate (usually a LiteralToken)
                    foreach (Token valChild in valAgg.Tokens)
                        valueTokens.Add(valChild);
                }
                else
                {
                    valueTokens.Add(child);
                }
            }
        }

        // Emit: string("--flagname=")
        SerializePrimitive(sb, "string", "--" + flagName + "=");

        // Emit the value tokens (may include LineContinuationTokens and StringTokens)
        foreach (Token vt in valueTokens)
        {
            sb.Append(',');
            SerializeToken(sb, vt);
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
                // Workaround for #263: SerializeMountFlag returns absorbed trailing whitespace.
                // Emit the whitespace at instruction level (after the keyValue).
                string trailingWs = SerializeMountFlag(sb, mountFlag);
                if (trailingWs.Length > 0)
                {
                    sb.Append(',');
                    SerializePrimitive(sb, "whitespace", trailingWs);
                }
            }
            // Workaround for #266: other keyValue flags (--network, --security) that
            // contain a LineContinuationToken in their value subtree.
            else if (child is AggregateToken && IsKeyValueToken(child) && FlagKeyValueHasLineContinuation((AggregateToken)child))
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeFlagKeyValueAsLiteral(sb, (AggregateToken)child);
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
                        // Workaround for #265: hash treated as comment in shell-form commands.
                        SerializeShellFormLiteralWithHashMerge(sb, lit);
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
    /// <summary>
    /// Serialize a MountFlag, flattening its Mount value to an opaque LiteralToken.
    /// Returns any trailing whitespace that was absorbed into the mount string —
    /// the caller must emit this whitespace at the instruction level (outside the keyValue).
    /// </summary>
    private static string SerializeMountFlag(StringBuilder sb, MountFlag mountFlag)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"keyValue\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        string absorbedTrailingWs = "";

        foreach (Token child in mountFlag.Tokens)
        {
            if (child is Mount mount)
            {
                // Workaround for #263: mount.ToString() absorbs trailing whitespace into the
                // mount value string (C# includes the trailing space; Lean does not).
                // Trim the trailing whitespace from the mount text; return it to caller.
                if (!first) sb.Append(',');
                first = false;
                string mountText = mount.ToString();
                string trimmedMount = mountText.TrimEnd(' ', '\t');
                absorbedTrailingWs = mountText.Length > trimmedMount.Length
                    ? mountText.Substring(trimmedMount.Length)
                    : "";

                sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":null,\"children\":[");
                SerializePrimitive(sb, "string", trimmedMount);
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
        return absorbedTrailingWs;
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
