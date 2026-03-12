# Differential Testing Bug Summary

Comprehensive summary of all bugs found via differential testing comparing the C# parser (`Valleysoft.DockerfileModel`) against the Lean/BuildKit parser across 108,000+ test inputs.

When the C# and Lean parsers disagree, the Lean behavior is presumed correct because it is derived from BuildKit's authoritative Go implementation (`moby/buildkit/frontend/dockerfile/parser/`).

---

## Category A: Shell-Form Whitespace Collapsing (Bugs 1--4)

**Root cause:** `ParseHelper.ArgumentListAsLiteral()` calls `CollapseLiteralTokens()`, which merges whitespace and string tokens inside shell-form commands into a single `StringToken`. BuildKit tokenizes at whitespace boundaries, producing separate `StringToken` and `WhitespaceToken` children.

**Severity:** High -- affects the most common Dockerfile instructions.

**Impact:** ~120+ mismatches per 500-input seed run (30 RUN + 27 CMD + 30 ENTRYPOINT + 36 HEALTHCHECK).

### Bug 1: RUN shell-form

- **Input:** `RUN echo hello`
- **C# output:** `literal[string("echo hello")]`
- **Lean output:** `literal[string("echo"), whitespace(" "), string("hello")]`
- **Root cause:** `CollapseLiteralTokens()` merges the argument text into one string token.
- **Affected instruction:** `RUN` (shell form only; exec form is unaffected).

### Bug 2: CMD shell-form

- **Input:** `CMD echo hello`
- **C# output:** `literal[string("echo hello")]`
- **Lean output:** `literal[string("echo"), whitespace(" "), string("hello")]`
- **Root cause:** Same `CollapseLiteralTokens()` path as Bug 1.
- **Affected instruction:** `CMD` (shell form only).

### Bug 3: ENTRYPOINT shell-form

- **Input:** `ENTRYPOINT echo hello`
- **C# output:** `literal[string("echo hello")]`
- **Lean output:** `literal[string("echo"), whitespace(" "), string("hello")]`
- **Root cause:** Same `CollapseLiteralTokens()` path as Bug 1.
- **Affected instruction:** `ENTRYPOINT` (shell form only).

### Bug 4: HEALTHCHECK CMD shell-form

- **Input:** `HEALTHCHECK CMD echo hello`
- **C# output:** `literal[string("echo hello")]` (within CMD sub-instruction)
- **Lean output:** `literal[string("echo"), whitespace(" "), string("hello")]`
- **Root cause:** Same `CollapseLiteralTokens()` path, triggered through the CMD sub-instruction within HEALTHCHECK.
- **Affected instruction:** `HEALTHCHECK` (shell-form CMD sub-instruction only).

---

## Category B: ONBUILD Recursive Parsing (Bugs 5--6)

**Root cause:** `OnBuildInstruction.GetArgsParser()` calls `CreateInstruction(text, escapeChar)` to recursively parse the ONBUILD body as a structured sub-instruction. BuildKit treats the body as opaque literal text.

**Severity:** High -- every ONBUILD variant is affected (~50 mismatches per seed run).

### Bug 5: ONBUILD with any inner instruction

- **Input:** `ONBUILD LABEL key=value`
- **C# output:** nested `instruction[keyword("LABEL"), ...]` with fully structured inner tokens
- **Lean output:** `literal[string("LABEL"), whitespace(" "), string("key=value")]`
- **Root cause:** C# recursively invokes the full instruction parser on the ONBUILD body.
- **Affected instructions:** All valid ONBUILD inner instructions (RUN, CMD, ENTRYPOINT, COPY, ADD, ENV, EXPOSE, VOLUME, USER, WORKDIR, LABEL, STOPSIGNAL, HEALTHCHECK, SHELL, ARG).

### Bug 6: ONBUILD with exec-form

- **Input:** `ONBUILD CMD ["echo","hi"]`
- **C# output:** Fully parsed JSON array with structured exec-form tokens.
- **Lean output:** Opaque literal text: `string("CMD"), whitespace(" "), string("[\"echo\",\"hi\"]")`
- **Root cause:** Same recursive parsing as Bug 5; the exec-form JSON structure is fully decomposed by the C# parser but left as literal text by BuildKit.

---

## Category C: Heredoc Structural Differences (Bugs 7--11)

**Severity:** Medium-High -- heredoc syntax (`<<EOF`) is increasingly common in modern Dockerfiles.

### Bug 7: Heredoc marker tokenization

- **Input:** `RUN <<EOF`
- **C# output:** `symbol(<) + symbol(<) + identifier("EOF")`
- **Lean output:** `string("<<EOF")`
- **Root cause:** C# decomposes the `<<` heredoc prefix into individual symbol tokens and treats the delimiter as an identifier. BuildKit treats the entire marker as a single opaque string.

### Bug 8: Heredoc body kind

- **Input:** `RUN <<EOF\ncontent\nEOF`
- **C# output:** Uses `construct` kind for the heredoc body.
- **Lean output:** Uses `heredoc` kind for the heredoc body.
- **Root cause:** C# wraps heredoc content in a generic `construct` aggregate token rather than a dedicated `heredoc`-typed token.

