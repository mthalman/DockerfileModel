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

### 2026-03-05 — T1 + T2 Test Code Consolidation

**What was implemented:** Completed both test code consolidation tasks T2 (EscapeChar in base class, delete 37 per-file subclasses) and T1 (extract `RunParseTest<T>` to `TestHelper.cs`, update 39 Parse methods to one-liners). All 599 tests pass after the changes.

**T2 — Consolidating ParseTestScenario subclasses:**
- Added `public char EscapeChar { get; set; }` to `ParseTestScenario<T>` in `TestScenario.cs`.
- Deleted 33 per-file subclasses that only added `EscapeChar` (e.g., `ArgDeclarationParseTestScenario`, `FromInstructionParseTestScenario`, etc.).
- Left `LiteralTokenParseTestScenario` (has extra `ParseVariableRefs`) and `KeyValueTokenParseTestScenario` (has extra `Key`) intact; removed their now-duplicate `EscapeChar` property to fix CS0108 warnings.
- `FileTransferInstructionTests<T>` had a nested generic `FileTransferInstructionParseTestScenario<TInstruction>` — replaced with `ParseTestScenario<TInstruction>` directly.
- Used `replace_all` edit to swap type names globally in each file, then deleted the broken class definition with a targeted edit.

**T1 — Extracting RunParseTest helper:**
- Added `RunParseTest<T>(ParseTestScenario<T> scenario, Func<string, char, T> parseFunc) where T : AggregateToken` to `TestHelper.cs`. Constraint is `AggregateToken` (not `DockerfileConstruct`) because many tested types (flags, tokens, utility types) extend `AggregateToken` directly.
- Updated all applicable `Parse` methods to one-liners: `TestHelper.RunParseTest(scenario, XxxType.Parse)`.
- Special cases using constructor syntax (no static `Parse`): `StageNameTests` and `VariableTests` use lambda form: `(text, escapeChar) => new StageName(text, escapeChar)`.
- Files NOT updated for T1: `GenericInstructionTests` (different assertion pattern), `KeywordTokenTests` (constructor form), `LiteralTokenTests` (3-arg constructor), `KeyValueTokenTests` (complex multi-arg parse), `CommentTests`/`WhitespaceTests`/`DockerfileTests` (no escapeChar), `ParserDirectiveTests` (internal ParseTestScenario class).
- `FileTransferInstructionTests` protected `RunParseTest` instance method was refactored to delegate to `TestHelper.RunParseTest(scenario, this.parse)`.

**Bug fix exposed during T1:**
- The original `UserAccountTests.Parse` had a pre-existing bug in its error path: it called `ArgInstruction.Parse` instead of `UserAccount.Parse`. This was masked because `ArgInstruction.Parse("user:", ...)` threw correctly even though `UserAccount.Parse("user:", ...)` did not.
- After T1 unified the parse paths, the bug surfaced: `UserAccount.Parse("user:", '\0')` silently accepted `"user:"` (user with empty group after colon) by parsing `"user"` and leaving `":"` unconsumed — Sprache's `Parse` method does NOT require all input to be consumed.
- **Fix:** Added `.End()` to `UserAccount.Parse` (not to `GetInnerParser`, which is also used via `GetParser` in instruction-level parsing): `new(GetTokens(text, GetInnerParser(escapeChar).End()), escapeChar)`. This matches the pattern used in `FromInstruction.GetArgsParser`.
- **Test fix:** Updated the `"user:"` scenario's `ParseExceptionPosition` from `new Position(1, 1, 1)` to `new Position(1, 1, 5)` since with `.End()` the exception is thrown at column 5 (after `"user"`).
- `ExecFormCommandTests`, `ShellFormCommandTests` also had bugs in their error paths (wrong parse function). These were silently fixed by T1 unification.

