# Squad Decisions

## Active Decisions

### 2026-03-05T04:00:00Z: User directive
**By:** Matt Thalman (via Copilot)
**What:** Never reference an issue number in a commit message. Issue references belong in PR descriptions only.
**Why:** User request — captured for team memory

### 2026-03-05T04:20:00Z: User directive
**By:** Matt Thalman (via Copilot)
**What:** Always target the `dev` branch when creating PRs — not `main`.
**Why:** User request — captured for team memory

### 2026-03-04: RUN --network and --security Implementation Approach
**Author:** Dallas (Core Dev)
**Issue:** #116 — Support for `--network` and `--security` options on the RUN instruction

#### Context
The Docker `RUN` instruction supports `--network=<value>` (e.g., `default`, `none`, `host`) and `--security=<value>` (e.g., `insecure`, `sandbox`) options that were not yet modeled. Both are simple key-value flags.

#### Decision
**Key-value flag tokens**: Implemented `NetworkFlag` and `SecurityFlag` as `KeyValueToken<KeywordToken, LiteralToken>` subclasses, matching the established pattern used by `PlatformFlag`, `IntervalFlag`, `TimeoutFlag`, etc. This is the correct base for flags that carry a value after `=`.

**RunInstruction refactored to Options() pattern**: The parser was refactored from a mount-only combinator to an `Options()` method (matching `HealthCheckInstruction`) that accepts any of `MountFlag`, `NetworkFlag`, or `SecurityFlag` in any order via `.Many().Flatten()`. This maintains round-trip fidelity regardless of flag ordering in the source.

**3-tier property pattern on RunInstruction**: Added `Network`/`Security` string properties, `NetworkToken`/`SecurityToken` literal token properties, and private `NetworkFlag`/`SecurityFlag` token properties following the exact HealthCheckInstruction pattern. This required adding a `private readonly char escapeChar` field to RunInstruction.

**Constructor overload consolidation**: Removed the intermediate overloads that took `(string, IEnumerable<Mount>, char)` to avoid ambiguity with the new overloads adding optional `string? network` and `string? security` parameters. The new optional-parameter constructors fully subsume the old ones with no breaking change in behavior.

#### Rationale
- The Options() parser pattern is proven (HealthCheckInstruction uses it for 4 optional flags) and naturally handles any-order flag combinations.
- Using `KeyValueToken<KeywordToken, LiteralToken>` is the established pattern for all value-carrying flags in the codebase.
- The 3-tier property pattern enables both string-level and token-level access, with proper support for programmatic add/remove of flags after construction.

#### Files Changed
- `src/Valleysoft.DockerfileModel/NetworkFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/SecurityFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/RunInstruction.cs` (modified)
- `src/Valleysoft.DockerfileModel/DockerfileBuilder.cs` (modified)

### 2026-03-04: COPY --link Implementation Approach
**Author:** Dallas (Core Dev)
**Issue:** #115 — Support for new COPY instruction options (`--link`)

#### Context
The `--link` option on COPY is a boolean flag — it has no `=value`, unlike all other existing flags (`--from=`, `--chown=`, `--chmod=`). This required establishing a new token pattern for valueless flags.

#### Decision
**Boolean flag token as bare AggregateToken**: Implemented `LinkFlag` as an `AggregateToken` containing `SymbolToken('-')`, `SymbolToken('-')`, `KeywordToken("link")`. There is no `KeyValueToken` base since there is no separator or value. This differs from all existing flag classes which extend `KeyValueToken<TKey, TValue>`.

**Flag ordering in constructed instructions**: The `CreateInstructionString` method in `FileTransferInstruction` was extended with an optional `trailingOptionalFlag` parameter (defaults to `null`). This places a trailing flag _after_ `--chown` and `--chmod` in the generated string. The canonical construction order for COPY is: `--from`, `--chown`, `--chmod`, `--link`.

**Parser accepts any order**: The existing `GetArgsParser` combinator in `FileTransferInstruction` already handles arbitrary flag ordering via `.Many()` and `.Optional()`. No changes were needed to the parser to accept `--link` in any position — only a new alternative was added to the `GetInnerParser` call in `CopyInstruction`.

