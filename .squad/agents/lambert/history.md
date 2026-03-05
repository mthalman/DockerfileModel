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