**Key patterns learned:**
- Sprache `parser.Parse(string)` does NOT throw for unconsumed input — it only throws if the parser itself fails. Use `.End()` on the parser to enforce full consumption in standalone parse methods, but NOT on `GetParser()` methods used inside larger instruction parsers.
- The Sprache `Position` constructor is `Position(pos, line, column)` — `TestHelper` only checks `.Line` and `.Column` (2nd and 3rd args). The `pos` first argument is not validated.
- `FileTransferInstructionTests<T>.RunParseTest` is a protected instance method (not static), and it uses `this.parse` (a stored `Func<string, char, TInstruction>` field). It can delegate to `TestHelper.RunParseTest(scenario, this.parse)` cleanly.

**Files modified:**
- `src/Valleysoft.DockerfileModel.Tests/TestScenario.cs` — added `EscapeChar`
- `src/Valleysoft.DockerfileModel.Tests/TestHelper.cs` — added `RunParseTest<T>`
- `src/Valleysoft.DockerfileModel.Tests/UserAccountTests.cs` — updated parse test scenario position
- `src/Valleysoft.DockerfileModel/UserAccount.cs` — added `.End()` to `Parse` method
- 30+ test files — deleted per-file subclasses, updated Parse method bodies

### 2026-03-05 — Refactoring execution session complete

Team update (2026-03-05T16:04:05Z): Lambert completed T1+T2 test consolidation (added EscapeChar to base, deleted 37 subclasses, extracted RunParseTest helper, fixed UserAccount.Parse bugfix). Dallas completed L1+L2+L3 library cleanup. Ripley completed full code review and approved all changes. All 599 tests passing. Changes documented in .squad/decisions.md. Session logs created in .squad/log/ and .squad/orchestration-log/. Production-ready to merge and ship.

### 2026-03-05 — FsCheck Property-Based Testing Infrastructure Ready (P0-1 + P0-2)

Team update (2026-03-05T17:45:00Z): Dallas completed FsCheck Phase 0 infrastructure setup (P0-1 and P0-2). Added FsCheck 3.1.0 and FsCheck.Xunit 3.1.0 packages. Created comprehensive DockerfileArbitraries generator suite (609 lines) with generators for all 18 instruction types, variable references (all 6 modifiers), and Dockerfile-level composition. Identified and documented round-trip exclusion at Dockerfile level: STOPSIGNAL, MAINTAINER, SHELL use excludeTrailingWhitespace=true, causing trailing \n loss in multi-instruction context (pre-existing behavior). 621 tests passing (599 existing + 22 property-based). Zero warnings. Generators ready for P0-3 through P0-7 property test implementation. Lambert can now start P0-3 (round-trip fidelity) in parallel with other team members on P0-4 through P0-7. See .squad/decisions.md for full Formal Verification PRD and Phase 0 decomposition.

### 2026-03-05 — P0-4 through P0-7 Property-Based Tests Complete

**What was implemented:** 28 new property-based tests covering four categories:

**P0-4: Token tree consistency (7 tests)**
- Recursively walks the full token tree after parsing and verifies that every `AggregateToken` node satisfies `ToString() == string.Concat(children.Select(t => t.ToString()))`, after accounting for two known decorations: `VariableRefToken` prepends `$` to its children concatenation, and `IQuotableToken` wraps in quote characters. Tests at Dockerfile level, plus individual instruction types (FROM, RUN, COPY, ADD, HEALTHCHECK) and VariableRef.

**P0-5: Variable resolution non-mutation (3 tests)**
- Verifies that `Dockerfile.ResolveVariables()` with default options (UpdateInline=false) does NOT mutate the model's `ToString()` output. Tests with no overrides, with arbitrary overrides, and with explicit `UpdateInline = false`. Uses a new `DockerfileWithVariables()` generator that produces Dockerfiles with stage-level ARG declarations and $VAR references.

**P0-6: Modifier semantics (16 tests)**
- Tests all 6 variable modifier forms at the Dockerfile resolution level: `:-`, `-`, `:+`, `+`, `:?`, `?`.
- Each modifier tested in multiple states: unset, set-to-empty, set-to-value (where applicable).
- Verified correct exception throwing for `?` and `:?` modifiers on unset/empty variables.