#### Rationale
- Keeping `LinkFlag` as a standalone `AggregateToken` (not `KeyValueToken`) is the correct model for a flag with no value. It avoids forcing a value-oriented abstraction onto something that is simply a presence/absence toggle.
- The `trailingOptionalFlag` approach to `CreateInstructionString` preserves backward compatibility — existing callers pass `null` implicitly and no existing behavior changes.
- Placing `--link` last among all flags in constructed output matches the most common real-world usage pattern seen in Docker documentation.

#### Files Changed
- `src/Valleysoft.DockerfileModel/LinkFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/CopyInstruction.cs`
- `src/Valleysoft.DockerfileModel/FileTransferInstruction.cs`
- `src/Valleysoft.DockerfileModel/DockerfileBuilder.cs`

### 2026-03-05: ADD --checksum, --keep-git-dir, and --link Implementation Approach
**Author:** Dallas (Core Dev)
**Date:** 2026-03-05

#### Context
The Docker `ADD` instruction supports three new options not yet modeled: `--checksum=<hash>` (key-value), `--keep-git-dir` (boolean), and `--link` (boolean). The `--exclude` option was explicitly deferred as not yet stable syntax.

#### Decision
**Single constructor for AddInstruction**: AddInstruction was refactored to a single public constructor with all optional parameters (`changeOwner`, `permissions`, `checksum`, `keepGitDir`, `link`, `escapeChar`) rather than providing a backward-compat positional overload alongside the new extended one. Two overloads with overlapping optional parameters produce C# overload-ambiguity errors when named arguments are used.

**optionalFlagParser chain (not full Options() refactor)**: ADD uses the existing `optionalFlagParser` mechanism in `FileTransferInstruction.GetInnerParser`, chaining `ChecksumFlag | KeepGitDirFlag | LinkFlag` with `.Or()`. This is simpler than the full Options() combinator pattern used by RunInstruction, because `FileTransferInstruction.FlagOption` already handles arbitrary-order flag parsing via `.Many()` + `.Optional()`.

**Flag ordering in CreateInstructionString**: `--checksum` is the leading `optionalFlag` (placed before `--chown`/`--chmod`). `--keep-git-dir` and `--link` are combined into the `trailingOptionalFlag` slot (placed after `--chmod`).

**ChecksumFlag**: Extends `KeyValueToken<KeywordToken, LiteralToken>` with `isFlag: true` and `canContainVariables: true`. Identical pattern to NetworkFlag/SecurityFlag/PlatformFlag.

**KeepGitDirFlag**: Extends `AggregateToken` containing `SymbolToken('-')`, `SymbolToken('-')`, `KeywordToken("keep-git-dir")`. Identical pattern to `LinkFlag`.

**LinkFlag reused**: The existing `LinkFlag` class (created for COPY --link) is reused directly for ADD --link.

#### Files Changed
- `src/Valleysoft.DockerfileModel/ChecksumFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/KeepGitDirFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/AddInstruction.cs` (modified)
- `src/Valleysoft.DockerfileModel/DockerfileBuilder.cs` (modified)

### 2026-03-05: Ripley Refactor Analysis — Cross-File Pattern Findings
**Author:** Ripley (Lead)
**Date:** 2026-03-05
**Branch:** refactor

#### Summary
The refactor branch is in good shape. All three major structural changes (`CommandInstruction`, `BooleanFlag`, `KeywordLiteralFlag`) are clean, well-scoped, and preserve round-trip fidelity.

#### Key Findings

**Six cross-file patterns analyzed:**

1. **BooleanFlag thin-subclass boilerplate** (2 instances) — Already complete. The `BooleanFlag` base class extracts 100% of shared logic. Each concrete subclass is 23 lines with zero duplication beyond the factory lambda, which cannot be eliminated in C# without reflection or source generators. Recommended action: Leave alone. Risk: None.

2. **KeywordLiteralFlag thin-subclass boilerplate** (9 instances) — Already complete. The `KeywordLiteralFlag` base class eliminates all body duplication. The refactor branch migrated all 9 pre-existing flag classes from verbose inline pattern to slim form. Recommended action: Leave alone. Risk: None.

3. **3-tier optional flag property pattern** (8 instances across 4 instructions) — Invariant: string property, LiteralToken property, private flag property. Varying: flag type and parameter name. Attempting to collapse via generics would require complex type constraints or lose discoverability. Recommended action: Document, do not refactor. Risk: High to refactor, low value.

4. **Boolean flag property pattern** (3 instances) — Same reasoning as Pattern 3. Recommended action: Document, do not refactor. Risk: None to document; high if extracted via generics.

