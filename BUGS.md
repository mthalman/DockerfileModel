# Bugs Found via Differential Testing (FsCheck Generators)

Discovered by expanding FsCheck generators with edge cases (exotic variable modifiers,
empty exec forms, chown user:group, numeric --from, line continuations in flags, etc.)
and running differential tests between the C# parser and the Lean parser.

Lean follows BuildKit semantics and is treated as the reference implementation.

---

## C# Parser Bugs

### Bug 1: HEALTHCHECK `--start-interval` flag not supported

**Severity:** Error (crash)
**Input:** `HEALTHCHECK --start-interval=272m CMD echo hello`
**Error:** `Parsing failure: unexpected '-'; expected C or N`

The C# parser does not have a `StartIntervalFlag` parser. The `--start-interval` flag
was added to Docker in a later version and is a valid HEALTHCHECK option, but the C#
library only supports `--interval`, `--timeout`, `--start-period`, and `--retries`.

Also affects inputs with all 5 flags combined:
`HEALTHCHECK --interval=283ms --timeout=138ms --start-period=213s --start-interval=90m --retries=8 CMD ...`

---

### Bug 2: CMD/ENTRYPOINT/RUN exec form with empty string element crashes

**Severity:** Error (crash)
**Input:** `CMD ["", "hello"]`
**Error:** `'tokens' must contain at least one element. (Parameter 'tokens')`

The C# parser crashes when parsing an exec form JSON array that contains an empty
string element (`""`). Empty strings are valid in Docker exec form — they represent
empty arguments. The crash occurs in the token construction code that requires at
least one child token, but an empty quoted string has no content tokens.

Also affects: `ENTRYPOINT ["", "/app/run"]`, `RUN ["", "-c"]`

---

### Bug 3: CMD/ENTRYPOINT empty exec form `[]` not supported

**Severity:** Error (crash)
**Input:** `CMD []`, `ENTRYPOINT []`
**Error:** `Parsing failure: unexpected ']'; expected "`

The C# parser rejects empty exec form arrays. Docker allows `CMD []` and
`ENTRYPOINT []` as valid instructions (they reset the default command/entrypoint).
The C# JSON array parser requires at least one quoted string element.

Note: `RUN []` does NOT crash in C# — it falls through to shell form and parses
`[]` as literal text. This is a MISMATCH (Bug 4) rather than a crash.

---

### Bug 4: `RUN []` parsed as shell form instead of exec form

**Severity:** Mismatch
**Input:** `RUN []`
**C#:** `literal` containing string `[]` (shell form interpretation)
**Lean:** `symbol("[")` + `symbol("]")` (exec form interpretation)

When `RUN` encounters `[]`, the C# parser falls through to shell form and treats
`[]` as opaque text. Lean correctly parses it as an empty exec form array.

---

### Bug 5: Variable ref modifiers `#`, `##`, `%`, `%%`, `/`, `//` not tokenized as symbols

**Severity:** Mismatch (affects all instruction types that support variable refs)
**Input:** `STOPSIGNAL ${bb##uvwlqy}`, `USER ${y01_c_10%%nde1}`, `VOLUME ${ccb/omn/replace}`, etc.

C# includes the modifier characters as part of the literal value text:
```json
{"type":"aggregate","kind":"literal","children":[{"type":"primitive","kind":"string","value":"##uvwlqy"}]}
```

Lean correctly decomposes the modifier into separate symbol tokens:
```json
{"type":"primitive","kind":"symbol","value":"#"},{"type":"primitive","kind":"symbol","value":"#"},{"type":"aggregate","kind":"literal","children":[{"type":"primitive","kind":"string","value":"uvwlqy"}]}
```

Affected modifiers:
- `#` (prefix removal, shortest match)
- `##` (prefix removal, longest match)
- `%` (suffix removal, shortest match)
- `%%` (suffix removal, longest match)
- `/` (substitution, first match)
- `//` (substitution, all matches)

All modifiers in the `ValidModifiers` list are affected. The C# `VariableRefToken`
parser correctly identifies the modifier and value, but the serialization emits
the modifier chars as part of the value's string content rather than as distinct
symbol tokens.

---

### Bug 6: COPY/ADD `--from` with variable reference not parsed as flag

**Severity:** Mismatch
**Input:** `COPY --from=${za000} src dst`, `COPY --from=$cca1 src dst`

C# treats `--from=${za000}` as a regular literal argument (the flag is not recognized):
```json
{"type":"aggregate","kind":"literal","children":[{"type":"primitive","kind":"string","value":"--from="},{"type":"aggregate","kind":"variableRef",...}]}
```

Lean correctly parses it as a keyValue flag:
```json
{"type":"aggregate","kind":"keyValue","children":[symbol("-"),symbol("-"),keyword("from"),symbol("="),literal(variableRef)]}
```

Root cause: The C# `StageName` parser (used for `--from` flag values) only accepts
identifiers starting with a lowercase letter. Variable references like `${VAR}` do
not match the StageName grammar, so the entire `--from=...` flag falls through to
literal parsing.

---

### Bug 7: COPY/ADD `--from` with numeric stage index not parsed as flag

