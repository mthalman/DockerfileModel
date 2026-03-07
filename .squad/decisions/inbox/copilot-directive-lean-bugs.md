### 2026-03-06: Lean parser bug — shell-form varefs

**By:** Matt Thalman (via Copilot)

**What:** One area where Lean's behavior does NOT match actual Dockerfile parsing and Lean needs to be fixed:

1. **Shell-form variable refs**: Lean's `shellFormCommand` parser decomposes `$VAR` and `${VAR:-default}` into structured `variableRef` tokens inside RUN, CMD, and ENTRYPOINT shell-form commands. BuildKit's Go parser (the authoritative source) does NOT expand variables in these instructions — the shell does at runtime. Lean should treat these as opaque text, matching BuildKit's actual behavior.

**Corrected — mount structure:** BuildKit's Go parser stores flag values (including `--mount`) as opaque strings in a `[]string` field. Structured mount spec parsing happens downstream in the instructions layer, NOT the parser. Therefore **Lean is CORRECT** to treat mount values as opaque literals. The C# library is the outlier here — it does instruction-level parsing inside the tokenizer. Mount structure mismatches should be handled by a C# serializer workaround, not a Lean fix.

**Why:** User explicitly stated: "i would expect lean to behave not as it is currently but more like how the actual Dockerfile parsing is working." Verified against BuildKit source: parser.go stores Flags as []string (opaque). The source of truth is always BuildKit's Go implementation.

**Impact:** Shell-form varefs is a genuine Lean bug that needs fixing to match BuildKit. Mount structure is NOT a Lean bug — Lean matches BuildKit; C# is the outlier.
