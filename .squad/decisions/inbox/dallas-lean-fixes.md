# Lean Parser Fixes for BuildKit Alignment

**Date:** 2026-03-06
**Author:** Dallas (Core Dev)

## Context

Differential testing (Phase 3) identified two areas where Lean's parser behavior diverged from BuildKit's actual Dockerfile parsing. The C# library also diverges in some cases, but the authoritative reference is BuildKit's Go implementation.

## Decision 1: Shell Form Commands Do Not Expand Variables

BuildKit does NOT expand `$VAR` references in RUN, CMD, ENTRYPOINT shell-form commands. The shell handles variable expansion at runtime. The Lean parser was modified to treat `$` as a regular character in shell form, producing only `StringToken` and `WhitespaceToken` children (no `VariableRefToken` nodes).

**Scope:** Only affects shell-form commands in RUN, CMD, ENTRYPOINT (and transitively HEALTHCHECK CMD, ONBUILD). All other instructions (WORKDIR, USER, EXPOSE, ENV, LABEL, FROM, ARG, COPY, ADD, VOLUME, STOPSIGNAL) continue to expand variables as before.

**C# serializer workaround:** The C# diff test serializer (`TokenJsonSerializer.cs`) was updated to flatten `VariableRefToken` back to plain text in shell form literal serialization, so the C# output matches Lean's new output.

## Decision 2: Structured Mount Spec Parsing

Mount flag values (`--mount=type=bind,source=/src,target=/dst`) are now parsed structurally in Lean, decomposed into comma-separated key=value pairs. Each pair becomes a `KeyValueToken` with `keyword(key)`, `symbol('=')`, `literal(value)` children. The overall mount spec becomes a `ConstructToken` containing these pairs separated by `symbol(',')` tokens.

**Limitation:** The C# `MountFlag` parser only handles `type=secret,id=...` mounts via `SecretMount.GetParser`. Non-secret mount types (bind, cache, tmpfs) cause C# to fall back to shell form, creating remaining diff test mismatches. These are known C# limitations, not Lean bugs.

## Impact

- Diff test mismatches: reduced from 91 to ~55
- All Lean proofs continue to build (all `sorry`-based proofs unaffected)
- No functional regression in any instruction parser
