# Project Context

- **Owner:** Matt Thalman
- **Project:** Valleysoft.DockerfileModel — a .NET library for parsing and generating Dockerfiles with full fidelity. Parsed content round-trips character-for-character, including whitespace.
- **Stack:** C# (.NET Standard 2.0 / .NET 6.0), Sprache parser combinator, xUnit, NuGet
- **Created:** 2026-03-04

## Key Test Patterns

- xUnit with Theory/InlineData for data-driven tests
- Each instruction type has a corresponding test file (e.g., FromInstructionTests.cs)
- ScenarioTests.cs has integration-level examples
- TestHelper.cs provides shared utilities like ConcatLines()
- Round-trip fidelity: parse → ToString() must match original input exactly

## Key Paths

- `src/Valleysoft.DockerfileModel.Tests/` — test project (net8.0)
- `src/Valleysoft.DockerfileModel/` — library under test

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-04 — COPY --link tests (Issue #115)

## Core Context

### Test Infrastructure & Feature Testing (2026-03-04 to 2026-03-06)

Implemented comprehensive test coverage for new Dockerfile instruction options:
- **COPY --link** (Issue #115): Boolean flag tests in CopyInstructionTests
- **RUN --network/--security** (Issue #116): KeyValueToken flag tests, 25 new tests
- **ADD --checksum/--keep-git-dir/--link** (Issue #103): 42 new comprehensive tests
- **Property-based testing**: Added FsCheck infrastructure (P0-1 + P0-2 generators)
- **Differential testing expansion**: Generators producing 1800+ varied inputs (variables, mounts, empty values, label keys, multi-source transfers, exec forms)
- **Test suite status**: 599 → 693 tests passing across all phases

**Key test patterns**: Base class inheritance (FileTransferInstructionTests generic), xUnit Theory/InlineData, round-trip fidelity validation, property-based edge case coverage.

**Differential testing results**: Identified ~100-125 mismatches per 1800 inputs (5-7% rate). Categorized into: variable refs in shell form (65%), mount structure (20%), empty values (10%), single-quoted $ (5%).

---

## Recent Work


**What was updated:** Updated `DockerfileArbitraries.cs` to include `StopSignalInstruction()`, `MaintainerInstruction()`, and `ShellInstruction()` in the `BodyInstruction()` generator's `Gen.OneOf(...)` list. These three instruction types were previously excluded because their Sprache parsers used `excludeTrailingWhitespace: true`, which caused trailing `\n` loss during Dockerfile-level parsing. Dallas is fixing the parsers (issue #176) so these instructions will correctly preserve trailing newlines, making them safe to include in the Dockerfile body generator.

**Changes made:**
1. Added `StopSignalInstruction()`, `MaintainerInstruction()`, and `ShellInstruction()` to the `Gen.OneOf(...)` list in `BodyInstruction()` (lines 580-582).
2. Updated the XML doc comment on `BodyInstruction()` to remove the exclusion note — the comment now simply states it includes all instruction types whose parsers preserve trailing `\n`.

**Test results:** All 649 tests pass (0 failures, 0 skipped). Dallas's parser fix was apparently already applied on this branch, so the round-trip property tests (which exercise the `BodyInstruction()` generator via `DockerfileBody()` and `ValidDockerfile()`) pass with STOPSIGNAL, MAINTAINER, and SHELL included.

**Build note:** FsCheck 3.1.0 NuGet packages required a `dotnet restore --force` and clearing the stale `obj/Debug` cache to resolve correctly. The `--no-restore` flag would fail without this.

**Files modified:**
- `src/Valleysoft.DockerfileModel.Tests/Generators/DockerfileArbitraries.cs` — added 3 instructions to BodyInstruction() generator, updated XML doc comment

### 2026-03-05 — Phase 2: Lean Parser Tests for FROM and ARG Instructions

**What was implemented:** Comprehensive parser test suite in Lean 4 for FROM and ARG instructions, translating all test cases from the C# test files (`FromInstructionTests.cs` and `ArgInstructionTests.cs`).

**Test file created:** `lean/DockerfileModel/Tests/ParserTests.lean`

**Test counts:**
- FROM round-trip tests: 22 (simple, tag, digest, stage name, platform flag, all combined, variable refs, line continuations with both `\` and `` ` `` escape chars, quoted names, case-insensitive keywords, extra whitespace, fully qualified image refs)
- ARG round-trip tests: 14 (simple, multiple declarations, empty default, quoted empty, with values, variable ref defaults, line continuations, embedded comments, underscore names, quoted values with spaces)
- Token tree structure validation tests: 4 (FROM with platform+stage deep walk, ARG with value deep walk, ARG quoted empty structure)
- Dockerfile-level tests: 2 (ARG+FROM combination, multi-stage)
- Edge case tests: 6 (empty instruction, single-char names, CRLF, tab whitespace, tab indentation)
- Parser stubs (commented out for Dallas): 18 (FROM success: 6, FROM errors: 4, ARG success: 4, ARG errors: 3, Dockerfile: 1)
- **Total active tests: 48, Total parser stubs: 18**

**Approach chosen:** Two-tier strategy:
1. **Active tests (run now):** Construct expected token trees manually using Token.mk* helpers, then verify `toString` round-trips to the original string. This validates the token model and provides expected outputs.
2. **Parser stubs (commented out):** Full `Parser.parseFrom` / `Parser.parseArg` / `Parser.parseDockerfile` test functions that can be uncommented once Dallas's parser combinator library is ready. Includes both success and error path cases.

**Key patterns discovered:**
- The Lean token model maps cleanly to C# test scenarios. `Token.mkKeyValue` serves as the analog for both `PlatformFlag` (key-value flag) and `ArgDeclaration` (name[=value]).
- `Token.mkIdentifier` maps to both `Variable` and `StageName` in C# (both are identifier tokens).
- The `QuoteInfo` mechanism correctly handles the `ARG MYARG=""` case where the quoted literal wraps empty content in double quotes.
- Line continuation tokens (`mkLineContinuation`) contain exactly two children: escape symbol + newline, matching the C# `LineContinuationToken` structure.
- The C# test file uses `ValidateQuotableAggregate<LiteralToken>` for quoted literals — in Lean this maps to `Token.mkLiteral children (some { quoteChar })`.
- Comments inside instructions (e.g., `FROM ... #comment\n ...`) are modeled as `Token.mkComment` children of the instruction token, matching C# `CommentToken`.

**Integration points:**
- `SlimCheck.lean` updated to import `ParserTests` and call `runParserTests` from `main`.
- No changes needed to `lakefile.lean` — the `lean_lib` target auto-includes all files under `DockerfileModel/`.

**Files created:**
- `lean/DockerfileModel/Tests/ParserTests.lean` — 48 active tests + 18 parser stubs

**Files modified:**
- `lean/DockerfileModel/Tests/SlimCheck.lean` — added import and `runParserTests` call in main

### 2026-03-05T20:00:00Z — Phase 2 Parser Test Suite Complete

Team update (2026-03-05T20:00:00Z): Phase 2 comprehensive parser test suite completed. ParserTests.lean (1180 lines) created with 48 active token-tree construction tests (FROM: 28, ARG: 20) plus 18 commented parser stubs. All tests pass; token model validated with full edge case coverage (line continuations, variable references, quoting, comments, case insensitivity, CRLF/tabs). SlimCheck.lean updated to run ParserTests. Ready for Dallas parser integration.

**Test suite statistics:**
- **Active tests:** 48 (FROM: 28, ARG: 20)
  - Basic syntax, tags, digests, platform flags, staging, line continuations, quotes, variables, comments, case variations, whitespace
- **Parser stubs:** 18 (marked with `-- [PARSER]`)
  - Full parser integration tests using `Parser.parseFrom`, `Parser.parseArg`
  - Round-trip fidelity verification
  - Error path tests (malformed syntax, missing fields)

**Test strategy rationale:**
- Token tree tests exercise model immediately with all edge cases
- Parser stubs provide Dallas concrete acceptance criteria
- Shared test data allows straightforward integration (uncomment + import)

**Architecture alignment:** Follows Ripley's 8 Phase 2 architecture decisions. Tests validate token model before parser integration, ensuring round-trip correctness.

### 2026-03-05T23:43:03Z — Phase 4 Variable Resolution Lean Proofs Complete

Team update (2026-03-05T23:43:03Z): Dallas completed Phase 4 formal verification of variable resolution semantics. Three new Lean 4 modules created:
- `lean/DockerfileModel/VariableResolution.lean` — Core `resolve` function, `VarMap` type, `Modifier` enum, `isVariableSet` predicate
- `lean/DockerfileModel/Scoping.lean` — Dockerfile variable scoping rules
- `lean/DockerfileModel/Proofs/VariableResolution.lean` — All 5 modifier proofs (dash_setEmpty, dash_useEmpty, colon_setEmpty, colon_useEmpty, plain)

Key design decisions: VarMap as association list (List (String × String)) for proof-friendliness, Except String String return type for error modeling, extracted processEscapes for termination clarity. All modifier proofs complete (no sorry), 1 documented sorry in resolve_token_toString_unchanged per spec. Lean build passes (18 jobs). .NET baseline verified green (649 tests).
Team update (2026-03-06T00:12:22Z): Phase 5 Capstone proofs completed: 12 new theorems in Capstone.lean, token_concat_length fixed in RoundTrip.lean, proof coverage documented. Total: 55 proved, 4 documented sorries. Build: 19 jobs, 0 errors. — decided by Dallas

### 2026-03-08 — Differential Testing Expansion (Issues #238-#247)

**Team update (2026-03-08)**: Lambert completed differential testing expansion to find additional parsing edge cases. Successfully filed 10 new GitHub issues (#238-#247) covering COPY/ADD flag handling, boolean flag formats (=true/=false), and serializer gaps. Added FsCheck generators for new edge case coverage including mount flags, variable references, empty values, dotted/hyphenated label keys, and multi-source file transfers. Implemented TokenJsonSerializer.cs workarounds for known C#/Lean parser differences. PR #248 opened targeting dev branch. Test suite now at 693 passing tests.

**Key findings:**
- Variable references in shell-form commands: ~60-70% of mismatches
- Mount flag token structure differences: ~15-20% of mismatches  
- Empty values in key=value pairs (LABEL, ENV): ~5-8% of mismatches
- Single-quoted strings with dollar signs: ~5% of mismatches

**Workarounds implemented:**
- BooleanFlag maps to keyValue token kind
- UserAccount conditional transparency (with/without group)
- Shell form whitespace splitting (RUN, CMD, ENTRYPOINT, HEALTHCHECK CMD)
- Instruction-specific serializer methods with GitHub issue tracking

**All changes documented in .squad/decisions.md for architectural reference.**

## Team update (2026-03-09T12:49:57Z): Copilot review workflow directive
Added to shared decisions: Copilot PR review workflow (add reviewer via API, respond per comment, re-request until resolved). Note: `gh pr edit --add-reviewer` does NOT work for bots; must use API directly with bot name `copilot-pull-request-reviewer[bot]`. — decided by Scribe

### 2026-03-09 — Copilot PR Review Rounds 4-7 (PR #253 Heredoc)

Team update (2026-03-09T14:39:44Z): Lambert completed final 4 rounds (4-7) of Copilot review for PR #253 (heredoc support). Addressed 3 comments in rounds 4-6 and received clean round 7 with 0 comments. Changes: JSON quote fix in comment (3d04431), DestinationToken conditional guard preservation (a3c61c8), heredoc detection robustness improvement via trailing comment stripping (3910316). Test suite expanded from 753 to 760 tests. All tests passing. Heredoc review cycle complete and ready for merge. — decided by Scribe

### 2026-03-09 — HeredocToken.Body and Heredocs property tests (PR #253)

Added 30 new tests to HeredocTests.cs covering the newly added `Body` property on `HeredocToken` and `Heredocs` property on both `RunInstruction` and `FileTransferInstruction` (via COPY and ADD).

**Test categories:**
- **HeredocToken.Body** (14 tests): single-line, multi-line, empty body, no trailing newline, special characters ($, quotes), empty lines in body, whitespace-only body, chomp flag, double-quoted delimiter, single-quoted delimiter, shebang+commands, COPY heredoc, ADD heredoc, CRLF line endings
- **RunInstruction.Heredocs** (6 tests): single heredoc, shell-form (empty), exec-form (empty), empty body, multi-line body, with mount flag
- **RunInstruction.HeredocTokens consistency** (1 test): verifies HeredocTokens.Body matches Heredocs
- **FileTransferInstruction.Heredocs via COPY** (4 tests): with heredoc, without heredoc (empty), multi-line body, HeredocTokens consistency
- **FileTransferInstruction.Heredocs via ADD** (4 tests): with heredoc, without heredoc (empty), multi-line body, special characters
- **Heredocs/HeredocTokens alignment** (1 test via COPY): count and value match

**Key patterns used:**
- `[Fact]` for each test (no data-driven tests needed since each scenario is distinct)
- Parse from text, then assert Body/Heredocs values directly
- Negative tests: shell-form, exec-form, non-heredoc COPY/ADD all return empty enumerables
- Edge cases: CRLF, whitespace-only lines, empty body between markers, no trailing newline after delimiter

**Test suite: 809 total tests, 0 failures.**

### 2026-03-10 — Comprehensive Heredoc Test Suite Rewrite (Issue #245)

Rewrote HeredocTests.cs from scratch as a comprehensive 1859-line test file covering all heredoc syntax for RUN, COPY, and ADD instructions. Written against the expected API surface (HeredocToken, Body, HeredocTokens, Heredocs, DockerfileParser.ExtractHeredocDelimiters, StripTrailingComment) that Dallas is implementing.

**Test count: 139 [Fact] tests + 3 [Theory] tests with 22 data scenarios (~160 total test cases)**

**Coverage categories:**
- **Theory-based token validation** (22 scenarios across 3 Theory methods): RUN (13), COPY (3), ADD (3) — full token tree validation with ValidateAggregate/ValidateString/ValidateNewLine
- **RUN round-trip fidelity** (20 Fact tests): simple, chomp, double-quoted, single-quoted, multi-line, empty body, custom delimiter, no trailing newline, chomp+quoted, shebang, mount flag, extra whitespace
- **COPY round-trip fidelity** (6 Fact tests): simple, quoted, single-quoted, multi-line, empty body
- **ADD round-trip fidelity** (6 Fact tests): simple, quoted, single-quoted, multi-line, empty body
- **HeredocToken.Body extraction** (14 Fact tests): single-line, multi-line, empty, no trailing newline, special chars ($, ", ', \, `), empty lines, whitespace-only, chomp, double/single-quoted, shebang, COPY, ADD, CRLF
- **RunInstruction.Heredocs property** (7 Fact tests): single, shell-form empty, exec-form empty, empty body, multi-line, mount flag, consistency with HeredocTokens
- **COPY Heredocs property** (4 Fact tests): with heredoc, without (empty), multi-line, consistency
- **ADD Heredocs property** (4 Fact tests): with heredoc, without (empty), multi-line, special chars
- **Child token inspection** (3 Fact tests): marker string, chomp marker, quoted marker
- **Edge cases** (21 Fact tests): whitespace preservation, empty lines, special chars, backslash/backtick, delimiter-like instruction names (<<RUN, <<FROM, <<COPY), very long body, mixed tabs/spaces, non-chomp tab-indented delimiter, chomp closes, multi-tab chomp, body with << syntax, substring of delimiter, single-char delimiter, dot in delimiter, long lines, blank-only body, heredoc-in-body syntax
- **Dockerfile-level** (17 Fact tests): RUN/COPY/ADD heredoc, followed by instruction, multiple heredocs, between instructions, multi-stage, ADD between, COPY between FROMs, end-of-file, mixed types, non-chomp tab, comment before/after, 3-stage, complex multi-stage, escape directive, ARG/ENV context
- **Command/Destination known limitations** (5 Fact tests): Command null, SetCommand throws, Destination null, Sources empty, SetDestination throws
- **Variable resolution** (1 Fact test): heredoc body not resolved
- **ExtractHeredocDelimiters** (11 Fact tests): unquoted, double-quoted, single-quoted, chomp, no heredoc, custom name, chomp+quoted, hyphenated, charset alignment, arbitrary chars rejected, dot delimiter, COPY/ADD
- **Trailing comment stripping** (5 Fact tests): no comment, with comment, hash in single quotes, hash in double quotes, hash after closing quote
- **Case insensitivity** (3 Fact tests): lowercase run, copy, add with heredoc

**Key design decisions:**
- Used [Fact] for most tests (each scenario is distinct), [Theory/MemberData] for token-validation scenarios following RunInstructionTests pattern
- Used TestHelper.RunParseTest (added by Dallas on heredoc branch) for Theory-based parse tests
- All round-trip tests assert `Parse(text).ToString() == text` — the foundational fidelity guarantee
- Tests written against expected API that Dallas is implementing; minor adjustments may be needed
- Covered known limitations (Destination null for COPY/ADD heredoc) with explicit assertion tests

**File created:** `src/Valleysoft.DockerfileModel.Tests/HeredocTests.cs` (1859 lines, 142 test attributes)

---

## Team update (2026-03-10T12:30:00Z): Heredoc test suite decision merged, architecture finalized

Decision merged to decisions.md documenting comprehensive heredoc test suite strategy. Test-first approach locks in API surface before implementation lands on dev. Dual strategy: [Theory]/[MemberData] for token-tree structural validation, [Fact] for round-trip, property extraction, edge case, and Dockerfile-level tests. Known limitations (COPY/ADD Destination-is-null, Command-setter-throws) explicitly tested as assertions — any future fix will break tests intentionally and force coordinated update.

**160 test cases** provide executable specification covering all edge cases (delimiter names matching instructions, body content with heredoc syntax, chomp modes, single-character delimiters). Dependencies on Dallas's implementation tracked (HeredocToken, Body property, HeredocTokens/Heredocs properties, DockerfileParser.ExtractHeredocDelimiters, TestHelper.RunParseTest).

**Impact:** Dallas must ensure implementation passes all test cases. API surface changes require coordination for test updates.