### Bug 9: Heredoc body lines merged

- **Input:** `RUN <<EOF\nline1\nline2\nEOF`
- **C# output:** Single `string("line1\nline2\n")`
- **Lean output:** `string("line1\n"), string("line2\n")` (one string token per line)
- **Root cause:** C# concatenates all heredoc body lines into a single string token. BuildKit emits one string token per line.

### Bug 10: Heredoc tab-stripping

- **Input:** `RUN <<-EOF\n\tcontent\n\tEOF`
- **C# output:** Preserves leading tabs: `string("\tcontent\n")`
- **Lean output:** Strips leading tabs: `string("content\n")` (matching BuildKit behavior)
- **Root cause:** C# does not implement the `<<-` tab-stripping semantics. BuildKit strips leading tabs from heredoc body lines when the delimiter is preceded by `-`.

### Bug 11: Heredoc quoted delimiter

- **Input:** `RUN <<"EOF"\ncontent\nEOF`
- **C# output:** Preserves quote symbols: `symbol(") + identifier(EOF) + symbol(")`
- **Lean output:** Strips quote symbols; delimiter treated as `EOF` (unquoted).
- **Root cause:** C# emits the quoting characters as separate symbol tokens. BuildKit strips the quotes from the delimiter (the quotes only serve to disable variable expansion inside the heredoc body).

---

## Category D: Empty Flag Value Absorption (Bugs 12--13)

**Severity:** High -- semantic corruption and parser crashes.

### Bug 12: Empty flag value in COPY/ADD/RUN

- **Input:** `COPY --from= src.txt /app/`
- **C# output:** Absorbs `src.txt` as the flag value: `--from=src.txt` with `/app/` as the sole file argument (semantic corruption).
- **Lean output:** Treats `--from=` as an empty-valued flag; `src.txt` and `/app/` are both file arguments.
- **Root cause:** The C# flag parser does not distinguish between an empty value after `=` and a missing value. When the value is empty, it absorbs the next whitespace-delimited token as the flag value.
- **Also affects:**
  - `COPY --chown=` (absorbs next argument as owner)
  - `COPY --chmod=` (absorbs next argument as mode)
  - `ADD --chown=` (absorbs next argument as owner)
  - `ADD --checksum=` (absorbs next argument as checksum)
  - `RUN --network=` (absorbs next argument as network)
  - `RUN --security=` (absorbs next argument as security mode)

### Bug 13: FROM --platform= crash

- **Input:** `FROM --platform= alpine`
- **C# output:** Throws a parse error (unhandled exception).
- **Lean output:** Gracefully treats `--platform=` as an empty-valued flag; `alpine` is the image name.
- **Root cause:** The C# `FROM` instruction parser does not handle an empty platform value after `=`, leading to a parse failure rather than graceful degradation.

---

## Category E: Miscellaneous (Bug 14)

### Bug 14: Newlines in quoted strings

- **Input:** `ENV MY_VAR="hello\nworld"` (with actual newline character, not `\n` escape sequence)
- **C# output:** Accepts the newline inside the quoted string and parses it as a single value spanning two lines.
- **Lean output:** Treats the newline as an instruction terminator regardless of quoting; the second line is parsed as a separate construct.
- **Severity:** Medium -- uncommon in practice but causes silent parsing differences when encountered.
- **Root cause:** C# quoted-string parsing allows raw newline characters within double quotes. BuildKit (and Lean) treat newlines as unconditional line terminators.

---

## Summary Table

| Bug | Category | Instruction(s) | Severity | Est. Mismatches/500 |
|-----|----------|----------------|----------|---------------------|
| 1 | A: Whitespace Collapsing | RUN (shell) | High | ~30 |
| 2 | A: Whitespace Collapsing | CMD (shell) | High | ~27 |
| 3 | A: Whitespace Collapsing | ENTRYPOINT (shell) | High | ~30 |
| 4 | A: Whitespace Collapsing | HEALTHCHECK CMD (shell) | High | ~36 |
| 5 | B: ONBUILD Recursive | ONBUILD (all inner) | High | ~50 |
| 6 | B: ONBUILD Recursive | ONBUILD (exec-form) | High | (included in 5) |
| 7 | C: Heredoc | RUN/COPY/ADD (heredoc) | Medium-High | TBD |
| 8 | C: Heredoc | RUN/COPY/ADD (heredoc) | Medium-High | TBD |
| 9 | C: Heredoc | RUN/COPY/ADD (heredoc) | Medium-High | TBD |
| 10 | C: Heredoc | RUN/COPY/ADD (heredoc) | Medium-High | TBD |
| 11 | C: Heredoc | RUN/COPY/ADD (heredoc) | Medium-High | TBD |
| 12 | D: Empty Flag Absorption | COPY/ADD/RUN | High | TBD |
| 13 | D: Empty Flag Crash | FROM | High | TBD |
| 14 | E: Quoted Newlines | ENV (and others) | Medium | TBD |
