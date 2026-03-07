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

## Project Structure

```
lean/
  lakefile.lean                    # Lake build configuration
  lean-toolchain                   # Pinned Lean 4 version (v4.27.0)
  DockerfileModel.lean             # Root import file
  DockerfileModel/
    Token.lean                     # Token inductive type (mirrors C# hierarchy)
    Instruction.lean               # 18 instruction name variants
    Dockerfile.lean                # Dockerfile structure
    Json.lean                      # Canonical JSON serialization
    Main.lean                      # Differential test CLI entry point
    VariableResolution.lean        # Variable resolution semantics
    Scoping.lean                   # ARG scoping rules (global vs per-stage)
    Parser/
      Basic.lean                   # Core parser monad and primitives
      Combinators.lean             # Parser combinators (many, or', satisfy, etc.)
      DockerfileParsers.lean       # Shared parsers (literals, variable refs, shell form, etc.)
      ExecForm.lean                # Exec form parser (JSON array syntax)
      Flags.lean                   # Flag parsers (--flag=value, boolean flags)
      Heredoc.lean                 # Heredoc syntax support
      Instructions/                # Per-instruction parsers (18 files, one per type)
        From.lean, Run.lean, Cmd.lean, Copy.lean, Add.lean, Env.lean,
        Arg.lean, Expose.lean, Entrypoint.lean, Healthcheck.lean,
        Label.lean, Maintainer.lean, Onbuild.lean, Shell.lean,
        Stopsignal.lean, User.lean, Volume.lean, Workdir.lean
    Proofs/
      TokenConcat.lean             # Token concatenation theorem
      RoundTrip.lean               # Round-trip fidelity proofs
      VariableResolution.lean      # Variable modifier semantics proofs
      Capstone.lean                # Full round-trip + mutation isolation proofs
    Tests/
      ParserTests.lean             # Parser unit tests
      SlimCheck.lean               # Property-based tests (mirrors C# FsCheck tests)
```

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

## Known C# Divergences

When differential testing finds mismatches, the C# side gets a GitHub issue and a serializer workaround in `TokenJsonSerializer.cs`. Current known divergences:

| Issue | Description | Status |
|-------|-------------|--------|
| [#199](https://github.com/mthalman/DockerfileModel/issues/199) | STOPSIGNAL: C# doesn't parse variable refs | Serializer workaround |
| [#200](https://github.com/mthalman/DockerfileModel/issues/200) | RUN: C# MountFlag parser fails on various mount types | Generators disabled |
| [#201](https://github.com/mthalman/DockerfileModel/issues/201) | ENV/LABEL: C# emits empty LiteralToken for `key=` | Serializer workaround |
