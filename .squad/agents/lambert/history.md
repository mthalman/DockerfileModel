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

**What was implemented:** Tests for the `--link` boolean flag on `CopyInstruction`.

**Key patterns discovered:**
- `CopyInstructionTests` extends `FileTransferInstructionTests<CopyInstruction>` — base class owns all shared flag tests (chown, chmod, sources, destination). COPY-specific tests live in `CopyInstructionTests.cs`.
- All existing flags in this codebase are `key=value` pairs (`KeyValueToken<KeywordToken, T>`). The `--link` flag is unique: a bare boolean flag with no `=value`. It needs its own `LinkFlag` class (a custom `AggregateToken` containing two `SymbolToken('-')` and a `KeywordToken("link")`).
- Token structure for `--link`: `ValidateAggregate<LinkFlag>(token, "--link", symbol '-', symbol '-', keyword "link")` — no separator or value tokens.
- The `Link` property on `CopyInstruction` should be a `bool` (not `bool?`): `false` when absent, `true` when `--link` present.
- The `CopyInstruction` constructor needs a `link: bool` parameter. `DockerfileBuilder.CopyInstruction()` needs the same `link:` parameter.
- Round-trip tests cover: parse → `ToString()` must reproduce input exactly, including whitespace and line continuations.
- Combinations tested: `--link` alone, with `--from`, with `--chown`, with `--chmod`, all four together, link before/after `--from`, multiple sources, line-continuation whitespace.

**Files modified:**
- `src/Valleysoft.DockerfileModel.Tests/CopyInstructionTests.cs` — added `Link()`, `Link_WithFromStageName()`, `Link_WithChown()`, `Link_WithChmod()` [Fact] tests and 8 new `ParseTestInput()` Theory cases with `ValidateLinkFlag()` helper
- `src/Valleysoft.DockerfileModel.Tests/DockerfileBuilderTests.cs` — added 4 `CopyInstruction_WithLink*` [Fact] tests

**Files Dallas must implement (not yet present):**
- `src/Valleysoft.DockerfileModel/LinkFlag.cs` — new `AggregateToken` subclass for the `--link` token
- `CopyInstruction.Link` property (bool) and `link:` constructor parameter
- `DockerfileBuilder.CopyInstruction()` overload/parameter update with `link:`
- Parser support for `--link` in `CopyInstruction.GetInnerParser()`

### 2026-03-05 — COPY --link tests complete (Issue #115)

**Team update (2026-03-05T04:13:15Z)**: Lambert completed test coverage for LinkFlag. Added 4 Facts, 8 parse scenarios, 4 builder tests. All 532 tests pass. Dallas completed implementation. Ready for review.

### 2026-03-04 — RUN --network and --security tests

**What was implemented:** Full test coverage for `NetworkFlag`, `SecurityFlag`, and their integration in `RunInstruction`.

**Key patterns discovered:**
- `NetworkFlag` and `SecurityFlag` are `KeyValueToken<KeywordToken, LiteralToken>` — standard key-value flags identical in shape to `IntervalFlag`, `PlatformFlag`, etc. Use `ValidateKeyValueFlag<T>` from `TokenValidator.cs` for concise token validation in parse scenarios.
- For variable-containing values (e.g., `--network=$NET`), the token structure nests: `LiteralToken` wraps `VariableRefToken` wraps `StringToken`. The validator pattern is: `ValidateAggregate<LiteralToken>(token, "$NET", token => ValidateAggregate<VariableRefToken>(token, "$NET", token => ValidateString(token, "NET")))`.
- `RunInstruction` constructors only accept `network:` and `security:` named parameters on overloads that also take `IEnumerable<Mount>`. When creating without mounts, pass `Enumerable.Empty<Mount>()` explicitly (not `null`) to reach the right overload.
- The `RunInstruction.Create` test method was simplified: always route through the mounts-accepting constructor using `scenario.Mounts ?? Enumerable.Empty<Mount>()`, avoiding the complex nested-if branching.
- `Network` and `Security` properties follow the exact same nullable get/set pattern as `HealthCheckInstruction.Interval`/`Timeout`/etc.: set to a value inserts the flag token, set to null removes it, and `ToString()` updates accordingly. Use `TestHelper.TestVariablesWithNullableLiteral()` for variable resolution tests.

**Test counts:**
- `NetworkFlagTests.cs` (new): 7 tests — 4 parse (default, none, host, $NET variable), 3 create (host, none, default)
- `SecurityFlagTests.cs` (new): 5 tests — 3 parse (insecure, sandbox, $SEC variable), 2 create (insecure, sandbox)
- `RunInstructionTests.cs` (updated): 13 new tests — 6 parse scenarios (network alone, security alone, both together, mount+network, all three mixed order, network with exec form), 3 create scenarios (network only, security only, both), 4 Facts (Network, NetworkWithVariables, Security, SecurityWithVariables)
- Total: 25 new tests. Full suite: 557 pass, 0 fail.

**Files created:**
- `src/Valleysoft.DockerfileModel.Tests/NetworkFlagTests.cs`
- `src/Valleysoft.DockerfileModel.Tests/SecurityFlagTests.cs`

**Files modified:**
- `src/Valleysoft.DockerfileModel.Tests/RunInstructionTests.cs` — added parse/create scenarios, Network/Security Fact tests, updated CreateTestScenario class with Network/Security properties, simplified Create method routing

### 2026-03-05 — RUN --network and --security tests complete (Issue #116)