**Severity:** Mismatch
**Input:** `COPY --from=0 src dst`, `COPY --from=4 src dst`

Same root cause as Bug 6. The C# `StageName` parser requires the first character
to be a lowercase letter, so numeric stage indices (valid in Docker to reference
build stages by index) are rejected.

C# output: `literal("--from=4")` (treats entire flag as a literal argument)
Lean output: `keyValue(symbol("-"),symbol("-"),keyword("from"),symbol("="),literal("4"))`

---

### Bug 8: COPY/ADD `--chown=user:group` over-tokenized as nested keyValue

**Severity:** Mismatch
**Input:** `COPY --chown=z1bc0_:a_1 src dst`

C# decomposes the chown value into a nested `keyValue` with separate user and group
literals separated by a `:` symbol:
```json
{"type":"aggregate","kind":"keyValue","children":[
  literal("z1bc0_"), symbol(":"), literal("a_1")
]}
```

Lean treats the entire value as a single literal:
```json
{"type":"aggregate","kind":"literal","children":[string("z1bc0_:a_1")]}
```

The C# `ChangeOwnerFlag` parser specifically decomposes `user:group` into structured
tokens. BuildKit treats the value as an opaque string and delegates parsing to the
container runtime. The extra decomposition creates a structural mismatch.

---

## Lean Parser Bugs

### Bug 9: COPY/ADD line continuation between multiple flags fails

**Severity:** Error (crash)
**Input:** `COPY --from=stage \<newline>  --chown=user src dst`
**Input:** `ADD --chown=owner \<newline>  --link src dst`
**Error:** `Parse error: failed to parse COPY instruction`

The Lean parser cannot handle line continuations between COPY/ADD flags. When
a line continuation appears after one flag and before another, the parser fails
to continue parsing the next flag.

Root cause: The COPY/ADD flag parsers use `argTokens` but don't properly handle
line continuations in the gap between consecutive flags. The `many` combinator
over the flag parser can't recover across a line continuation boundary.

Also occurs with backtick line continuations (`ADD --chown=owner \`<newline>  --link`).

---

### Bug 10: RUN shell form with trailing whitespace in line continuation

**Severity:** Mismatch
**Input:** `RUN echo hello \   <newline>  && echo world`

C# correctly treats `\` + spaces + newline as a line continuation and includes the
text after the continuation as part of the command:
```
literal("echo hello \ <spaces> <newline> <indent> && echo world")
```

Lean does NOT recognize the line continuation — it treats `\` as a regular character,
the spaces as whitespace, and the newline terminates the instruction:
```
literal("echo hello \ <spaces>"), newLine
```

In Docker, `\` followed by optional whitespace followed by a newline IS a valid
line continuation. The whitespace between the escape char and the newline is
permitted. The Lean `lineContinuationParser` should handle this case (it parses
`escapeChar + optional ws + newline`) but the `shellFormCommand` parser appears
to be matching the `\` as an escaped character before `lineContinuationParser`
gets a chance to try.

Root cause likely in the priority ordering of alternatives in `shellFormCommand`:
the `escapedChar` parser matches `\ ` (backslash-space) before `lineContinuationParser`
can try `\ <spaces> <newline>`.

---

## Summary

| # | Issue | Parser | Type | Description |
|---|-------|--------|------|-------------|
| 1 | [#202](https://github.com/mthalman/DockerfileModel/issues/202) | C# | Error | HEALTHCHECK `--start-interval` flag not supported |
| 2 | [#203](https://github.com/mthalman/DockerfileModel/issues/203) | C# | Error | Exec form with empty string `""` element crashes |
| 3 | [#204](https://github.com/mthalman/DockerfileModel/issues/204) | C# | Error | Empty exec form `[]` not supported in CMD/ENTRYPOINT |
| 4 | [#205](https://github.com/mthalman/DockerfileModel/issues/205) | C# | Mismatch | `RUN []` parsed as shell form instead of exec form |
| 5 | [#206](https://github.com/mthalman/DockerfileModel/issues/206) | C# | Mismatch | Variable ref modifiers `#`/`##`/`%`/`%%`/`/`/`//` not tokenized as symbols |
| 6 | [#207](https://github.com/mthalman/DockerfileModel/issues/207) | C# | Mismatch | COPY/ADD `--from` with variable ref not parsed as flag |
| 7 | [#208](https://github.com/mthalman/DockerfileModel/issues/208) | C# | Mismatch | COPY/ADD `--from` with numeric stage index not parsed as flag |
| 8 | [#209](https://github.com/mthalman/DockerfileModel/issues/209) | C# | Mismatch | COPY/ADD `--chown=user:group` over-tokenized as nested keyValue |
| 9 | [#210](https://github.com/mthalman/DockerfileModel/issues/210) | Lean | Error | Line continuation between COPY/ADD flags fails |
| 10 | [#211](https://github.com/mthalman/DockerfileModel/issues/211) | Lean | Mismatch | Shell form `\<spaces><newline>` not treated as continuation |

**Total: 10 distinct bugs (8 C#, 2 Lean)**
