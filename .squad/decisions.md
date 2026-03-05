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

### 2026-03-05: Library Cleanup L1+L2+L3
**Author:** Dallas (Core Dev)
**Date:** 2026-03-05
**Branch:** refactor

#### Summary

Three low-risk cleanup changes made to the library. All 599 tests pass with zero build warnings.

#### L1+L2 — Extract GetArgsParser/GetCommandParser to CommandInstruction; remove dead MountFlag parser

**Context:**
`CmdInstruction` and `EntrypointInstruction` both had character-for-character identical `private static GetArgsParser(char)` and `private static GetCommandParser(char)` methods. The `CommandInstruction` base class was introduced earlier on this branch specifically to hold shared CMD/ENTRYPOINT behavior, but the parser methods were not yet extracted.

Additionally, `GetArgsParser` in both classes contained a dead combinator:
```csharp
from mounts in ArgTokens(MountFlag.GetParser(escapeChar).AsEnumerable(), escapeChar).Many()
```
CMD and ENTRYPOINT do not support `--mount`. This line was silently matching zero mounts on every parse — benign but misleading and inconsistent with the instruction's actual syntax.

**Decision:**
Extract both methods to `CommandInstruction` as `protected static`, removing them from `CmdInstruction` and `EntrypointInstruction`. The extracted `GetArgsParser` omits the dead mount combinator.

`RunInstruction` and `ShellInstruction` are not changed (other than adding `private new static` to suppress CS0108 warnings). `RunInstruction.GetCommandParser` uses `.Or()` instead of `.XOr()`, and `RunInstruction.GetArgsParser` uses the `Options()` combinator pattern with mount/network/security flags. `ShellInstruction.GetArgsParser` parses exec-form only. Both are intentionally different from the base class implementation.

**Files changed:** CommandInstruction.cs (2 new protected static methods), CmdInstruction.cs (removed duplicates), EntrypointInstruction.cs (removed duplicates), RunInstruction.cs (CS0108 annotations), ShellInstruction.cs (CS0108 annotations).

#### L3 — Delete dead TokenBuilder in FileTransferInstruction.CreateInstructionString

**Context:**
`FileTransferInstruction.CreateInstructionString` constructed a `TokenBuilder` object, added items to it, then never used it. The actual return value was computed by string interpolation further down. The builder construction was vestigial code from an earlier implementation approach.

**Decision:**
Remove the dead block entirely. No behavior change.

**Files changed:** FileTransferInstruction.cs (9-line removal).

### 2026-03-05: Test Code Consolidation T1+T2
**Author:** Lambert (Tester)
**Date:** 2026-03-05
**Branch:** refactor

#### Context

