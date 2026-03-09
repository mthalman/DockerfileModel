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

## Key Paths

- `src/Valleysoft.DockerfileModel/` — library (netstandard2.0, net6.0)
- `src/Valleysoft.DockerfileModel.Tests/` — tests (net8.0)
- `src/Directory.Build.props` — shared MSBuild properties

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

## Core Context

### Comprehensive Implementation & Verification Sprint (2026-03-04 to 2026-03-08)

Executed multi-phase development and verification project:

**Phase 1 — Flag Implementations** (Issues #115-#116): Implemented COPY --link (boolean flag pattern), RUN --network/--security (key-value pattern), ADD --checksum/--keep-git-dir/--link (mixed patterns). Established reusable token patterns and property architecture.

**Phase 2 — Code Quality & Analysis**: Code smell analysis across 47 files, library cleanup (L1-L3 refactoring), 693+ tests passing.

**Phase 3 — Testing Infrastructure**: FsCheck property-based testing (P0-1+P0-2), differential testing expansion (1800+ varied inputs per seed), test suite grew from 532 to 693+ tests.

**Phase 4 — Formal Verification**: 5-phase Lean 4 verification (token model, parser combinators, variable resolution proofs, round-trip preservation, capstone). Delivered 15 instruction parsers with formal correctness guarantees.

**Phase 5 — Differential Analysis**: Identified ~100-125 mismatches per 1800 inputs (5-7% rate) between C# and Lean. Categorized into 4 actionable areas with prioritized fix recommendations (P0-P2).

**Key architectural patterns**: Boolean flags → AggregateToken; key-value flags → KeyValueToken; optional flags → 3-tier property pattern.

**Current status**: All phases complete. Differential testing operational. Issue #176 (trailing newlines) fixed. 10 GitHub issues (#238-#247) filed for prioritized remediation.

---


## Team update (2026-03-09T12:49:57Z): Copilot review workflow directive
Added to shared decisions: Copilot PR review workflow (add reviewer via API, respond per comment, re-request until resolved). Note: `gh pr edit --add-reviewer` does NOT work for bots; must use API directly with bot name `copilot-pull-request-reviewer[bot]`. — decided by Scribe
