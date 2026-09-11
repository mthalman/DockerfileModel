# Lean 4 Formal Specification

This directory contains a formal specification of the Dockerfile grammar in [Lean 4](https://lean-lang.org/), serving as an executable oracle for the C# `Valleysoft.DockerfileModel` library.

## Purpose

The Lean spec provides:

1. **Bug-finding oracle** — via differential testing, random Dockerfile inputs are parsed by both C# and Lean. Mismatches reveal C# bugs.
2. **Machine-checked proofs** — theorems about round-trip fidelity, token concatenation, variable resolution semantics, and mutation isolation.
3. **Authoritative grammar** — the Lean parser is derived from [BuildKit's Go implementation](https://github.com/moby/buildkit/tree/master/frontend/dockerfile/parser), the authoritative Dockerfile parser. When C# and Lean disagree, the Lean behavior is presumed correct.

This follows the [AWS Cedar pattern](https://www.amazon.science/publications/cedar-a-new-language-for-expressive-fast-extensible-and-analyzable-authorization): an executable formal spec alongside production code, validated by differential testing, graduated to machine-checked proofs.

## Prerequisites

Install [elan](https://github.com/leanprover/elan) (Lean version manager). The toolchain version is pinned in `lean-toolchain`.

```bash
curl https://raw.githubusercontent.com/leanprover/elan/master/elan-init.sh -sSf | sh -s -- -y
```

## Build

```bash
lake build                          # Build library + proofs
lake build DockerfileModelDiffTest  # Build the differential test CLI
lake build DockerfileModelTests     # Build SlimCheck property tests
```

## Running Differential Tests

The differential test harness lives in `src/Valleysoft.DockerfileModel.DiffTest/` (C# project). It generates random inputs with FsCheck, parses with both C# and Lean, and compares canonical JSON output:

```bash
# From the repo root:
dotnet run --project src/Valleysoft.DockerfileModel.DiffTest/ -- \
  --compare --lean-cli lean/.lake/build/bin/DockerfileModelDiffTest.exe \
  --count 180 --seed 42
```

The Lean CLI (`DockerfileModelDiffTest`) reads a Dockerfile instruction from stdin and outputs the canonical JSON token tree to stdout.

### Persistent workers

Comparison mode starts a bounded pool of long-lived Lean processes instead of
launching a process for every input. The default worker count is the smaller of
the machine's processor count and 4; override it with `--workers`:

```bash
dotnet run --project src/Valleysoft.DockerfileModel.DiffTest/ -- \
  --compare --lean-cli lean/.lake/build/bin/DockerfileModelDiffTest \
  --count 10000 --seed 42 --workers 4
```

Each worker runs the Lean CLI with `--batch`. Requests and responses are
tab-delimited, one per line. Request frames contain a request ID, the decimal
escape-character code point, and base64-encoded UTF-8 input. Response frames
contain the same request ID, `ok`, `parse-error`, or `error`, and a
base64-encoded UTF-8 body. The `error` status is reserved for malformed
requests and unknown instructions. This framing keeps newlines, tabs, JSON,
and parser errors out of the protocol structure.

### Replaying and minimizing failures

Generated cases retain their seed, stable generator name, generator-relative
case index, and escape character. Every failure prints those values and an
exact replay command using the minimized input:

```bash
dotnet run --project src/Valleysoft.DockerfileModel.DiffTest -- \
  --replay --lean-cli lean/.lake/build/bin/DockerfileModelDiffTest \
  --instruction FROM --escape-code 92 --input-base64 RlJPTSBhbHBpbmU=
```

The minimizer tries deterministic, instruction-shaped simplifications. A
candidate is accepted only when it preserves the original outcome category:
JSON mismatch, one-sided parse error, or C# parser crash. Inputs both parsers
reject are treated as agreement. Infrastructure failures such as process
exits, malformed frames, unknown instructions, and timeouts are reported
without being minimized.

### Local regression corpus

Locally authored regressions are normalized JSON fixtures in
`src/Valleysoft.DockerfileModel.DiffTest/RegressionCorpus`. They are loaded in
filename order and always run before generated cases. This corpus is separate
from the pinned upstream BuildKit corpus tracked by issue #358, but both use
the same differential execution path.

Ordinary comparison and CI runs never modify the corpus. To persist minimized
failures as idempotent, content-addressed fixtures, explicitly pass:

```bash
dotnet run --project src/Valleysoft.DockerfileModel.DiffTest/ -- \
  --compare --lean-cli lean/.lake/build/bin/DockerfileModelDiffTest \
  --count 10000 --seed 42 --promote-failures
```

Review and commit the resulting fixture changes together with the parser fix.

Pull-request CI uses seed `42`. The scheduled workflow derives a rotating but
reproducible daily seed as `UTC year * 1000 + UTC day-of-year` and prints it
before running the same corpus-first comparison.

## Design Principles

### BuildKit is the Source of Truth

The Lean parser is written to match **BuildKit's Go implementation**, not the C# library. Key behaviors derived from BuildKit:

- **Variable expansion**: expanded in ADD, COPY, ENV, EXPOSE, FROM, LABEL, STOPSIGNAL, USER, VOLUME, WORKDIR. NOT expanded in RUN, CMD, ENTRYPOINT (the shell handles it). In shell-form commands, `$` is treated as a regular character.
- **Mount flags**: `Flags` is `[]string` in Go — opaque strings. Mount value parsing (extracting `type=`, `source=`, `target=`) happens downstream in BuildKit's instructions layer, not in the Dockerfile parser. The Lean parser treats mount values as opaque literals.
- **Flag parsing**: flags are `--name=value` key-value pairs. The value is an opaque literal string.

### Token Model

The Lean `Token` type mirrors the C# hierarchy:

- **PrimitiveToken** — `string`, `whitespace`, `symbol`, `newLine`
- **AggregateToken** — `keyword`, `literal`, `identifier`, `variableRef`, `comment`, `lineContinuation`, `keyValue`, `instruction`, `construct`, `heredoc`

The `toString` function satisfies the same concatenation property as C#: for any aggregate token, `toString` equals the concatenation of its children's `toString` values.

### Parser Combinators

The parser is built from monadic combinators (`Parser/Basic.lean`, `Parser/Combinators.lean`) that mirror the Sprache combinators used in the C# `ParseHelper.cs`. The translation from C#'s `from...in...select` LINQ syntax maps directly to Lean's `do` notation.
