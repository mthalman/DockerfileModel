# Project Context

- **Owner:** Matt Thalman
- **Project:** Valleysoft.DockerfileModel — a .NET library for parsing and generating Dockerfiles with full fidelity. Parsed content round-trips character-for-character, including whitespace.
- **Stack:** C# (.NET Standard 2.0 / .NET 6.0), Sprache parser combinator, xUnit, NuGet
- **Created:** 2026-03-04

## Key Architecture

- Token-based model: Dockerfile > DockerfileConstruct > Instruction > Token
- Parser: Sprache-based combinators in ParseHelper.cs
- Builder: DockerfileBuilder for fluent construction
- Stages: StagesView/Stage for multi-stage build organization
- ImageName: Parses image references into components
- Variable resolution: Dockerfile.ResolveVariables() with optional inline update
- Tests: xUnit with Theory/InlineData, per-instruction test files

## Key Paths

- `src/Valleysoft.DockerfileModel/` — library (netstandard2.0, net6.0)
- `src/Valleysoft.DockerfileModel.Tests/` — tests (net8.0)
- `src/Directory.Build.props` — shared MSBuild properties

## Learnings


## Core Context

### Architectural Analysis & Formal Verification (2026-03-05 to 2026-03-06)

Conducted comprehensive design review and formal verification framework development:
- **Refactor branch analysis**: Reviewed flag class hierarchy, instruction patterns, architecture soundness
- **Lean 4 formal verification**: Implemented 5 complete phases including token model spec, parser combinators, variable resolution proofs, round-trip preservation proofs
- **Phase completion**: Delivered stage validation (Phase A), shared parser infrastructure (Phase B), 15 instruction parsers across phases C-D
- **Issue #176 analysis**: Identified trailing newline loss in STOPSIGNAL/MAINTAINER/SHELL instructions

**Established patterns**: Boolean flags use AggregateToken; key-value flags use KeyValueToken; optional flag properties follow 3-tier pattern. Lean formalization provides high-confidence verification of parser correctness.

**Status**: Differential testing identifies ~100-125 mismatches per 1800 inputs (5-7% rate), all categorized and prioritized for remediation.

---

## Recent Work

- `src/Valleysoft.DockerfileModel/ParseHelper.cs` — C# reference (877 lines)
- `src/Valleysoft.DockerfileModel/DockerfileParser.cs` — C# top-level parser reference
- `src/Valleysoft.DockerfileModel/Tokens/VariableRefToken.cs` — variable ref parser reference
- `src/Valleysoft.DockerfileModel/Tokens/LineContinuationToken.cs` — line continuation parser reference

### 2026-03-05T20:00:00Z — Phase 2 Parser Combinator Architecture Adopted

Team update (2026-03-05T20:00:00Z): Phase 2 Lean 4 parser combinator architecture design completed and adopted by team. 8 architecture decisions finalized, decision document (21.3KB) merged to .squad/decisions.md. Dallas implemented parser combinator library (1462 lines, 6 modules); Lambert created test suite (1180 lines, 48 active tests + 18 stubs); both follow architecture recommendations. Orchestration logs and session log created.

**Architecture (8 decisions) highlights:**
- Use `Lean.Parsec` built-in (zero external dependencies)
- Produce existing `Token` type directly, not intermediate AST
- Flat module structure under `Parser/`, one file per concern
- Systematic Sprache → Lean combinator mapping
- Thread escape char as explicit `ParserConfig` parameter
- Bottom-up round-trip theorem strategy, FROM first
- Implementation order: Basic.lean → Tokens.lean → From/Arg with proofs
- No lakefile changes; auto-discovery sufficient

**Deliverables from this team session:**
- **Ripley:** 8 architecture decisions, Lean parser design specification
- **Dallas:** 6 Lean modules (Basic, Combinators, DockerfileParsers, FromParser, ArgParser, RoundTripProofs), parseFrom/parseArg API
- **Lambert:** ParserTests.lean with 48 active token-tree tests + 18 parser stubs

**Status:** Foundation complete; ready for Phase 2 execution (remaining 16 instructions, expanded proof work).
Team update (2026-03-06T00:12:22Z): Phase 5 Capstone proofs completed: 12 new theorems in Capstone.lean, token_concat_length fixed in RoundTrip.lean, proof coverage documented. Total: 55 proved, 4 documented sorries. Build: 19 jobs, 0 errors. — decided by Dallas