**P0-7: Parse isolation (2 tests)**
- Verifies that parsing instruction X followed by instruction Y in a Dockerfile produces the same tokens for X as parsing X alone, and vice versa. Uses `Instruction.CreateInstruction()` (internal, accessed via `InternalsVisibleTo`) for standalone parsing. Tests both the first and second instruction in the combined Dockerfile.

**Key patterns discovered:**
- `VariableRefToken.GetUnderlyingValue()` overrides the base class to prepend `$` to the children concatenation. `IQuotableToken` wraps in quotes via `Token.ToString()`. These are the only two decorations that break the naive `ToString() == concat(children)` invariant.
- For variable modifier testing, "unset" vs "set to null" is a critical distinction: `ARG x` (no default) adds `{x: null}` to stageArgs. Non-colon modifiers (`-`, `+`, `?`) check dictionary existence (`TryGetValue` returns true), so the variable is "set". Colon modifiers (`:-`, `:+`, `:?`) additionally check for non-empty value, so null counts as "unset". To test truly unset variables, omit the ARG declaration entirely.
- ARG declarations must be stage-level (after FROM) for them to appear in stageArgs during instruction resolution. Global ARGs (before FROM) are only available to FROM instructions.
- When an instruction is parsed as part of a Dockerfile, it includes the trailing `\n` (used as construct delimiter). Standalone parsing does not include trailing `\n`. Comparison requires `TrimEnd('\n')` for non-final instructions.

**New generators added to DockerfileArbitraries.cs:**
- `DockerfileWithVariables()` — complete Dockerfiles with stage-level ARG + $VAR references
- `SingleBodyInstruction()` — public wrapper for the private `BodyInstruction()` generator
- `VariableModifierComponents()` — generates (varName, modifier, modValue) tuples for modifier testing

**Test counts:** 649 total (621 baseline + 28 new). 0 failures, 0 skipped, 0 warnings.

**Files modified:**
- `src/Valleysoft.DockerfileModel.Tests/PropertyTests.cs` — added 28 new tests across 4 categories
- `src/Valleysoft.DockerfileModel.Tests/Generators/DockerfileArbitraries.cs` — added 3 new public generators

### 2026-03-05 — FsCheck Generator Update for Issue #176 Fix

**What was updated:** Updated `DockerfileArbitraries.cs` to include `StopSignalInstruction()`, `MaintainerInstruction()`, and `ShellInstruction()` in the `BodyInstruction()` generator's `Gen.OneOf(...)` list. These three instruction types were previously excluded because their Sprache parsers used `excludeTrailingWhitespace: true`, which caused trailing `\n` loss during Dockerfile-level parsing. Dallas is fixing the parsers (issue #176) so these instructions will correctly preserve trailing newlines, making them safe to include in the Dockerfile body generator.

**Changes made:**
1. Added `StopSignalInstruction()`, `MaintainerInstruction()`, and `ShellInstruction()` to the `Gen.OneOf(...)` list in `BodyInstruction()` (lines 580-582).
2. Updated the XML doc comment on `BodyInstruction()` to remove the exclusion note — the comment now simply states it includes all instruction types whose parsers preserve trailing `\n`.

**Test results:** All 649 tests pass (0 failures, 0 skipped). Dallas's parser fix was apparently already applied on this branch, so the round-trip property tests (which exercise the `BodyInstruction()` generator via `DockerfileBody()` and `ValidDockerfile()`) pass with STOPSIGNAL, MAINTAINER, and SHELL included.

**Build note:** FsCheck 3.1.0 NuGet packages required a `dotnet restore --force` and clearing the stale `obj/Debug` cache to resolve correctly. The `--no-restore` flag would fail without this.

**Files modified:**
- `src/Valleysoft.DockerfileModel.Tests/Generators/DockerfileArbitraries.cs` — added 3 instructions to BodyInstruction() generator, updated XML doc comment
