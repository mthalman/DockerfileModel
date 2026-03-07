# Generator Expansion Findings — Differential Test Mismatches

**Date:** 2026-03-06
**Author:** Dallas (Core Dev)
**Context:** Expanded FsCheck generators in `DockerfileArbitraries.cs` to produce more varied inputs, then ran differential tests comparing C# and Lean parser output.

## Summary

After expanding generators to cover variable references, mount flags, empty values, dotted/hyphenated label keys, multi-source file transfers, varied exec forms, and more, the differential tests now find **~100-125 mismatches per 1800 inputs** (5-7% mismatch rate) across three different seeds (42, 123, 999). Previously, with shallow generators, 0/900 mismatches were found.

## Mismatch Categories

### 1. Variable references in shell-form commands (majority of mismatches)

**Affected types:** RUN, CMD, ENTRYPOINT, STOPSIGNAL, HEALTHCHECK, ONBUILD
**Count:** ~60-70% of all mismatches

**Pattern:** The C# parser treats `$VAR` and `${VAR:-default}` as plain string tokens inside shell-form instruction arguments. The Lean parser correctly identifies them as structured `variableRef` tokens with sub-components (braces as symbols, modifier symbols, etc.).

**Examples:**
- `RUN echo ${abc}` — C# sees `"${abc}"` as a string; Lean sees `variableRef["{", "abc", "}"]`
- `STOPSIGNAL $SIGNAL` — C# sees `"$SIGNAL"` as a string; Lean sees `variableRef["SIGNAL"]`
- `CMD set ${x:-default}` — C# sees `"${x:-default}"` as a string; Lean sees `variableRef["{", "x", ":", "-", literal["default"], "}"]`

**Root cause:** The C# `shellFormCommand` parser uses `LiteralWithVariables` only in certain instruction types (WORKDIR, LABEL, ENV, ARG, USER, EXPOSE, FROM). For shell-form commands in RUN, CMD, ENTRYPOINT, and ONBUILD, the C# parser does NOT decompose variable references — it treats the entire command as an opaque literal. The Lean parser uses `shellFormCommand` which does decompose variable references everywhere.

### 2. Mount flag token structure (RUN --mount)

**Affected types:** RUN
**Count:** ~15-20% of mismatches

**Pattern:** Two sub-patterns:

**(a) C# structured, Lean opaque:** When the C# parser successfully recognizes `--mount=...`, it produces a deeply structured token tree with nested `keyValue` tokens for `type=bind`, `source=x`, `target=/y`, etc. The Lean parser treats the mount value as a single opaque `literal` string.

**(b) C# fails to parse mount:** When `--mount=type=tmpfs,target=/path` or `--mount=type=cache,target=/path` is combined with `--network=...`, the C# parser sometimes fails to match the mount flag and treats the entire `--mount=... --network=... command` as a plain literal (shell form). The Lean parser correctly identifies both `--mount` and `--network` as structured `keyValue` flag tokens.

**Root cause:** The C# `MountFlag` parser has a structured sub-parser for mount specs (with `Mount` being a `construct` token type). The Lean parser uses a generic `flagParser "mount"` that treats the value as an opaque literal. Additionally, the C# mount parser may fail on certain mount type values, causing the fallback to shell-form parsing.

### 3. Empty values in key=value pairs (LABEL, ENV)

**Affected types:** LABEL, ENV
**Count:** ~5-8% of mismatches

**Pattern:** For `LABEL key=` and `ENV key=` (empty value after `=`), the C# parser includes an empty `literal` child token (with value `""`), while the Lean parser omits the value token entirely (the `keyValue` token has only `identifier` and `symbol(=)` children, with no literal).

**Examples:**
- `LABEL mykey=` — C# has `keyValue[identifier["mykey"], symbol["="], literal[""]]`; Lean has `keyValue[identifier["mykey"], symbol["="]]`
- `ENV myvar=` — Same pattern

**Root cause:** The C# parser uses `LiteralWithVariables().Optional()` which, on empty input, produces `Some(emptyLiteral)` rather than `None`. The Lean parser's `Parser.optional (literalWithVariables ...)` returns `none` when there's no value to consume, resulting in no child token.

### 4. Single-quoted strings with dollar signs (minor)

**Affected types:** RUN (commands with `awk '{print $1}'`)
**Count:** ~5% of mismatches

**Pattern:** Commands containing single-quoted text with `$` inside (e.g., `awk '{print $1}'`) are split differently. The C# parser treats `'{print` and `$1}'` as separate string tokens. The Lean parser handles the `$1` within the single quotes as a variable reference token.

## Mismatch Counts by Type (seed 42, 1800 inputs)

| Instruction | Mismatches | Primary Cause |
|-------------|-----------|---------------|
| STOPSIGNAL  | 38        | Variable refs |
| RUN         | 26        | Variable refs + mount structure |
| HEALTHCHECK | 12        | Variable refs in CMD |
| CMD         | 8         | Variable refs |
| ENTRYPOINT  | 5         | Variable refs |
| ONBUILD     | 7         | Variable refs (in trigger text) |
| ENV         | 5         | Empty values |
| LABEL       | 4         | Empty values |

## Recommendation

These are real parser behavior differences, not bugs in the generators. The three actionable items:

1. **Variable refs in shell form** — Decide whether the C# parser should decompose `$VAR`/`${VAR}` inside shell-form commands. If yes, update the C# `shellFormCommand` to use `LiteralWithVariables`. If no, update the Lean `shellFormCommand` to NOT decompose them. Either way, they should agree.

2. **Mount flag structure** — Decide whether mount values should be structured (`type=x,source=y`) or opaque. The C# structured approach is richer but means the token trees will differ. Standardize one way.

3. **Empty value handling** — Decide whether `key=` should include an empty literal token or omit the value. Small difference but affects token tree equivalence.