### 2026-03-06 — Differential Testing Mismatch Analysis (4 categories, 91+ mismatches)

**Branch:** grammar

**Analysis completed for Dallas's generator expansion findings.** 91+ mismatches across 4 categories analyzed against BuildKit authoritative behavior.

**Key findings:**

1. **STOPSIGNAL variable refs (HIGH severity, P0):** C# uses `LiteralToken` (no variable decomposition) but BuildKit explicitly expands variables in STOPSIGNAL. This is a real C# bug. Fix: one-line change from `LiteralToken(escapeChar, ...)` to `LiteralWithVariables(escapeChar)` in `StopSignalInstruction.GetArgsParser`. 38 of 91 mismatches.

2. **Shell-form variable refs (LOW severity):** RUN/CMD/ENTRYPOINT shell-form commands: C# treats `$VAR` as opaque text (`canContainVariables: false`), Lean decomposes them. Both are valid -- C# is semantically faithful (BuildKit doesn't expand vars in these), Lean is structurally complete. Resolution: serializer workaround, flatten Lean variableRef tokens for comparison.

3. **Mount flag structure (MEDIUM severity for parse failures):** Lean uses opaque `flagParser "mount"`, C# uses structured `SecretMount.GetParser`. The opaque approach is more robust. Sub-pattern (b) where C# mount parser fails on valid inputs is a real bug needing fix.

4. **Empty values in key=value (LOW-MEDIUM):** C# synthesizes empty `literal[""]` via `.GetOrElse(new LiteralToken(""))`, Lean omits the value token. Resolution: serializer workaround (strip empty literals from C# tree before comparison).

5. **Single-quoted `$` in shell form (LOW):** Subset of category 1. Lean's shellFormCommand doesn't model shell quoting, so `$1` inside `'{print $1}'` becomes a variableRef. Acceptable limitation.

**Architectural insight:** Fundamental tension between semantic faithfulness (C# parses only what will be resolved) vs structural completeness (Lean parses all recognizable syntax). Lean's context-free approach is the better spec. C#'s `CommandInstruction.ResolveVariables` override correctly handles runtime semantics regardless.

**Priority order:** P0 = STOPSIGNAL fix (C#), P1 = mount parser robustness (C#), P2 = serializer normalizations (test infra).

**Decision document:** `.squad/decisions/inbox/ripley-mismatch-analysis.md`

## Team update (2026-03-09T12:49:57Z): Copilot review workflow directive
Added to shared decisions: Copilot PR review workflow (add reviewer via API, respond per comment, re-request until resolved). Note: `gh pr edit --add-reviewer` does NOT work for bots; must use API directly with bot name `copilot-pull-request-reviewer[bot]`. — decided by Scribe

---

## Team update (2026-03-10T12:30:00Z): Heredoc architecture design finalized and merged to decisions.md

Designed comprehensive heredoc token architecture for Issue #245 (RUN, COPY, ADD instructions). Architecture decision merged to .squad/decisions.md.

**Token Model:** `HeredocMarkerToken` (inline, contains `<<delimiter` with optional `<<-` chomp and EOF redirect) paired with `HeredocBodyToken` (sequential, contains body lines and closing delimiter). Semantic `Heredoc` wrapper encapsulates marker+body into semantic unit for instruction consumption.
**Token Model:** `HeredocMarkerToken` (inline, contains `<<delimiter` with optional `<<<` chevron and EOF redirect) paired with `HeredocBodyToken` (sequential, contains body lines and closing delimiter). Semantic `Heredoc` wrapper encapsulates marker+body into semantic unit for instruction consumption.

**Architecture Layers:**
1. Parser combinators in ParseHelper (`HeredocMarker`, `HeredocBody`, `Heredoc`)
2. Instruction integration via `RunInstruction.HeredocTokens` and `FileTransferInstruction.HeredocTokens`
3. High-level accessors `Heredocs` property returning `IEnumerable<string>`

**Implementation Plan:** 10-step progression produced for Dallas covering token class creation, integration with argument parsing, and full instruction support.

**Coordination:** Decision locked for review before implementation lands on dev. Lambert's test suite (160 test cases) provides executable specification. Dallas continues implementation on `squad/245-heredoc-syntax-heredocs-property` branch.