**Team update (2026-03-05T04:38:40Z)**: Lambert completed 25 comprehensive tests for NetworkFlag and SecurityFlag. NetworkFlagTests (7), SecurityFlagTests (5), RunInstructionTests updates (13). All 557 tests pass. Dallas completed implementation. Decisions documented in decisions.md. Ready for review.

### 2026-03-05 — Baseline Test Run + Test Code Refactoring Analysis

**Baseline established**: 599 tests, 0 failed, 0 skipped. Run time ~570 ms. Branch is `refactor`.

**Key refactoring patterns discovered (analysis only — no code changed):**

1. **Parse method body is copy-pasted verbatim across 39 test files.** Every `Parse(T scenario)` method has an identical if/else structure: parse on success path (assert round-trip + tokens + Validate), throw+assert on error path. This could be extracted as a static `RunParseTest<T>(ParseTestScenario<T> scenario, Func<string, char, T> parseFunc)` helper in `TestHelper.cs`. `FileTransferInstructionTests<T>` already does this correctly — the same approach can be applied universally.

2. **Per-file ParseTestScenario subclasses are nearly identical across 37 files.** Every test file defines a local `class XxxParseTestScenario : ParseTestScenario<Xxx> { public char EscapeChar { get; set; } }`. This is the same class repeated 37 times, differing only in the generic type argument.

3. **Boolean flag validator helpers duplicated.** `CopyInstructionTests.ValidateLinkFlag()` and `AddInstructionTests.ValidateLinkFlag()` are identical private static methods (3 tokens: symbol '-', symbol '-', keyword "link"). Similarly `AddInstructionTests.ValidateKeepGitDirFlag()` stands alone but the pattern is the same shape. These could be promoted to `TokenValidator.cs` as `ValidateBooleanFlag<T>(Token token, string keyword)`.

4. **`--network=` and `--security=` flag validator patterns repeated.** `NetworkFlagTests`, `SecurityFlagTests`, and `RunInstructionTests` each expand the full 5-token `symbol '-', symbol '-', keyword, symbol '=', literal` chain inline rather than using the existing `ValidateKeyValueFlag<T>()` helper from `TokenValidator.cs`. The helper already exists and matches exactly — those parse scenarios can be shortened.

5. **`KeepGitDirFlagTests` and `ChecksumFlagTests` each define their own `CreateTestScenario` inner class.** These are structurally identical to `TestScenario<T>` with one payload property added. No shared base.

6. **Flag-level test files are thin (single parse case, single create case).** `KeepGitDirFlagTests` has only 1 parse scenario and 1 create scenario. Compare to `ChecksumFlagTests` (4 parse + 3 create) and `NetworkFlagTests` (4 parse + 3 create). The pattern for boolean flags (KeepGitDirFlag, LinkFlag) omits: error path (invalid input), line-continuation round-trip, variable reference tests (not applicable for boolean, but confirm it).

7. **Missing instruction test files for `BooleanFlag` abstraction.** There is no `LinkFlagTests.cs` (the way `KeepGitDirFlagTests.cs` exists). LinkFlag is only tested through `CopyInstructionTests` and `AddInstructionTests`. A standalone `LinkFlagTests.cs` would be consistent.

8. **`HealthCheckInstructionTests.Create` method has nested if/else branching** to select the right constructor overload, where `RunInstructionTests.Create` was already simplified to use `??` on `Mounts`. The HealthCheck version is more complex and could be simplified.

**Coverage gaps identified:**
- No negative/error path tests for boolean flags (KeepGitDirFlag, LinkFlag): what does parsing `--keep-git-dir=value` or `--link=true` do?
- `ChecksumFlagTests` lacks a test for empty/null checksum value.
- `AddInstructionTests` does not test `ChecksumWithVariables` for the token-level path (set via `ChecksumToken = null` then `Checksum = "$var"`) beyond what `TestVariablesWithNullableLiteral` covers.
- No integration test in `ScenarioTests.cs` covering multi-flag ADD (checksum + keep-git-dir + link all together in a real Dockerfile parse).

**Key file paths:**
- `C:/repos/DockerfileModel/src/Valleysoft.DockerfileModel.Tests/TestHelper.cs` — ConcatLines, TestVariablesWithLiteral, TestVariablesWithNullableLiteral
- `C:/repos/DockerfileModel/src/Valleysoft.DockerfileModel.Tests/TokenValidator.cs` — ValidateKeyValueFlag<T>, ValidateAggregate<T>, ValidateLiteral, etc.
- `C:/repos/DockerfileModel/src/Valleysoft.DockerfileModel.Tests/TestScenario.cs` — base TestScenario<T> and ParseTestScenario<T>
- `C:/repos/DockerfileModel/src/Valleysoft.DockerfileModel.Tests/FileTransferInstructionTests.cs` — best-in-class example: base class with RunParseTest/RunCreateTest helpers

### 2026-03-05 — Refactor branch analysis session complete

**Team update (2026-03-05T15:16:02Z)**: Ripley completed cross-file refactoring analysis of refactor branch. Verdict: production-ready. All 599 tests passing baseline confirmed. Lambert, Dallas, and Ripley performed parallel analyses. Ripley assessed architectural changes (3 base classes, 6 cross-file patterns). Dallas identified 5 implementation code smells (low-to-medium severity, P1-P5 prioritized). Lambert identified 6 test code refactoring opportunities (T1-T6 prioritized): consolidate 37 ParseTestScenario subclasses (highest value), extract 39 RunParseTest methods, consolidate validators, fill gaps. All findings documented in decisions.md with appropriateness gates and risk assessment.
