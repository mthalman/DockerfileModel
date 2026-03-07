# Mismatch Analysis: C# vs Lean Parser Differential Testing

**Date:** 2026-03-06
**Author:** Ripley (Lead/Architect)
**Context:** Dallas expanded FsCheck generators and found 91+ mismatches (out of 900) between C# and Lean parsers, falling into 4 categories. This document provides architectural analysis of each, referencing BuildKit's authoritative behavior.

**Governing principle:** Lean is the authoritative specification. We do NOT change Lean to match C#.

---

## Category 1: Variable References in Shell-Form Commands

### What's Happening

The C# parser uses `ArgumentListAsLiteral(escapeChar)` for shell-form commands (RUN, CMD, ENTRYPOINT). This calls `LiteralToken(escapeChar, ...)` with `canContainVariables: false`, which means `$VAR` and `${VAR:-default}` are treated as opaque string characters -- no `VariableRefToken` is ever produced. The entire shell command becomes a single `LiteralToken` containing only `StringToken` children.

The Lean parser uses `shellFormCommand(escapeChar)` which calls `valueOrVariableRef` -- the same combinator used for `literalWithVariables`. This decomposes `$VAR` into structured `VariableRefToken` nodes with sub-components (braces, modifiers, etc.).

**Key code paths:**
- C#: `ShellFormCommand.GetInnerParser` -> `ParseHelper.ArgumentListAsLiteral` -> `LiteralToken(escapeChar, ..., canContainVariables: false)`
- Lean: `shellFormCommand` -> `valueOrVariableRef` -> `variableRefParser` / `simpleVariableRef` / `bracedVariableRef`

### Who's Right According to BuildKit

This requires a sub-analysis because different instructions have different BuildKit behavior:

**Sub-analysis 1a: RUN, CMD, ENTRYPOINT shell-form commands**

BuildKit documentation is explicit: "Variable substitution is NOT expanded by builder in: RUN, CMD, ENTRYPOINT (shell form gets substitution from the shell; exec form gets no substitution)."

The **C# parser is semantically correct** for these instructions. Since BuildKit does not perform variable substitution in RUN/CMD/ENTRYPOINT, treating `$VAR` as opaque text is a faithful representation of what BuildKit does -- the shell, not the builder, handles these references.

However, the **Lean parser is structurally correct** in that it still identifies the _syntactic structure_ of variable references even though they won't be expanded by BuildKit. A parser model that understands `$VAR` is a variable reference (even if it won't be resolved) is richer than one that treats it as flat text.

The C# `CommandInstruction` base class explicitly overrides `ResolveVariables` to return `ToString()` unchanged -- so even if the parser identified variable refs, the resolution engine would ignore them. This confirms the C# design intent: don't bother parsing what won't be resolved.

**Sub-analysis 1b: STOPSIGNAL variable references**

BuildKit documentation says: "Variable substitution IS performed by the builder in: STOPSIGNAL."

Here the situation is different. The C# `StopSignalInstruction.GetArgsParser` uses `LiteralToken(escapeChar, ...)` -- the NON-variable-aware parser (same as shell-form commands). This is a **C# bug**. STOPSIGNAL should use `LiteralWithVariables` because BuildKit resolves variables in STOPSIGNAL arguments.

The Lean parser uses `literalWithVariables escapeChar` in `stopsignalArgsParser`, which correctly decomposes variable references. Lean is correct.

**Evidence:** STOPSIGNAL accounts for 38 of the ~91 mismatches (the single largest instruction contributor), and ALL of them are variable reference mismatches. This is the highest-confidence bug in this analysis.

**Sub-analysis 1c: HEALTHCHECK CMD variable references**

HEALTHCHECK is in the BuildKit expansion list ("Variable substitution is performed by the builder in... ONBUILD"). But the CMD argument within HEALTHCHECK is itself a shell command. BuildKit expands variables in the HEALTHCHECK instruction's flags (`--interval`, `--timeout`, etc.) but the CMD/shell portion follows the same rules as RUN/CMD/ENTRYPOINT. The Lean parser decomposes variable refs in the CMD portion, which is more aggressive than BuildKit's actual behavior.