5. **GetFlagArgs / GetOptionArgs builder duplication** (2 instances) — Private, instruction-specific methods with different signatures. Extracting a shared helper would be more obscure than current code. Recommended action: Leave alone. Risk: Low to leave, medium to refactor.

6. **CmdInstruction GetArgsParser with orphaned MountFlag parser** (dead code in 2 files) — The mount parser was carried over from before RUN was the only instruction using mounts. CMD and ENTRYPOINT do not support mounts. The parser silently matches zero mounts rather than rejecting invalid `--mount` tokens. Recommended action: Flag for cleanup. Remove `MountFlag.GetParser().Many()` from both CmdInstruction and EntrypointInstruction. Risk: Low-medium. Parser behavior change (would reject `--mount` rather than silently swallowing).

#### Architectural Observations
- CommandInstruction base is correct and complete
- Token hierarchy is stable and well-structured
- Round-trip fidelity is maintained across all refactor changes
- ParseHelper changes are additive and bug-fix only (quote parsing fix in WrappedInQuotesIdentifier/WrappedInQuotesLiteralString)
- FileTransferInstruction flag ordering design is sufficient for current scope (two-slot design works for one "leading extra flag" and multiple "trailing extra flags")

#### Verdict
The refactor branch is production-ready. The one actionable finding is the dead `MountFlag` parser in `CmdInstruction` and `EntrypointInstruction`, which should be removed before shipping (low-medium risk, improves clarity).

### 2026-03-05: Dallas Code Smell Analysis — Refactor Branch
**Author:** Dallas (Core Dev)
**Date:** 2026-03-05
**Branch:** refactor

#### Summary
Full code smell scan of `src/Valleysoft.DockerfileModel/` identified 5 prioritized findings and confirmed the refactor branch has resolved the highest-impact duplication.

#### What Was Already Fixed
- **11 flag classes** previously had fully duplicated `Parse()` and `GetParser()` method bodies. Now 24-line thin wrappers via `BooleanFlag` and `KeywordLiteralFlag` bases.
- **CMD/ENTRYPOINT shared state** (`Command` property, variable resolution suppression) extracted to `CommandInstruction` base.

#### Remaining Findings (Prioritized)

**P1 — Near-Duplicate Parser Methods in CmdInstruction / EntrypointInstruction** (Medium)
- Both contain character-for-character identical `GetArgsParser` and `GetCommandParser` methods
- Suggested refactoring: Extract as `protected static` methods on `CommandInstruction`
- Risk assessment: Low. Both methods are private/static with no external callers. Parser behavior preserved exactly.
- Appropriateness gate: YES — this is a clear win. CommandInstruction base was introduced precisely for this.

**P2 — Dead/Vestigial Code in FileTransferInstruction.CreateInstructionString** (Low)
- Lines 126-135 contain unused `TokenBuilder` construction
- Suggested refactoring: Remove the block entirely
- Risk assessment: Zero — the variable is unused. No behavior change.
- Appropriateness gate: YES — this is dead code and should be cleaned up.

**P3 — Boolean Flag Property Boilerplate (3-Tier Pattern)** (Medium)
- 3 instances: CopyInstruction (Link), AddInstruction (KeepGitDir, Link)
- Each requires 3 nearly-identical property tiers
- Suggested refactoring: No practical extraction without generic helper methods or C# source generators
- Appropriateness gate: NO — leave as-is. The pattern is intentional boilerplate — it's the cost of the 3-tier model that provides both `bool` convenience and `TFlag?` token access.

**P4 — Long Method: Dockerfile.ResolveVariables (private overload)** (Medium)
- ~60 lines, 3 levels of nesting, handles null-coalescing, stage enumeration, global arg resolution, etc.
- Suggested refactoring: Extract `ResolveStage(Stage stage, ...)` method for inner stage loop body
- Risk assessment: Medium. Variable resolution is the most complex behavioral logic in the library.
- Appropriateness gate: MAYBE — only pursue if the method needs modification for a new feature. Not worth touching speculatively.

**P5 — Near-Duplicate Whitespace Extraction Helpers** (Low)
- `GetTrailingWhitespaceToken` (lines 131-145) and `GetLeadingWhitespaceToken` (lines 151-163) in ParseHelper.cs
- Both share structure: LINQ-over-chars + null-check + return `WhitespaceToken`. Differ only in whether `.Reverse()` is applied.
- Suggested refactoring: Extract private `ExtractWhitespace(IEnumerable<char> chars)` helper
- Risk assessment: Low but noisy
- Appropriateness gate: LOW priority. Leave for opportunistic cleanup only.

