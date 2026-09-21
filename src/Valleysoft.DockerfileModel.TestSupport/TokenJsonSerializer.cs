using System.Text;
using Valleysoft.DockerfileModel;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.TestSupport;

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
///   - https://github.com/mthalman/DockerfileModel/issues/387 (shell-form final newline ownership): C# keeps the newline in the shell
///     literal to preserve the public round-trip token model. Canonical JSON moves
///     trailing newline tokens out to instruction level to match Lean's BuildKit-derived
///     representation without changing the parsed model.
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

        if (token is EnvInstruction envInstruction)
        {
            SerializeEnvInstruction(sb, envInstruction);
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
        SerializeAggregate(sb, kind, token, ((AggregateToken)token).Tokens);
    }

    private static void SerializeAggregate(StringBuilder sb, string kind, Token token, IEnumerable<Token> children)
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

        bool first = true;
        foreach (Token child in children)
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

    private static void SerializeEnvInstruction(StringBuilder sb, EnvInstruction instruction)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"instruction\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        foreach (Token child in instruction.Tokens)
        {
            if (child is KeyValueToken<Variable, LiteralToken> keyValue)
            {
                if (!first) sb.Append(',');
                SerializeEnvKeyValue(sb, keyValue);
                first = false;
                continue;
            }

            if (!first) sb.Append(',');
            SerializeToken(sb, child);
            first = false;
        }

        sb.Append("]}");
    }

    private static void SerializeEnvKeyValue(StringBuilder sb, KeyValueToken<Variable, LiteralToken> keyValue)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"keyValue\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        foreach (Token child in keyValue.Tokens)
        {
            if (child is LiteralToken literal && IsEnvEscapedQuoteLiteral(literal))
            {
                if (!first) sb.Append(',');
                SerializeEnvEscapedQuoteLiteral(sb, literal);
                first = false;
                continue;
            }

            if (!first) sb.Append(',');
            SerializeToken(sb, child);
            first = false;
        }

        sb.Append("]}");
    }

    private static bool IsEnvEscapedQuoteLiteral(LiteralToken literal)
    {
        return literal is EnvEscapedQuoteLiteralToken { HasOriginalEscapedQuoteSyntax: true }
            && literal.QuoteChar.HasValue;
    }

    private static void SerializeEnvEscapedQuoteLiteral(StringBuilder sb, LiteralToken literal)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"literal\",\"quoteChar\":null,\"children\":[");

        bool first = true;
        foreach (Token child in CollapseStringTokens(ConcatTokens(
            new Token[] { new StringToken(literal.QuoteChar!.Value.ToString()) },
            literal.Tokens,
            new Token[] { new StringToken(literal.QuoteChar.Value.ToString()) })))
        {
            if (!first) sb.Append(',');
            SerializeToken(sb, child);
            first = false;
        }

        sb.Append("]}");
    }

    private static IEnumerable<Token> CollapseStringTokens(IEnumerable<Token> tokens)
    {
        StringBuilder builder = new();
        foreach (Token token in tokens)
        {
            if (token is StringToken stringToken)
            {
                builder.Append(stringToken.Value);
                continue;
            }

            if (builder.Length > 0)
            {
                yield return new StringToken(builder.ToString());
                builder.Clear();
            }

            yield return token;
        }

        if (builder.Length > 0)
        {
            yield return new StringToken(builder.ToString());
        }
    }

    private static IEnumerable<Token> ConcatTokens(params IEnumerable<Token>[] tokenSets) =>
        tokenSets.SelectMany(tokens => tokens);

    // ===================================================================
    // Shell form literal serialization
    // Shell form commands are parsed as opaque text without variable
    // expansion — $VAR is treated as a regular character sequence inside
    // a StringToken, not decomposed into a VariableRefToken. If a
    // VariableRefToken is ever encountered here, it indicates a parsing
    // regression that should be investigated.
    // ===================================================================

    /// <summary>
    /// Serialize a shell form LiteralToken. Shell form commands should never contain
    /// VariableRefToken children, and their trailing final newline tokens are serialized
    /// as instruction-level siblings to match Lean's canonical BuildKit-derived shape.
    /// </summary>
    private static void SerializeShellFormLiteral(StringBuilder sb, LiteralToken literal, ref bool first)
    {
        List<Token> literalChildren = literal.Tokens.ToList();
        foreach (Token child in literalChildren)
        {
            if (child is VariableRefToken)
            {
                throw new InvalidOperationException(
                    "Unexpected VariableRefToken in shell form LiteralToken. " +
                    "Shell form commands should be parsed as opaque text without variable expansion.");
            }
        }

        int contentEnd = literalChildren.Count;
        while (contentEnd > 0 && literalChildren[contentEnd - 1] is NewLineToken)
        {
            contentEnd--;
        }

        if (!first) sb.Append(',');
        SerializeAggregate(sb, "literal", literal, literalChildren.Take(contentEnd));
        first = false;

        for (int i = contentEnd; i < literalChildren.Count; i++)
        {
            if (!first) sb.Append(',');
            SerializeToken(sb, literalChildren[i]);
            first = false;
        }
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
                    // Validate shell form LiteralTokens (fail-fast on VariableRefToken)
                    if (cmdChild is LiteralToken lit)
                    {
                        SerializeShellFormLiteral(sb, lit, ref first);
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
    // Workaround: COPY/ADD unrecognized flags and boolean flag explicit values
    //
    // Issue #238: COPY --parents — C# treats it as literal["--parents"]; Lean: keyValue
    // Issue #239: COPY --exclude=... — C# treats it as literal["--exclude=..."]; Lean: keyValue
    // Issue #240: ADD --unpack — C# treats it as literal["--unpack"]; Lean: keyValue
    // Issue #241: ADD --exclude=... — C# treats it as literal["--exclude=..."]; Lean: keyValue
    // Strategy:
    //   1. Scan instruction tokens with a look-ahead of 1.
    //   2. A literal whose StringToken value starts with a Lean-recognized file-transfer
    //      flag is emitted as keyValue; unrelated unknown flags remain opaque literals.
    // ===================================================================

    private static void SerializeCopyOrAddInstruction(StringBuilder sb, Instruction instruction)
    {
        sb.Append("{\"type\":\"aggregate\",\"kind\":\"instruction\",\"quoteChar\":null,\"children\":[");

        List<Token> tokens = instruction.Tokens.ToList();
        bool first = true;

        for (int i = 0; i < tokens.Count; i++)
        {
            Token child = tokens[i];

            // Workaround #238/#239/#240/#241: Lean-recognized flags that C# treats as
            // source literals are emitted as keyValue[-, -, keyword["name"], optionally =, literal["value"]].
            if (child is LiteralToken flagLit && IsLeanRecognizedFileTransferFlagLiteral(instruction, flagLit, out string? flagName, out IReadOnlyList<Token>? flagValueTokens))
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeUnrecognizedFlagAsKeyValue(sb, flagName!, flagValueTokens);
                continue;
            }

            if (!first) sb.Append(',');
            SerializeToken(sb, child);
            first = false;
        }

        sb.Append("]}");
    }

    /// <summary>
    /// Returns the serialized text of a LiteralToken.
    /// </summary>
    private static string GetLiteralText(LiteralToken literal) => literal.ToString();

    /// <summary>
    /// Returns true if the literal token holds a file-transfer flag text that Lean recognizes
    /// but the C# parser kept as an operand literal. Parses the flag name and optional value tokens.
    /// </summary>
    private static bool IsLeanRecognizedFileTransferFlagLiteral(
        Instruction instruction, LiteralToken literal, out string? flagName, out IReadOnlyList<Token>? flagValueTokens)
    {
        flagValueTokens = null;
        string text = GetLiteralText(literal);
        if (text.StartsWith("--") && text.Length > 2)
        {
            string nameAndValue = text.Substring(2); // strip "--"
            int eqIdx = nameAndValue.IndexOf('=');
            if (eqIdx >= 0)
            {
                flagName = nameAndValue.Substring(0, eqIdx);
            }
            else
            {
                flagName = nameAndValue;
                flagValueTokens = null;
            }

            if (eqIdx >= 0)
            {
                flagValueTokens = SliceTokenText(literal.Tokens, 2 + flagName.Length + 1).ToArray();
            }

            return instruction switch
            {
                CopyInstruction => TryNormalizeCopyFileTransferFlag(flagName, ref flagValueTokens),
                AddInstruction => TryNormalizeAddFileTransferFlag(flagName, ref flagValueTokens),
                _ => false
            };
        }
        flagName = null;
        flagValueTokens = null;
        return false;
    }

    private static bool TryNormalizeCopyFileTransferFlag(string flagName, ref IReadOnlyList<Token>? flagValueTokens) =>
        flagName switch
        {
            _ when IsFlagName(flagName, "parents") => TryNormalizeBooleanFlagValue(ref flagValueTokens),
            _ when IsFlagName(flagName, "exclude") => IsValueFlagValue(flagValueTokens),
            _ => false
        };

    private static bool TryNormalizeAddFileTransferFlag(string flagName, ref IReadOnlyList<Token>? flagValueTokens) =>
        flagName switch
        {
            _ when IsFlagName(flagName, "unpack") => TryNormalizeBooleanFlagValue(ref flagValueTokens),
            _ when IsFlagName(flagName, "exclude") => IsValueFlagValue(flagValueTokens),
            _ => false
        };

    private static bool IsFlagName(string actual, string expected) =>
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalizeBooleanFlagValue(ref IReadOnlyList<Token>? valueTokens)
    {
        if (valueTokens is null)
        {
            return true;
        }

        string value = string.Concat(valueTokens.Select(token => token.ToString()));
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsValueFlagValue(IReadOnlyList<Token>? valueTokens) =>
        valueTokens is not null && valueTokens.Count > 0;

    private static IEnumerable<Token> SliceTokenText(IEnumerable<Token> tokens, int startOffset)
    {
        int offset = 0;
        foreach (Token token in tokens)
        {
            string text = token.ToString();
            int nextOffset = offset + text.Length;
            if (nextOffset <= startOffset)
            {
                offset = nextOffset;
                continue;
            }

            if (offset < startOffset)
            {
                if (token is StringToken)
                {
                    yield return new StringToken(text.Substring(startOffset - offset));
                    offset = nextOffset;
                    continue;
                }

                throw new InvalidOperationException("Cannot split non-string token while serializing file-transfer flag value.");
            }

            yield return token;
            offset = nextOffset;
        }
    }

    /// <summary>
    /// Serialize an unrecognized flag (originally a LiteralToken with text "--flagname[=value]")
    /// as a keyValue token matching Lean's structure.
    /// Without value: keyValue[symbol[-], symbol[-], keyword["flagname"]]
    /// With value:    keyValue[symbol[-], symbol[-], keyword["flagname"], symbol[=], literal[string["value"]]]
    /// </summary>
    private static void SerializeUnrecognizedFlagAsKeyValue(StringBuilder sb, string flagName, IReadOnlyList<Token>? flagValueTokens)
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

        if (flagValueTokens is not null)
        {
            // symbol["="]
            sb.Append(',');
            SerializePrimitive(sb, "symbol", "=");
            sb.Append(',');
            SerializeAggregate(sb, "literal", new LiteralToken(flagValueTokens, canContainVariables: true, Dockerfile.DefaultEscapeChar), flagValueTokens);
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
                    // Validate shell form LiteralTokens (fail-fast on VariableRefToken)
                    if (cmdChild is LiteralToken lit)
                    {
                        SerializeShellFormLiteral(sb, lit, ref first);
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