**Sub-analysis 1d: ONBUILD trigger variable references**

ONBUILD is listed in BuildKit's expansion list. However, ONBUILD wraps another instruction, and the expansion behavior depends on the wrapped instruction. `ONBUILD RUN echo $VAR` -- the `RUN echo $VAR` portion should follow RUN semantics (no builder expansion). The Lean parser decomposes variable refs in the trigger text uniformly, which is again more aggressive.

### Verdict: SPLIT DECISION

| Instruction | Who Should Change | Severity |
|---|---|---|
| STOPSIGNAL | **C# must fix** -- use `LiteralWithVariables` | **HIGH** -- real semantic bug |
| RUN/CMD/ENTRYPOINT | **Neither changes** -- acceptable model divergence | **LOW** -- cosmetic |
| HEALTHCHECK CMD | **Neither changes** -- acceptable model divergence | **LOW** -- cosmetic |
| ONBUILD | **Neither changes** -- acceptable model divergence | **LOW** -- cosmetic |

### Recommendation

1. **STOPSIGNAL fix (C#):** Change `StopSignalInstruction.GetArgsParser` from `LiteralToken(escapeChar, ...)` to `LiteralWithVariables(escapeChar)`. This is a one-line change. The `SignalToken` property type is already `LiteralToken`, which is the return type of `LiteralWithVariables`, so no API surface changes are needed.

2. **Shell-form divergence (serializer workaround):** For RUN/CMD/ENTRYPOINT/HEALTHCHECK/ONBUILD shell-form commands, the differential test serializer should normalize variable references. When comparing C# (flat string) vs Lean (structured variableRef), the serializer should flatten Lean's variableRef tokens back to their string representation before comparison. This is already the correct approach since both representations round-trip identically to the same text.

---

## Category 2: Mount Flag Token Structure

### What's Happening

Two sub-patterns:

**(a) C# structured, Lean opaque:** The C# `MountFlag` parser delegates to `SecretMount.GetParser(escapeChar)` which produces a deeply structured `Mount` token with nested `KeyValueToken` children for `type=bind`, `source=x`, `target=/y`, etc. The Lean parser uses `flagParser "mount" escapeChar` which treats the entire mount value as a single opaque `LiteralToken`.

**(b) C# parse failure on combined flags:** When `--mount=type=tmpfs,target=/path` appears alongside `--network=...`, the C# mount parser sometimes fails (the structured mount sub-parser can't handle certain mount type values), causing the entire instruction to fall back to shell-form parsing. The Lean parser handles both flags correctly because it treats mount values as opaque literals.

### Who's Right According to BuildKit

Mount syntax is a BuildKit extension with complex, evolving semantics. The mount spec (`type=bind,source=x,target=/y,readonly`) is a comma-separated key=value format that BuildKit interprets at build time. It is NOT part of the Dockerfile grammar per se -- it's a flag value that BuildKit interprets.

For a **parser model** (not an evaluator), both approaches are defensible:
- **C# approach (structured):** Provides programmatic access to mount fields. But it's fragile -- new mount types or fields break the parser.
- **Lean approach (opaque):** Treats mount value as a literal string. Robust against new mount types. The mount spec can be parsed in a separate pass if needed.

Sub-pattern (b) is a **C# bug** regardless of the structural approach chosen. The C# mount parser should not fail on valid mount specs and cause the entire RUN instruction to fall back to shell-form parsing.

### Verdict: LEAN IS RIGHT (opaque approach is more robust)

**SEVERITY: MEDIUM** (sub-pattern b is a real parse failure; sub-pattern a is a design choice)

### Recommendation

1. **Short-term (serializer workaround):** The differential test serializer should normalize mount token structure. When comparing, flatten the C# mount structure to match Lean's opaque format.

2. **Medium-term (C# mount parser fix):** Fix the C# mount parser so it does not fail on valid mount specs (sub-pattern b). This may require expanding the mount type whitelist or making the sub-parser more lenient.

3. **Long-term architectural note:** The Lean approach (opaque mount value) is the better design for a parser library. Mount spec interpretation belongs in a higher-level layer (evaluator/builder), not in the parser. However, the C# structured approach is an existing API surface (`RunInstruction.Mounts` property) that consumers may depend on. Changing it would be a breaking change. The pragmatic path is to fix the C# mount parser to not fail on valid inputs while keeping the structured model, and accept the structural divergence in differential tests via serializer normalization.

---

## Category 3: Empty Values in Key=Value Pairs (LABEL, ENV)

### What's Happening

For `LABEL key=` and `ENV key=` (empty value after `=`):
- **C# produces:** `keyValue[identifier["key"], symbol["="], literal[""]]` -- an explicit empty literal token
- **Lean produces:** `keyValue[identifier["key"], symbol["="]]` -- no value token at all

The root cause is in the optional value parser:
- C#: `LiteralWithVariables(escapeChar).Optional()` returns `Some(emptyLiteral)` when no value is consumed, because the C# `MultiVariableFormatValueParser` and `LabelInstruction.ValueParser` use `.GetOrElse(new LiteralToken(""))` -- they explicitly synthesize an empty literal when the optional parser returns None.
- Lean: `Parser.optional (literalWithVariables ...)` returns `none` when there's no value to consume, and the token list construction uses `match value with | some v => [v] | none => []`.

### Who's Right According to BuildKit

BuildKit accepts `ENV key=` and `LABEL key=` as valid -- they set the key to an empty string. Both parser outputs round-trip to the same text (`key=`). The question is whether the token tree should include an explicit empty-string node.

From a **semantic modeling** perspective, the C# approach is more precise: the `=` sign implies a value was intended, and an empty string is the value. `ENV key=` means "set key to empty string", which is different from `ENV key` (which is the legacy form and means something different).

From a **parser purity** perspective, the Lean approach is cleaner: the parser only produces tokens for text that was actually consumed. No synthetic empty nodes.

### Verdict: LEAN SHOULD ADD EMPTY LITERAL TOKEN

**SEVERITY: LOW-MEDIUM**

This is a case where the Lean parser should be adjusted to match the C# behavior -- but wait, the directive says "Lean is the authoritative spec. We do NOT change Lean to match C#."

Re-evaluating: The Lean approach is defensible from a parser perspective. The empty value can be inferred from the presence of `=` without a following value token. However, this creates unnecessary complexity for consumers who need to check "is there a value?" -- they'd need to check both "is there a value token?" AND "is there an `=` symbol without a value token?"

**Revised verdict:** This is an area where neither is clearly "right" -- both are valid parser designs. Since Lean is authoritative, we accept Lean's behavior and adjust the **C# behavior to match Lean** OR add a **serializer normalization** for the differential tests.

### Recommendation

1. **Preferred: Serializer workaround.** When comparing token trees, if C# has a `literal[""]` child after `symbol["="]` and Lean has no corresponding child, the serializer should strip the empty literal from the C# tree before comparison. This preserves the C# API behavior (consumers who call `.Value` on an ENV variable get `""`) while making differential tests pass.

2. **Alternative: C# change.** Modify `MultiVariableFormatValueParser` and `LabelInstruction.ValueParser` to NOT synthesize empty literals. Instead of `.GetOrElse(new LiteralToken(""))`, use the raw optional and handle the absence downstream. This would be a bigger change and could affect consumers.

The serializer workaround is strongly preferred because:
- It doesn't change either parser's behavior
- The C# empty-literal approach has better API ergonomics
- Round-trip fidelity is maintained either way

---

## Category 4: Single-Quoted Strings with Dollar Signs

### What's Happening

Commands containing single-quoted text with `$` (e.g., `awk '{print $1}'`) are parsed differently:
- **C#:** Treats `'{print` and `$1}'` as separate string tokens (the `$` triggers no special handling because `canContainVariables: false` in `ArgumentListAsLiteral`)
- **Lean:** The `shellFormCommand` parser's `valueOrVariableRef` sees `$1` and produces a `VariableRefToken` for it

### Who's Right According to BuildKit

In shell context (RUN shell-form), single quotes are interpreted by the shell, not by the Dockerfile parser or BuildKit. BuildKit does NOT perform variable substitution in RUN, so `$1` inside single quotes is doubly opaque -- neither the builder nor the Dockerfile parser should interpret it.

The **C# behavior is more correct** here -- treating everything in shell-form as flat text avoids false-positive variable detection inside shell quoting contexts. The Lean parser's `shellFormCommand` does not model shell quoting semantics, so it incorrectly identifies `$1` as a variable reference even though it's inside single quotes where no substitution would occur at any level.

However, this is the same fundamental issue as Category 1: the Lean `shellFormCommand` decomposes variable references uniformly, regardless of shell quoting context. Since properly modeling shell quoting in the Dockerfile parser is out of scope (it would require a full shell parser), this is an inherent limitation.

### Verdict: SERIALIZER WORKAROUND (same as Category 1)

**SEVERITY: LOW**

This is a subset of Category 1. The Lean parser's `shellFormCommand` does not understand shell quoting, so it over-identifies variable references. This is acceptable because:
1. The Dockerfile parser is not a shell parser
2. The variable references, though structurally identified, are never resolved (for RUN/CMD/ENTRYPOINT)
3. Round-trip fidelity is maintained regardless

### Recommendation

Same as Category 1's serializer workaround. When comparing shell-form command token trees, flatten Lean's variableRef tokens to their string representation before comparison. No code changes needed in either parser.

---

## Summary Table

| # | Category | Mismatches | Verdict | Severity | Action Required |
|---|----------|-----------|---------|----------|-----------------|
| 1a | Shell-form variable refs (RUN/CMD/ENTRYPOINT) | ~45 | Serializer workaround | LOW | Flatten variableRef in comparisons |
| 1b | STOPSIGNAL variable refs | ~38 | **C# must fix** | **HIGH** | Change to `LiteralWithVariables` |
| 1c | HEALTHCHECK CMD variable refs | ~12 | Serializer workaround | LOW | Same as 1a |
| 1d | ONBUILD trigger variable refs | ~7 | Serializer workaround | LOW | Same as 1a |
| 2a | Mount flag structure (C# structured, Lean opaque) | ~10 | Serializer workaround | LOW | Normalize mount structure |
| 2b | Mount flag parse failure | ~5 | **C# must fix** | **MEDIUM** | Fix mount parser robustness |
| 3 | Empty values in key=value | ~9 | Serializer workaround | LOW-MEDIUM | Strip empty literals in comparison |
| 4 | Single-quoted `$` in shell form | ~5 | Serializer workaround | LOW | Same as 1a |

## Priority Order for Fixes

1. **P0 (High): STOPSIGNAL `LiteralWithVariables` fix** -- One-line C# change. Real semantic bug. STOPSIGNAL supports BuildKit variable expansion, C# parser doesn't decompose them.
2. **P1 (Medium): Mount parser robustness fix** -- C# mount parser should not fail on valid mount specs. Scope TBD based on failure analysis.
3. **P2 (Low): Serializer normalizations** -- Add comparison normalizations for shell-form variable refs, mount structure, and empty values. These are test infrastructure changes, not parser changes.

## Key Architectural Insight

The fundamental tension is between **semantic faithfulness** (C# only parses what will be resolved) and **structural completeness** (Lean parses all recognizable syntax regardless of runtime behavior). Both are valid design philosophies. The Lean approach is the better spec because it's context-free -- the parser identifies structure without needing to know which instruction types support variable expansion. The C# approach is more pragmatic for consumers who only care about resolvable values.

Since Lean is the authoritative spec, the long-term direction is clear: the parser should identify all syntactic structure. The C# `CommandInstruction.ResolveVariables` override already handles the runtime semantics correctly by returning the raw string. If we ever wanted to align C# fully with Lean, we would change `ShellFormCommand` to use `LiteralWithVariables` and rely on `ResolveVariables` to suppress resolution. But that's a larger API change with no immediate consumer benefit, so it's not recommended now.