#### What NOT to Change
- Flag class files (already clean after `KeywordLiteralFlag` abstraction)
- `AggregateToken.cs` (well-structured, all methods purposeful)
- `StringHelper.cs` (minimal and correct)
- `DockerfileBuilder.cs` (intentionally repetitive fluent API; duplication is user-facing API surface, not implementation)

### 2026-03-05: Lambert Test Code Analysis — Refactor Branch
**Author:** Lambert (Tester)
**Date:** 2026-03-05
**Branch:** refactor

#### Baseline
599 tests, 0 failed, 0 skipped. Duration: ~570 ms. All passing.

#### Test Code Findings (Prioritized)

**T1 — Extract RunParseTest helper to TestHelper.cs** (Impact: 39 files)
- Every single test file contains a verbatim copy of a 12-line Parse test pattern
- `FileTransferInstructionTests` already has `RunParseTest()` doing exactly this
- Suggested refactoring: Extract to `TestHelper.cs` as a generic static helper
- Risk assessment: Low — purely mechanical extraction

**T2 — Consolidate ParseTestScenario subclasses** (Impact: 37 files) — HIGHEST VALUE
- Every file defines `class XxxParseTestScenario : ParseTestScenario<Xxx> { public char EscapeChar }`. These 37 classes are structurally identical.
- Base `ParseTestScenario<T>` already exists in `TestScenario.cs` with `Text` and `ParseExceptionPosition`
- Suggested refactoring: Add `EscapeChar` to base `ParseTestScenario<T>`, remove all 37 per-file subclasses
- Risk assessment: Medium — requires updating all construction sites with named args, but change is purely additive

**T3 — Promote boolean flag validators to TokenValidator.cs** (Impact: 4 files)
- `CopyInstructionTests.ValidateLinkFlag()` and `AddInstructionTests.ValidateLinkFlag()` are identical private helpers
- Both expand to: `ValidateAggregate<LinkFlag>(token, "--link", symbol '-', symbol '-', keyword "link")`
- Suggested refactoring: Add `TokenValidator.ValidateBooleanFlag<T>(Token token, string keyword)` helper
- Risk assessment: Low

**T4 — Use ValidateKeyValueFlag<T> in NetworkFlagTests and SecurityFlagTests** (Impact: 2 files)
- Both expand the full 5-token `--{key}={value}` chain inline in every parse scenario
- `TokenValidator.ValidateKeyValueFlag<T>` already encapsulates this exact pattern
- Suggested refactoring: Use existing helper instead of inline validation
- Risk assessment: Low

**T5 — Add LinkFlagTests.cs** (Impact: consistency, 1 new file)
- `KeepGitDirFlagTests.cs` exists as a standalone test file for its flag type
- `LinkFlag` lacks an equivalent
- Suggested refactoring: Create `LinkFlagTests.cs` with parse and create scenarios
- Risk assessment: Low (new file, no risk)
- Additional: Add error path scenario to `KeepGitDirFlagTests` (e.g., `--keep-git-dir=true` should fail)

**T6 — Coverage gaps — missing error paths and integration tests** (Impact: test completeness)
- No error path tests for boolean flags (what does `--keep-git-dir=value` do when parsed directly?)
- `ChecksumFlagTests` has no test for empty value or null
- `ScenarioTests.cs` missing integration test covering ADD with all three new flags (checksum + keep-git-dir + link) in a real Dockerfile
- `AddInstructionTests.ChecksumWithVariables` covers nullable literal path but not full set-via-token-then-null cycle

#### Recommendation Priority
1. Add `EscapeChar` to `ParseTestScenario<T>` base — remove 37 subclasses (high value, low risk)
2. Extract `RunParseTest` to `TestHelper.cs` — remove 39 duplicate method bodies (high value, low risk)
3. Add `ValidateBooleanFlag<T>` to `TokenValidator.cs` — remove 2 duplicate private helpers (medium value, low risk)
4. Fill `LinkFlagTests.cs` gap (low effort, improves consistency)
5. Add `ScenarioTests.cs` integration test for multi-flag ADD (medium effort, fills real coverage gap)

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