The test project contained 37 per-file `ParseTestScenario` subclasses that only added a single `EscapeChar` property, and 39 identical `Parse` method bodies duplicated across test files. Both were identified in the 2026-03-05 refactoring analysis (T1 and T2 from Lambert's findings). These were purely mechanical duplications with no semantic variation.

#### T2 — Add EscapeChar to base ParseTestScenario<T>

**Decision:** Added `public char EscapeChar { get; set; }` directly to `ParseTestScenario<T>` in `TestScenario.cs` and deleted 33 per-file subclasses that only added that one property.

**Subclasses retained:**
- `LiteralTokenParseTestScenario` — has `ParseVariableRefs` (CS0108 fix applied)
- `KeyValueTokenParseTestScenario` — has `Key` (CS0108 fix applied)
- `FileTransferInstructionTests<T>` — generic nested subclass replaced with direct `ParseTestScenario<TInstruction>`

**Rationale:** 37 near-identical class definitions offer no value. The base class `ParseTestScenario<T>` already held `Text` and `ParseExceptionPosition`; `EscapeChar` is the same kind of parse-time configuration.

#### T1 — Extract RunParseTest to TestHelper.cs

**Decision:** Added a generic static `RunParseTest<T>(ParseTestScenario<T> scenario, Func<string, char, T> parseFunc) where T : AggregateToken` to `TestHelper.cs`. Updated all applicable `Parse` methods to one-liner delegation.

**Constraint choice:** `AggregateToken` (not `DockerfileConstruct`) — many tested types (flag tokens, `UserAccount`, `StageName`, `Variable`, `VariableRefToken`, etc.) extend `AggregateToken` directly without going through `DockerfileConstruct`.

**Files excluded from T1:** GenericInstructionTests, KeywordTokenTests, LiteralTokenTests, KeyValueTokenTests, CommentTests, WhitespaceTests, DockerfileTests, ParserDirectiveTests (documented in decision record).

**Special constructor-based types:** `StageNameTests` and `VariableTests` use lambda form: `(text, escapeChar) => new StageName(text, escapeChar)`.

#### Bug Fix: UserAccount.Parse does not reject "user:" (trailing colon with empty group)

**Root cause:** `UserAccount.Parse("user:", '\0')` silently succeeded by parsing `"user"` and ignoring the trailing `":"`. This occurred because Sprache's `parser.Parse(string)` does NOT require the entire input to be consumed.

**Why it was hidden:** The original test called `ArgInstruction.Parse("user:", ...)` in its error path (a pre-existing bug). `ArgInstruction.Parse` threw correctly, masking the `UserAccount.Parse` bug.

**Fix applied:**
1. `UserAccount.cs`: Added `.End()` to the standalone `Parse` method only. The `GetParser()` method (used inside `UserInstruction`) is left without `.End()`.
2. `UserAccountTests.cs`: Updated the `"user:"` error scenario's `ParseExceptionPosition` from `(1, 1, 1)` to `(1, 1, 5)`.

**Pattern established:** For standalone `Parse(string, char)` methods on `AggregateToken` types, add `.End()` to enforce full input consumption. Do NOT add `.End()` to `GetParser()` methods, which compose into larger instruction-level parsers. This matches the existing pattern in `FromInstruction.GetArgsParser`.

#### Result

All 599 tests pass. Build produces 0 warnings, 0 errors.

### 2026-03-05: Review Verdict — Dallas L1+L2+L3 and Lambert T1+T2+BugFix
**Author:** Ripley (Lead)
**Date:** 2026-03-05
**Branch:** refactor

#### Verdict: APPROVE

All changes reviewed. 599 tests pass, 0 build warnings, 0 build errors.

#### Dallas — Library Cleanup (L1+L2+L3)

**L1+L2: CommandInstruction parser extraction — APPROVED.**
- `GetArgsParser` and `GetCommandParser` extracted to `CommandInstruction` as `protected static`.
- Extracted `GetArgsParser` omits the dead `MountFlag.GetParser().Many()` combinator — CMD and ENTRYPOINT do not support `--mount`.
- Extracted `GetCommandParser` uses `.XOr` — correct for CMD and ENTRYPOINT.
- `RunInstruction.GetArgsParser` and `RunInstruction.GetCommandParser` annotated `private new static` (correct C# idiom for intentional hiding).
- `RunInstruction.GetCommandParser` uses `.Or` instead of `.XOr` — pre-existing behavioral difference correctly preserved.
- `ShellInstruction.GetArgsParser` annotated `private new static` — exec-form-only parse correctly preserved.
- Zero compiler warnings confirm `new` annotations appropriate everywhere.

**L3: FileTransferInstruction dead TokenBuilder — APPROVED.**
- Removed block was genuine dead code — variable never read before method returned.
- Zero behavior change.

#### Lambert — Test Consolidation (T1+T2) and Bug Fix

**T2: EscapeChar in ParseTestScenario base — APPROVED.**
- `EscapeChar` added to `ParseTestScenario<T>` is the correct home for this property.
- Three retained subclasses correctly identified with their additional properties.
- Default `'\0'` harmless for existing tests.

**T1: RunParseTest extracted to TestHelper — APPROVED.**
- Signature correct: `RunParseTest<T>(ParseTestScenario<T> scenario, Func<string, char, T> parseFunc) where T : AggregateToken`.
- Constraint `where T : AggregateToken` correct — all tested types satisfy it.
- Five exclusion categories correctly identified and documented.
- Lambda form for `StageName` and `Variable` correctly handles no-static-Parse pattern.

**Bug Fix: UserAccount.Parse + UserAccountTests — APPROVED. This is the most important change in the batch.**
- Root cause was dual bug: test calling wrong type, `UserAccount.Parse` not consuming input.
- Fix correct: `.End()` added to standalone `Parse` only. `GetParser()` NOT modified — must remain composable.
- Pattern matches existing codebase (`FromInstruction.GetArgsParser`).
- Position change from `(1, 1, 1)` to `(1, 1, 5)` correct with `.End()`.

#### Summary

No issues requiring changes. These changes are ready to commit.

### 2026-03-05: FsCheck Property-Based Testing Infrastructure (P0-1 + P0-2)
**Author:** Dallas (Core Dev)
**Date:** 2026-03-05
**Branch:** formal-verification

#### Context
Phase 0 of the formal verification plan requires FsCheck generators to produce random valid Dockerfile strings for property-based testing. The primary property under test is round-trip fidelity: `Parse(text).ToString() == text`.

#### Decision: FsCheck 3.x API approach for C#

**Package versions:** FsCheck 3.1.0 and FsCheck.Xunit 3.1.0.

**C# API:** FsCheck 3.x splits its API across three namespaces:
- `FsCheck` — core types (`Gen<T>`, `Arbitrary<T>`, `Property`)
- `FsCheck.Fluent` — C#-friendly static methods and LINQ extension methods for `Gen<T>` (`Select`, `SelectMany`, `Where`, `ListOf`, `ArrayOf`, `OneOf`, `Elements`, `Constant`, `Choose`, `Sample`)
- `FsCheck.FSharp` — F#-oriented API (`Prop.ForAll` takes `FSharpFunc`, not usable from C# without wrapping)

**Testing pattern chosen:** `[Fact]` with `Gen.Sample(size, count)` rather than `[Property]` attribute. The `[Property]` attribute works for simple auto-generated `Arbitrary<T>` types, but our generators produce custom `Gen<string>` values that need explicit wiring. Using `[Fact]` + `Gen.Sample(50, 200)` provides explicit control over sample count, clear failure messages, and no dependency on FsCheck's xUnit runner integration.

**Generator design:** All generators produce strings (not token trees), testing the public API. Generators use LINQ query syntax (`from ... in ... select ...`) for readability and natural shrinkability through FsCheck's built-in `Gen` combinators.

#### Decision: Dockerfile-level round-trip exclusions

Three instructions — STOPSIGNAL, MAINTAINER, SHELL — use `ArgTokens` with `excludeTrailingWhitespace: true` in their Sprache parser definitions. This causes them to NOT consume the trailing `\n` character during parsing. When these instructions appear as intermediate lines in a multi-instruction Dockerfile, the `\n` between them and the next instruction is silently dropped by `Instruction.CreateInstruction`, breaking round-trip fidelity at the Dockerfile level.

Individual instruction round-trip tests are unaffected (they don't include trailing `\n`). The Dockerfile-level body generator excludes these three types.

This is a pre-existing parser behavior, not a regression. Documenting it here for awareness.

#### Files Changed
- `src/Valleysoft.DockerfileModel.Tests/Valleysoft.DockerfileModel.Tests.csproj` (modified — added FsCheck packages)
- `src/Valleysoft.DockerfileModel.Tests/Generators/DockerfileArbitraries.cs` (new — 609 lines)
- `src/Valleysoft.DockerfileModel.Tests/PropertyTests.cs` (new — 270 lines)

#### Result
621 tests pass (599 existing + 22 new property tests). 0 build warnings, 0 errors.

### 2026-03-05: Formal Verification PRD — Work Item Decomposition
**Author:** Ripley (Lead)
**Date:** 2026-03-05
**Branch:** formal-verification
**Status:** Phase 0 ready for execution; Phase 1 future planning only

#### Phase 0 Overview
Property-based testing with FsCheck. Eight work items (P0-1 through P0-8) executed in dependency order.

**P0-1: Add FsCheck packages to test project** — Status: COMPLETE (Dallas)
- Added FsCheck 3.x and FsCheck.Xunit NuGet references
- All 599 existing tests still pass after adding
- No version conflicts with test project dependencies

**P0-2: Create DockerfileArbitraries — instruction string generators** — Status: COMPLETE (Dallas)
- FsCheck generators for all 18 instruction types
- Variable reference generators with all 6 modifiers
- Dockerfile-level composition generator
- Line continuation and escape character variants
- 609 lines, production-ready

**P0-3: Create PropertyTests — round-trip fidelity property** — Status: PENDING (Lambert)
- Implement `Parse(text).ToString() == text` property
- Use `[Property]` attribute from FsCheck.Xunit
- Test at Dockerfile and individual instruction levels
- Configure minimum 100+ inputs (MaxTest = 200 or similar)

**P0-4: Create PropertyTests — token tree consistency property** — Status: PENDING (Lambert)
- Implement `ToString() == concat(children.ToString())` property
- Walk parsed token trees recursively
- Account for overrides like `VariableRefToken` that add `$` prefix
- Uses same PropertyTests.cs file as P0-3

**P0-5: Create PropertyTests — variable resolution non-mutation property** — Status: PENDING (Lambert)
- Property: `ResolveVariables()` without `UpdateInline = true` does not mutate model
- Generate Dockerfile with ARG declarations and variable references
- Capture `ToString()` pre-resolution, call `ResolveVariables()`, assert unchanged
- Uses same PropertyTests.cs file

**P0-6: Create PropertyTests — modifier semantics property** — Status: PENDING (Lambert)
- Properties for each variable modifier: `:-`, `:+`, `:?`, `-`, `+`, `?`
- Verify "unset" vs "unset or empty" semantics for `:` variants
- Generate random variables, defaults, and resolution contexts
- Uses same PropertyTests.cs file

**P0-7: Create PropertyTests — parse isolation property** — Status: PENDING (Lambert)
- Property: parsing X+Y produces same tokens for X as parsing X alone
- Catches parser context leakage between constructs
- Generate two instructions, parse separately and together, compare
- Uses same PropertyTests.cs file

**P0-8: Review generators and property tests** — Status: PENDING (Ripley)
- Review gate before Phase 0 closure
- Verify all 599 existing tests pass
- Verify property tests run with 100+ inputs each
- Verify generator quality and diversity
- Document any findings

#### Phase 1 (Future Planning Only)

Lean 4 formal proof scaffold with five preliminary work items for later execution:

**P1-1: Lean 4 project setup and build integration**
- Create Lean 4 project structure with Lake build system
- Determine proof directory location
- Select Lean 4 toolchain version

**P1-2: Formal token model in Lean 4**
- Define Token, PrimitiveToken, AggregateToken hierarchy
- Model VariableRefToken with $ prefix behavior

**P1-3: Formal parse/toString specification in Lean 4**
- Specify round-trip theorem: `forall (text : String), toString (parse text) = text`
- Formalize parse and toString operations
- Model Sprache parser combinators or abstract them

**P1-4: Lean 4 proof of token tree consistency**
- Prove `AggregateToken.ToString == concat(children.ToString)`
- Structural induction on token tree
- Handle VariableRefToken and IQuotableToken overrides

**P1-5: CI integration for Lean 4 proofs**
- Add Lean 4 proof checking to CI pipeline
- Plan caching strategy for Lean builds

#### Architecture Notes

**Generator Design:**
1. Generators produce strings (public API testing, not internal token trees)
2. Start simple, expand coverage iteratively
3. Escape character variants (\ and `) tested throughout
4. FsCheck shrinking preserved via `Gen` combinators

**Risk Assessment:**
- P0-2 is the bottleneck (generator quality)
- Parser edge cases likely to surface in property test runs
- Variable resolution is most complex behavioral logic

#### Verdict
Phase 0 architecture is sound. P0-1 and P0-2 complete and production-ready. P0-3 through P0-7 can proceed in parallel after P0-2 (Lambert starts with P0-3 for broadest coverage). P0-8 review gate before Phase 0 closure.

### 2026-03-05T18:10:00Z: P0-4 through P0-7 Property-Based Tests
**Author:** Lambert (Tester)
**Date:** 2026-03-05
**Branch:** formal-verification

#### Summary
Completed P0-4, P0-5, P0-6, P0-7 property-based tests covering four structural properties of the Dockerfile model. Added 28 comprehensive tests across four categories. All 649 tests pass with 0 warnings.

#### Key Design Decisions

**Token tree consistency accounts for VariableRefToken and IQuotableToken**

The naive invariant `ToString() == concat(children.ToString())` does not hold universally. Two token types add content beyond their children:
- `VariableRefToken` prepends `$` via `GetUnderlyingValue` override
- `IQuotableToken` wraps in quote chars via `Token.ToString`

The P0-4 tests account for both decorations. No other `AggregateToken` subclasses override `GetUnderlyingValue`.

**Variable modifier "unset" vs "declared with null default"**

For non-colon modifiers (`-`, `+`, `?`), "set" means "key exists in the dictionary" regardless of value. Declaring `ARG x` with no default puts `{x: null}` in stageArgs, so the variable is "set" for non-colon modifiers.

For colon modifiers (`:-`, `:+`, `:?`), "set" means "key exists AND value is non-empty". So `{x: null}` counts as "unset".

Tests use `declareArg: false` (no ARG declaration) to create truly unset variables for non-colon modifier tests.

**Parse isolation uses trailing newline trimming**

When parsed as part of a multi-instruction Dockerfile, non-final instructions include a trailing `\n` (the construct delimiter). Standalone parsing does not. The P0-7 tests trim the trailing `\n` for the first instruction comparison and compare the final instruction directly (no trailing `\n`).

#### Files Changed
- `src/Valleysoft.DockerfileModel.Tests/PropertyTests.cs` (28 new tests across P0-4, P0-5, P0-6, P0-7)
- `src/Valleysoft.DockerfileModel.Tests/Generators/DockerfileArbitraries.cs` (3 new generators for variable resolution)

#### Result
649 tests pass (621 existing + 28 new). 0 build warnings, 0 errors. All formal verification phase 0 subtasks complete.

### 2026-03-05T18:00:00Z: User directive — Bug logging policy
**By:** Matt Thalman (via Copilot)
**What:** Any bugs identified during the formal verification process should be logged as GitHub issues.
**Why:** User request — captured for team memory

### 2026-03-05: Fix trailing newline loss in STOPSIGNAL, MAINTAINER, SHELL (Issue #176)
**Author:** Dallas (Core Dev)
**Date:** 2026-03-05
**Branch:** main

#### Context
STOPSIGNAL, MAINTAINER, and SHELL instructions silently dropped the trailing `\n` when parsed as part of a multi-instruction Dockerfile. This broke the library's core guarantee of character-for-character round-trip fidelity.

#### Root Cause
All three instruction parsers passed `excludeTrailingWhitespace: true` to `ArgTokens()` in their `GetArgsParser` methods. This parameter causes the parser to consume and discard trailing whitespace (including `\n`) rather than preserving it as a token. No other instruction type uses this parameter.

#### Decision
Removed `excludeTrailingWhitespace: true` from all three `GetArgsParser` methods, so they now use the default value (`false`). This aligns their behavior with every other instruction type in the codebase.

#### Rationale
- The `excludeTrailingWhitespace` parameter exists for cases where trailing whitespace is genuinely not part of the instruction's content. For these three instructions, there is no such reason — they should preserve trailing whitespace like all other instructions.
- The fix is minimal (one parameter removal per file) and carries zero risk of behavioral regression beyond the intended fix, since the parameter was only causing incorrect behavior.
- All 649 existing tests pass without modification, confirming that no test relied on the broken behavior.

#### Files Changed
- `src/Valleysoft.DockerfileModel/StopSignalInstruction.cs`
- `src/Valleysoft.DockerfileModel/MaintainerInstruction.cs`
- `src/Valleysoft.DockerfileModel/ShellInstruction.cs`

### 2026-03-05: Phase 0 Review Gate — P0-8 Verdict
**Author:** Ripley (Lead)
**Date:** 2026-03-05
**Branch:** formal-verification

#### Verdict: APPROVE WITH NOTES

Phase 0 property-based testing with FsCheck is ready to ship. All 649 tests pass (599 existing + 50 new). 0 build warnings, 0 errors.

#### What Was Reviewed

##### 1. Test Project Configuration (csproj)
- FsCheck 3.1.0 and FsCheck.Xunit 3.1.0 correctly added
- No version conflicts with existing dependencies
- Package versions are current and compatible

##### 2. Generators (DockerfileArbitraries.cs — 645 lines)
- 18 instruction generators covering all Dockerfile instruction types
- Variable reference generator with all 6 modifier forms
- Dockerfile-level composition generator with correct preamble/body structure
- Line continuation generators for both backslash and backtick escape chars
- Primitive generators (Identifier, SimpleAlphaNum, PathSegment, ImageName, etc.)
- STOPSIGNAL/MAINTAINER/SHELL correctly excluded from Dockerfile-level body composition

##### 3. Property Tests (PropertyTests.cs — 742 lines)
- **P0-3:** 22 round-trip tests verifying Parse(text).ToString() == text
- **P0-4:** 7 token tree consistency tests verifying aggregate toString == concat(children.toString) with VariableRefToken $ prefix and IQuotableToken wrapping
- **P0-5:** 3 variable resolution non-mutation tests
- **P0-6:** 16 modifier semantics tests covering all 6 modifiers across set/unset/empty states
- **P0-7:** 2 parse isolation tests verifying parsing context does not leak between instructions

#### Correctness Assessment

All properties correctly verify their stated invariants:

- **Round-trip:** Tests the exact property claimed. Each instruction type gets its own test method.
- **Token tree consistency:** The assertion logic correctly accounts for both decorating overrides (`VariableRefToken` adds `$` prefix, `IQuotableToken` adds quote wrapping). Verified these types are mutually exclusive in the hierarchy.
- **Non-mutation:** Tests three paths: default options, with overrides, and explicit `UpdateInline = false`.
- **Modifier semantics:** All 6 modifiers tested with the correct expected behavior for each state (set/unset/empty). The `declareArg` parameter correctly distinguishes "unset" from "set with null value" for non-colon modifiers.
- **Parse isolation:** Uses `Instruction.CreateInstruction` (internal, correctly exposed via InternalsVisibleTo) for standalone parsing. Correctly handles trailing `\n` difference between standalone and Dockerfile-level parsing.

#### Notes for Future Improvement

These are not blockers. Ship now, address opportunistically:

1. **Escape char coverage in generators (Dallas):** Only line continuation generators test backtick escape. All 18 instruction generators use the default backslash. Adding `escapeChar` as a generator parameter would significantly expand coverage.

2. **Token tree consistency coverage (Lambert):** Currently covers 5 of 18 instruction types plus Dockerfile and VariableRef. Expanding to all 18 instruction types would catch more structural invariant violations.

3. **HEALTHCHECK generator gap (Dallas):** Does not exercise `--start-period` option. Low risk — same pattern as the 3 options already tested.

4. **Variable reference diversity in P0-5 (Lambert):** `DockerfileWithVariables` only generates `$VAR` (simple) references. Adding `${VAR}` and `${VAR:-default}` forms would strengthen the non-mutation property.

5. **Shrinking tradeoff documented:** The `[Fact]` + `Gen.Sample()` pattern does not benefit from FsCheck's automatic shrinking. If property test failures become hard to debug, consider switching to `Prop.ForAll` with an `Arbitrary<T>` registration for the affected tests.

#### Integration Assessment

- New tests are completely additive — no existing test code was modified
- `PropertyTests` class is cleanly separated from the existing per-instruction test files
- `Generators/` subdirectory is a clean organizational choice
- Build time impact: ~9.7 seconds total (previously ~570ms for 599 tests). The 50 property tests add ~9 seconds due to 200 samples each. Acceptable for CI.

### 2026-03-05: Lean 4 Formal Verification Phase 1 — Token Model Specification
**Author:** Dallas (Core Dev)
**Date:** 2026-03-05
**Branch:** formal-verification-lean

#### Context
Phase 0 (FsCheck property-based testing) is complete and merged. Phase 1 creates the Lean 4 formal specification project that models the C# token hierarchy and proves fundamental properties about token concatenation.

#### Decisions

**1. Single recursive `toString` instead of `getUnderlyingValue` + `toString`**

The C# codebase uses two methods: `Token.GetUnderlyingValue()` (virtual, overridden in `VariableRefToken`) and `Token.ToString()` (sealed, adds quote wrapping). In the Lean model, these are combined into a single `Token.toString` function to avoid mutual recursion. The `variableRef` kind case handles the `$` prefix, and the `quoteInfo` match handles quote wrapping. This simplifies termination checking and proof structure.

**2. Token modeled as two-constructor inductive with kind tags**

Rather than a flat inductive with 13+ constructors (one per C# token subclass), we use:
- `Token.primitive (kind : PrimitiveKind) (value : String)` — 4 primitive kinds
- `Token.aggregate (kind : AggregateKind) (children : List Token) (quoteInfo : Option QuoteInfo)` — 9 aggregate kinds

This mirrors the C# two-level hierarchy and allows proofs to be stated generically over all non-variableRef aggregate kinds.

**3. Quote info as optional field rather than separate type**

`Option QuoteInfo` on the `aggregate` constructor models the C# `IQuotableToken` interface. Only `literal` and `identifier` kinds would realistically have `some` quote info, but the type system doesn't enforce this — the proofs handle all cases uniformly.

**4. Lean 4 v4.27.0 toolchain**

Pinned to the stable v4.27.0 release (January 23, 2025). This is a well-tested release with good support for nested inductive types and structural recursion over `List Token` children.

**5. Proof strategy: `unfold` + `rfl` for concrete kinds, `cases` + `simp_all` for general theorems**

Specialized theorems for individual kinds (keyword, literal, etc.) can be proved by `unfold Token.toString; rfl` because the nested matches fully reduce. The general theorem parameterized over `kind ≠ .variableRef` uses `cases kind <;> simp_all` to discharge all 9 cases.

**6. CI integration: independent `lean` job**

The lean job runs independently from the .NET build job. It installs elan, then runs `lake build` in the `lean/` directory. This keeps lean proof checking decoupled from the .NET build and avoids adding lean as a dependency of the main build.

#### Files Changed
- `lean/` — entire new directory (7 Lean files + lakefile + toolchain)
- `.github/workflows/ci.yml` — added `lean` job

#### Result
8 formal theorems proving token concatenation properties. 7 executable test suites covering primitive identity, aggregate concatenation, variableRef prefix, quote wrapping, Dockerfile concatenation, instruction name mapping, and recursive tree consistency.

### 2026-03-05: Phase 1 Review Gate — Lean 4 Token Model Specification
**Author:** Ripley (Lead)
**Date:** 2026-03-05
**Branch:** formal-verification-lean

#### Verdict: APPROVE

Phase 1 Lean 4 formal specification is correct and ready to ship. All 8 theorems are sound, all 18 instruction types are present, token type mapping is faithful to the C# hierarchy, and the CI job is properly structured.

---

#### 1. Token Type Mapping Correctness

**PrimitiveKind (4 kinds) -- CORRECT:**
- `string` -> `StringToken` -- matches
- `whitespace` -> `WhitespaceToken` -- matches
- `symbol` -> `SymbolToken` -- matches
- `newLine` -> `NewLineToken` -- matches

All 4 C# `PrimitiveToken` subclasses are represented.

**AggregateKind (9 kinds) -- CORRECT:**
- `keyword` -> `KeywordToken` -- matches
- `literal` -> `LiteralToken` (IQuotableToken) -- matches
- `identifier` -> `IdentifierToken` (IQuotableToken) -- matches
- `variableRef` -> `VariableRefToken` -- matches
- `comment` -> `CommentToken` -- matches
- `lineContinuation` -> `LineContinuationToken` -- matches
- `keyValue` -> `KeyValueToken<TKey, TValue>` -- matches
- `instruction` -> `Instruction` (via `DockerfileConstruct`) -- matches
- `construct` -> `DockerfileConstruct` -- matches

The two-constructor inductive with kind tags is a sound modeling choice. It mirrors the C# two-level hierarchy (`PrimitiveToken` vs `AggregateToken`) and avoids a flat 13+ constructor type that would complicate proofs.

**Modeling decision noted:** The Lean model does not represent every concrete C# AggregateToken subclass (e.g., flag types like `BooleanFlag`, `KeywordLiteralFlag`; domain types like `StageName`, `UserAccount`, `ImageName`, `Mount`). These are all structurally `AggregateToken` instances whose `toString` behavior follows the base concatenation rule. For Phase 1's scope (proving toString concatenation properties), this level of abstraction is correct. All those subclasses inherit `AggregateToken.GetUnderlyingValue` without overriding it.

**QuoteInfo modeling -- CORRECT:** `Option QuoteInfo` on the aggregate constructor models the C# `IQuotableToken` interface. In C#, `QuoteChar` is `char?`; when null, the string interpolation in `Token.ToString` produces just the underlying value (wrapping is invisible). The Lean `none` case correctly produces no wrapping. The model allows any aggregate kind to carry quote info, which is broader than C# (where only LiteralToken and IdentifierToken implement IQuotableToken), but the proofs handle all cases uniformly. Acceptable for Phase 1.

#### 2. Special Behavior Modeling

**VariableRefToken `$` prefix -- CORRECT:**

C# override in `VariableRefToken.GetUnderlyingValue`:
```csharp
return $"${base.GetUnderlyingValue(options)}";
```

Lean model:
```lean
let underlying := match kind with
  | .variableRef => "$" ++ childConcat
  | _ => childConcat
```

The `$` is prepended before the child concatenation, exactly matching the C# behavior. The `mkVariableRef` helper correctly notes that children do NOT include the `$`.

**IQuotableToken quote wrapping -- CORRECT:**

C# `Token.ToString`:
```csharp
if (!options.ExcludeQuotes && this is IQuotableToken quotableToken) {
    return $"{quotableToken.QuoteChar}{value}{quotableToken.QuoteChar}";
}
```

Lean model:
```lean
match quoteInfo with
| some qi => String.singleton qi.quoteChar ++ underlying ++ String.singleton qi.quoteChar
| none => underlying
```

Quote wrapping occurs after the underlying value (including `$` prefix for variableRef) is computed, matching the C# call order where `GetUnderlyingValue` runs first, then `ToString` wraps.

**Combined `toString` function -- CORRECT:**

Dallas's decision to combine `GetUnderlyingValue` and `ToString` into a single recursive `Token.toString` is the right call. It avoids mutual recursion, simplifies termination checking, and produces cleaner proofs. The function correctly handles all four cases:
1. Primitive: return value
2. Non-variableRef aggregate without quotes: concat children
3. VariableRefToken without quotes: `$` + concat children
4. Any aggregate with quotes: wrap (underlying) in quote chars

#### 3. Instruction Coverage -- CORRECT

All 18 instruction types present with correct keyword mappings:

| Lean Variant | C# Class | Keyword |
|---|---|---|
| from | FromInstruction | FROM |
| run | RunInstruction | RUN |
| cmd | CmdInstruction | CMD |
| entrypoint | EntrypointInstruction | ENTRYPOINT |
| copy | CopyInstruction | COPY |
| add | AddInstruction | ADD |
| env | EnvInstruction | ENV |
| arg | ArgInstruction | ARG |
| expose | ExposeInstruction | EXPOSE |
| volume | VolumeInstruction | VOLUME |
| user | UserInstruction | USER |
| workdir | WorkdirInstruction | WORKDIR |
| label | LabelInstruction | LABEL |
| stopSignal | StopSignalInstruction | STOPSIGNAL |
| healthCheck | HealthCheckInstruction | HEALTHCHECK |
| shell | ShellInstruction | SHELL |
| maintainer | MaintainerInstruction | MAINTAINER |
| onBuild | OnBuildInstruction | ONBUILD |

The `all_length` theorem (`all.length = 18`) proved by `native_decide` is a nice completeness check.

#### 4. Proof Soundness -- CORRECT

All 8 formal theorems (plus 2 auxiliary) are correctly stated and proved:

1. **`token_toString_aggregate`** -- General theorem for non-variableRef aggregates without quotes. Proved by `unfold; cases kind <;> simp_all`. Sound: exhausts all 9 AggregateKind cases, hypothesis `hkind` eliminates `variableRef`.

2. **`token_toString_keyword`**, **`token_toString_literal`**, **`token_toString_comment`**, **`token_toString_instruction_kind`** -- Specialized versions for specific kinds. All proved by `unfold; rfl`. Sound: these are definitional equalities that reduce directly.

3. **`token_toString_variableRef`** -- VariableRef prepends `$`. Proved by `unfold; rfl`. Sound.

4. **`token_toString_quoted`** -- Quote wrapping for non-variableRef. Proved by `unfold; cases kind <;> simp_all`. Sound.

5. **`token_toString_primitive`** -- Primitive returns value. Proved by `unfold; rfl`. Sound.

6. **`dockerfile_toString_concat`** -- Dockerfile toString = concat of constructs. Proved by `unfold; rfl`. Sound.

7. **`token_toString_variableRef_quoted`** -- VariableRef with quotes wraps the `$`-prefixed value. Proved by `unfold; rfl`. Sound.

**Key verification:** The proofs match the actual property we want to prove -- that `AggregateToken.ToString()` equals the concatenation of children's `ToString()` results, with the two documented exceptions (VariableRefToken prefix and IQuotableToken wrapping). This is the fundamental structural invariant of the C# token hierarchy.

#### 5. SlimCheck Tests -- ACCEPTABLE WITH NOTE

The file contains 7 test suites with concrete examples covering:
1. Primitive token identity (6 cases)
2. Aggregate concat (6 cases)
3. VariableRef `$` prefix (4 cases including modifier forms)
4. Quote wrapping (4 cases)
5. Dockerfile concatenation (3 cases)
6. Instruction name mapping (18 cases)
7. Recursive token tree consistency (2 complex cases)

**NOTE:** Despite the filename `SlimCheck.lean`, these tests do NOT use Lean's SlimCheck library for property-based testing. They are deterministic IO-based tests with handcrafted examples. The file header correctly explains this design choice (recursive Token type makes SlimCheck derivation complex). This is honest and pragmatic.

**IMPORTANT:** These tests will NOT execute during `lake build`. The lakefile defines only a `lean_lib` target, not a `lean_exe`. The `main` function in `SlimCheck.lean` is type-checked but never called. The tests serve as type-level specifications (confirming the function calls are well-formed) but do not verify runtime behavior in CI.

This is acceptable because:
- The formal proofs in `Proofs/TokenConcat.lean` ARE checked during `lake build` -- Lean verifies all theorem statements during elaboration
- The tests are supplementary examples, not the primary verification mechanism
- The proofs already cover the properties these tests exercise

**Future improvement:** Add a `lean_exe` target for the test runner and a CI step to execute it. This would provide runtime verification of the concrete examples in addition to the formal proofs.

#### 6. CI Integration -- CORRECT

The `lean` job is properly structured:
- Runs on `ubuntu-latest` -- correct for Lean 4
- Installs elan via the official `elan-init.sh` script -- standard method
- Adds `~/.elan/bin` to `$GITHUB_PATH` -- correct for subsequent steps
- Runs `~/.elan/bin/lake build` with `working-directory: lean` -- correct, uses full path as belt-and-suspenders
- Independent from the `build` job -- correct, no unnecessary coupling
- The `defaults.run.working-directory: src` applies to the Install elan step, but the `curl` and `echo` commands are directory-independent, so this is harmless

**Minor note:** The elan install URL is not pinned to a specific commit hash (unlike `actions/checkout` which pins `de0fac2e...`). This is standard practice for elan installations but could be pinned for reproducibility.

#### 7. Lean 4 Idioms -- CORRECT

- **Toolchain:** v4.27.0 (January 2026) -- stable release, good choice
- **`autoImplicit := false`** -- good practice for formal verification, prevents implicit variable introduction
- **Inductive type design:** Two-constructor with kind tags is idiomatic Lean 4. `deriving Repr, BEq, Inhabited` is appropriate. `DecidableEq` on `AggregateKind` enables the `cases kind <;> simp_all` proof strategy.
- **Recursive `toString`:** Structural recursion over `List Token` children via `List.map`. Lean 4 handles nested inductive types well for this pattern.
- **Proof tactics:** `unfold + rfl` for definitional equalities and `cases + simp_all` for case-split reasoning -- both are standard Lean 4 proof patterns.
- **Namespace organization:** `DockerfileModel` namespace with `Token`, `Instruction`, `Dockerfile` sub-namespaces -- clean and idiomatic.
- **Structure definitions:** `Instruction`, `DockerfileConstruct`, `Dockerfile`, `QuoteInfo` as structures with fields -- appropriate use of Lean 4 structures.

#### Summary

The implementation is correct and faithful to the C# token hierarchy. The formal proofs are sound and cover the key structural invariant (toString = concat children, modulo VariableRefToken and IQuotableToken). The CI integration is properly structured.

**Items for future phases (not blockers):**
1. Add `lean_exe` target + CI step to execute the test runner
2. Consider adding proofs about instruction-level structure (keyword + whitespace + args)
3. Consider proving properties about ConstructType discrimination
4. Pin elan install URL to a specific commit hash for reproducibility

### 2026-03-05: Architecture Decision: Lean 4 Parser Combinator Design for Phase 2
**Author:** Ripley (Lead)
**Date:** 2026-03-05
**Status:** Adopted

#### Context
Phase 1 is complete with formal token/instruction/dockerfile models. Phase 2 must build a parser translating Dockerfile text to these types, enabling round-trip theorems. The C# parser uses Sprache (monadic parser combinators). This decision documents the Lean 4 equivalent architecture.

#### Decision 1: Use Lean 4 Built-in `Parsec`
**Choice:** Build on `Lean.Parsec` (built-in parser combinator in Lean's standard library).

**Rationale:** Zero external dependencies. API is clean: `pure`, `bind`, `orElse`, `attempt`, `satisfy`, `pchar`, `pstring`, `many`, `eof`. Two-constructor `ParseResult` (success/error) is amendable to proofs. `attempt` provides backtracking (maps to Sprache `.Or`). Bare `<|>` without `attempt` maps to `.XOr` (committed choice).

#### Decision 2: Parser Output Type
**Choice:** All parsers return `Parsec Token` or `Parsec (List Token)`, producing existing `Token` inductive.

**Rationale:** The C# parser produces `IEnumerable<Token>` at every level. No intermediate AST — `Token` IS the AST, matching C# design. Per-instruction parsers return `Parsec (List Token)` (flat child list), wrapped in `Token.aggregate .instruction children none`.

#### Decision 3: Module Structure
**Choice:** Flat structure under `lean/DockerfileModel/Parser/`, one file per concern:
- Basic.lean (core combinators)
- Tokens.lean (token-level parsers)
- Instruction.lean (dispatch + generic parser)
- From.lean, Arg.lean, Run.lean, SimpleInstructions.lean, Command.lean, FileTransfer.lean, HealthCheck.lean (per-instruction)
- Dockerfile.lean (top-level parser)

**Rationale:** Mirrors C# file organization. Basic.lean is critical (whitespace, line continuation, quote wrapping) — everything depends on it.

#### Decision 4: Combinator Mapping
**Sprache → Lean 4 Parsec:**
- `from...in...select` → `do...let <- ...return`
- `.Or(other)` → `attempt p <|> q`
- `.XOr(other)` → `p <|> q`
- `.Many()` / `.AtLeastOnce()` → `many` / `many1`
- `.Optional()` → `optional` combinator
- `.Except(excluded)` → custom `except` combinator

#### Decision 5: Hard Parts Handling
- **Escape character:** Thread as `ParserConfig` parameter (not reader monad, keep proofs simple)
- **Whitespace preservation:** Every space/tab/newline becomes token; line continuations are `LineContinuationToken` children
- **Variable references:** `$VAR` and `${VAR:-default}` via `attempt` backtracking
- **Quote wrapping:** Try single/double/unwrapped via backtracking
- **Instruction keywords:** Case-insensitive parsing with optional line continuations between characters

#### Decision 6: Round-Trip Theorem Strategy
**Bottom-up, per-combinator approach:**
1. Prove primitive parsers produce tokens whose `toString` equals consumed input
2. Prove composite combinators preserve round-trip when each sub-parser does
3. Prove instruction parsers produce aggregate tokens matching input
4. Prove Dockerfile parser preserves full text

Prioritize FROM first (exercises keywords, whitespace, literals, variables, flags, staging). Extract reusable lemmas. Extend to simpler instructions.

#### Decision 7: Implementation Order
1. Basic.lean — Core combinators (foundation)
2. Tokens.lean — Token-level parsers
3. From.lean — First instruction
4. RoundTrip.lean — Primitive + FROM proofs
5. SimpleInstructions.lean — WORKDIR, USER, EXPOSE, etc.
6. Arg.lean → Command.lean → FileTransfer.lean → HealthCheck.lean
7. Run.lean (most complex)
8. Instruction dispatch + Dockerfile parser

#### Decision 8: Lakefile
No changes needed. `lean_lib` auto-discovers modules in subdirectories. No new Lake dependencies — `Lean.Parsec` is built-in.

#### Risks & Mitigations
- Termination checking: Use `partial` for recursive parsers; defer proofs to Phase 3
- String.Iterator proofs hard: Focus on combinator composition level, not iterator internals
- Full round-trip proof too large: Scope Phase 2 to FROM + 3-4 simple instructions
- Parsec API location: Verify import at build time; fallback: copy 50-line definition

#### Summary
Use `Lean.Parsec`, produce `Token` directly, flat module structure. Mirror C# file organization. Bottom-up round-trip proofs starting with FROM. Key insight: Sprache LINQ maps perfectly to Lean `do` notation — focus effort on whitespace/line-continuation machinery in Basic.lean.

---

### 2026-03-05: Implementation: Lean 4 Parser Combinator Library (Phase 2)
**Author:** Dallas (Core Dev)
**Date:** 2026-03-05
**Status:** Complete

#### Decision Summary
Implemented custom `Parser α := Position -> ParseResult α` monad (zero external dependencies) producing `Token` values. Three-tier module organization: Basic (monad/core combinators), Combinators (higher-level patterns), DockerfileParsers (Dockerfile-specific). Used `partial` for recursive parsers. Stated round-trip theorems with `sorry`.

#### Implementation
**Files created (1462 lines total):**
1. **Basic.lean** (327 lines) — Monad `Parser α`, combinators: `position`, `satisfy`, `pchar`, `pstring`, `whitespace`, `optionalNewLine`, `lineContinuation`, `symbol`, `concatTokens`, `argTokens`, `optional`, `except`
2. **Combinators.lean** (172 lines) — `many`, `many1`, `sepBy`, `between`, `tryParse`, `orBacktrack`, `wrappedInQuotes`, `wrappedInOptionalQuotes`
3. **DockerfileParsers.lean** (620 lines) — `keywordString`, `keywordToken`, `literalString`, `literalToken`, `literalWithVariables` (recursive), `variableRef`, `identifierToken`, `commentToken`, `whitespaceToken`, `newLineToken`
4. **FromParser.lean** (107 lines) — FROM instruction with platform flag, image name, AS staging
5. **ArgParser.lean** (91 lines) — ARG instruction with key=value or key-only parsing
6. **RoundTripProofs.lean** (145 lines) — Theorems for `fromInstruction_roundTrip` and `argInstruction_roundTrip` (stated with `sorry`), helper lemmas

**Files modified:** DockerfileModel.lean (root imports to expose parser API)

#### Technical Decisions (Implementation)
1. **Custom Parser monad:** Zero dependencies. Simple two-constructor ParseResult.
2. **Token-producing:** All parsers return `Token` values to preserve structure for proofs.
3. **Three-tier modules:** Basic (monad), Combinators (patterns), DockerfileParsers (app-specific).
4. **`partial` keyword:** For `many`, `bracedVariableRef`, `literalWithVariablesUnquoted` — termination by progress is guaranteed but hard to prove structurally.
5. **Round-trip theorems with `sorry`:** Statements are precise; proofs deferred to Phase 3+.

#### Deliverables
- `parseFrom` and `parseArg` API functions ready for Phase 3 differential testing
- Round-trip theorem statements provide acceptance criteria for proof work

---

### 2026-03-05: Test Suite: Parser Tests for FROM and ARG Instructions
**Author:** Lambert (Tester)
**Date:** 2026-03-05
**Status:** Complete

#### Decision Summary
Two-tier test strategy: (1) Active token-tree construction tests validating Lean token model with all edge cases; (2) Commented parser stubs providing acceptance criteria for Dallas's parser. Shared test data enables straightforward integration when parser matures.

#### Test Suite: ParserTests.lean (1180 lines)

**Active Token-Tree Construction Tests: 48 tests**
- FROM (28 tests): image name, tag, digest, platform flag, AS clause, line continuations, quotes, variables, comments, case insensitivity, whitespace variations, complex combinations
- ARG (20 tests): simple declaration, with default, multiple args, case insensitivity, whitespace, comments, continuations, variables in defaults, quoted values

Each test:
- Constructs expected token tree using `Token.mk*` helpers
- Asserts `Token.toString` equals original input string
- Validates token model without requiring parser completion

**Commented Parser Stubs: 18 stubs**
- Full parser integration tests using `Parser.parseFrom`, `Parser.parseArg`, `Parser.parseDockerfile`
- Round-trip verification on parsed output
- Error path tests (malformed syntax, missing fields)
- Marked with `-- [PARSER]` comments for identification
- Ready to uncomment when Dallas's parser integrated

#### Test Strategy Rationale
- Token tree tests exercise model immediately with all edge cases
- Parser stubs give Dallas concrete acceptance criteria
- Shared test data allows uncommenting stubs for parser integration
- Both tiers validate round-trip fidelity

#### Files Modified
- SlimCheck.lean — Added `ParserTests` module to main test runner

#### Test Quality
- Comprehensive coverage of documented FROM and ARG syntax variations
- Explicit handling of tricky cases (line continuations, variable refs, escaping)
- Well-organized by instruction type with explanatory comments

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction

---

# Decision: Phase 4 Variable Resolution Lean 4 Implementation

**Author:** Dallas (Core Dev)
**Date:** 2026-03-05
**Phase:** Phase 4 — Variable Resolution Proofs in Lean 4

## Context

Implemented the formal Lean 4 model and proofs for Dockerfile variable resolution semantics, covering:
- `VariableRefToken.ResolveVariables` (C#, lines 121-196)
- `Dockerfile.ResolveVariables` scoping rules (C#, lines 97-160)

## Decisions

### 1. Association List (VarMap) as Variable Map

**Decision:** Use `List (String × String)` aliased as `VarMap` rather than `Std.HashMap`.

**Rationale:**
- `List.lookup` unfolds completely in Lean proofs (no external axioms)
- No Std library dependency needed
- Structural induction works cleanly on lists
- `simp` knows how to simplify `List.lookup` with concrete hypotheses

**Tradeoff:** O(n) lookup vs O(1) HashMap, but this is a specification model not an implementation — correctness matters more than performance.

### 2. `Except String String` Return Type for `resolve`

**Decision:** `resolve` returns `Except String String` (`.error msg` | `.ok value`).

**Rationale:** The `?` and `:?` modifiers throw `VariableSubstitutionException` in C#. Modeling this as `Except` makes the error path explicit in the type, enabling proofs that state "this modifier errors when..." precisely.

**Alternative considered:** `Option String` — rejected because it conflates "unset error" with "no value", losing the error message content.

### 3. `isVariableSet` Extracted as Standalone Function

**Decision:** The colon vs non-colon distinction is a standalone `isVariableSet : VarMap → String → Modifier → Bool` rather than inlined in `resolve`.

**Rationale:** Individual modifier theorems (e.g., `dash_setEmpty`) need to reason about `isVariableSet` independently. Extraction makes these proofs cleaner and allows the colon/non-colon lemmas to be reused.

### 4. `processEscapes` as Top-Level Private Def

**Decision:** Extract the escape character processing loop as `private def processEscapes (escapeChar : Char) : List Char → List Char` with explicit `termination_by cs => cs.length` and `decreasing_by all_goals (simp only [List.length_cons]; omega)`.

**Rationale:** `let rec` inside `formatValue` caused termination proving difficulties because Lean couldn't see the structural decrease for the "single escape before other char" case. Top-level extraction with the three-constructor pattern `[] | [c] | c :: next :: rest` makes all three recursive calls obviously decreasing.

### 5. `unfold` + `rw` Proof Pattern (NOT plain `simp`)

**Decision:** Use `rw [find_eq_lookup] at h; unfold resolve isVariableSet VarMap.find?; rw [h]; simp` pattern for all modifier proofs.

**Rationale:** `simp [resolve, isVariableSet, VarMap.find?, h]` does not work because `VarMap.find?` unfolds to `vars.lookup name` (dot-notation method call) which doesn't match the `List.lookup name vars` function-call form that appears in the goal after unfolding. The bridge lemma `find_eq_lookup : vars.find? name = vars.lookup name` converts hypotheses to the right form, then `rw` substitutes into the unfolded goal.

**Lesson:** In Lean 4, method-style `obj.method` and function-style `Namespace.method obj` are definitionally equal but may not be transparently simplified by `simp`. Always unfold both sides to `List.lookup` before using hypotheses.

### 6. One `sorry` in Non-Mutation Invariant (Per Spec)

**Decision:** `resolve_token_toString_unchanged` uses `sorry`.

**Rationale:** The full C# non-mutation invariant (`updateInline = false` means Token.toString is unchanged) would require modeling the full C# Token tree mutation infrastructure. Our Lean model is purely functional — `resolve` has type `VarMap → VariableRef → Except String String` and structurally cannot modify any Token. The `sorry` documents this modeling gap, as specified in the task.

## Files Created

- `lean/DockerfileModel/VariableResolution.lean`
- `lean/DockerfileModel/Scoping.lean`
- `lean/DockerfileModel/Proofs/VariableResolution.lean`

## Files Modified

- `lean/DockerfileModel.lean` (added 3 imports)

## Build Verification

`cd lean && lake build` passes with 0 errors. 1 intentional `sorry`.
