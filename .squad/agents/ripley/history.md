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

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-05: Refactor Branch Analysis

**Flag class hierarchy (established pattern):**
- `BooleanFlag` (new base in `BooleanFlag.cs`) — for valueless flags like `--link`, `--keep-git-dir`. Extends `AggregateToken` with `--keyword` token structure. Concrete subclasses are 23 lines each: one `const string Keyword`, two constructors, static `Parse`, static `GetParser`.
- `KeywordLiteralFlag` (new base in `KeywordLiteralFlag.cs`) — for `--key=value` flags where value is a literal. Extends `KeyValueToken<KeywordToken, LiteralToken>`. Same thin-subclass pattern. Covers: `PlatformFlag`, `IntervalFlag`, `TimeoutFlag`, `StartPeriodFlag`, `RetriesFlag`, `ChangeModeFlag`, `NetworkFlag`, `SecurityFlag`, `ChecksumFlag`.
- Flags NOT yet migrated to `KeywordLiteralFlag`: `ChangeOwnerFlag` (uses `KeyValueToken<KeywordToken, UserAccount>`, different value type — correct, should not migrate), `FromFlag` (uses `KeyValueToken<KeywordToken, StageName>`, same reason).

**CommandInstruction base class (new in `CommandInstruction.cs`):**
- `CmdInstruction`, `EntrypointInstruction`, `ShellInstruction`, `RunInstruction` all extend `CommandInstruction`.
- Centralizes: `Command` property and `ResolveVariables` override (returns `ToString()` — no variable resolution).
- `RunInstruction` still has `GetArgsParser` with mount parsing hardcoded; only the SHELL/CMD/ENTRYPOINT pattern is fully symmetric (no-mounts args, exec/shell form).

**Key file paths for flag patterns:**
- `src/Valleysoft.DockerfileModel/BooleanFlag.cs` — boolean flag base
- `src/Valleysoft.DockerfileModel/KeywordLiteralFlag.cs` — key=value flag base
- `src/Valleysoft.DockerfileModel/CommandInstruction.cs` — command instruction base
- `src/Valleysoft.DockerfileModel/ParseHelper.cs` — all combinator infrastructure (GetLeadingWhitespaceToken moved here from GenericInstruction)

**3-tier property pattern on optional-flag instructions:**
- Private `XFlag?` property (token type, `Tokens.OfType<XFlag>().FirstOrDefault()` getter, `SetOptionalFlagToken` setter)
- Public `XToken?` property (LiteralToken, routes through XFlag)
- Public `string? X` property (string, routes through XToken)
- Used in: `HealthCheckInstruction` (4 flags), `RunInstruction` (2 flags), `AddInstruction` (1 flag + 2 boolean flags), `CopyInstruction` (1 flag + 1 boolean flag).

**Duplicate `SetOptionalFlagToken` self-reference bug in `AddInstruction`:**
- `KeepGitDirFlagToken.set` calls `SetOptionalFlagToken(KeepGitDirFlag, value)`, but `KeepGitDirFlag` private property setter also calls `SetOptionalFlagToken(KeepGitDirFlag, value)`. This is correct by design — the public token property routes through the private property setter consistently.

**Naming inconsistency in `AddInstruction`:**
- Private property is `KeepGitDirFlag` / `LinkFlag` but public token property is `KeepGitDirFlagToken` / `LinkFlagToken`. In `CopyInstruction`, the private is `LinkFlag`, public is `LinkFlagToken` — same pattern. Consistent across both files.

**StringHelper.FormatKeyValueAssignment:**
- The inline quote-wrapping logic in ARG, ENV, LABEL was extracted to `StringHelper.FormatKeyValueAssignment`. Three callers consolidated.

**`GetLeadingWhitespaceToken` moved from `GenericInstruction` to `ParseHelper`:**
- Was a private static in `GenericInstruction`, moved to `internal static` in `ParseHelper`. Makes it available without duplication if needed elsewhere.

### 2026-03-05: Refactor branch analysis session

**Cross-file refactoring analysis completed (2026-03-05T15:16:02Z)**: Reviewed all 5 merged commits on refactor branch (d855eb7 through b06ba15). Confirmed 3 major architectural changes are production-ready: BooleanFlag base (2 subclasses), KeywordLiteralFlag base (9 subclasses), CommandInstruction base (4 subclasses). Analyzed 6 cross-file patterns; 3 are already optimal, 3 flagged for documentation/cleanup. Key finding: dead MountFlag parser in CmdInstruction/EntrypointInstruction should be removed pre-ship (low-medium risk). Token hierarchy stable, round-trip fidelity maintained, ParseHelper changes additive. FileTransferInstruction flag ordering design sufficient. Verdict: refactor branch ready to ship with minor cleanup. Dallas and Lambert performed parallel analyses: 5 implementation code smells identified (P1-P5), 6 test refactoring opportunities identified (T1-T6), 599 tests all passing. All findings documented with appropriateness gates in decisions.md.

### 2026-03-05: Dallas L1+L2+L3 and Lambert T1+T2+BugFix Review — APPROVED

**Review completed (2026-03-05)**: All working-tree changes reviewed against the last committed state (b06ba15). 599 tests pass, 0 build warnings.

**Dallas L1+L2 (CommandInstruction parser extraction):** Confirmed correct. `GetArgsParser` and `GetCommandParser` extracted to `CommandInstruction` as `protected static`, using `.XOr` (matching original CMD/ENTRYPOINT behavior). Dead `MountFlag.GetParser().Many()` combinator removed from both CMD and ENTRYPOINT — this was the cleanup I had flagged earlier. `RunInstruction` annotated with `private new static` for both `GetArgsParser` and `GetCommandParser`; `ShellInstruction` annotated for `GetArgsParser` only. These annotations suppress CS0108 and correctly document intentional hiding — zero build warnings confirm this. RunInstruction's `.Or` vs `.XOr` difference is a pre-existing behavioral distinction, correctly preserved. ShellInstruction's exec-form-only parser is correctly preserved.

**Dallas L3 (FileTransferInstruction dead code):** Confirmed correct. The `TokenBuilder builder = new(); builder.Keyword(...); builder.Whitespace(...); if (changeOwner) { builder.Tokens.Add(...); }` block was genuinely dead — the variable was never referenced in the return statements. Removal is zero risk.

**Lambert T2 (EscapeChar to ParseTestScenario base):** Clean. Single property added to `ParseTestScenario<T>`. Three subclasses retained that had additional properties beyond `EscapeChar`: `LiteralTokenParseTestScenario`, `KeyValueTokenParseTestScenario`, `FileTransferInstructionTests<T>` internal class (replaced with direct `ParseTestScenario<TInstruction>`). All subclass removals and call-site updates verified.

**Lambert T1 (RunParseTest in TestHelper):** Clean. `where T : AggregateToken` constraint is correct — all callers satisfy it through `Instruction > DockerfileConstruct > AggregateToken`, `IdentifierToken > AggregateToken`, and direct `AggregateToken` subclasses. The five exclusion cases (GenericInstruction, KeywordToken, LiteralToken, KeyValueToken, Comment/Whitespace/Dockerfile/ParserDirective tests) are correctly documented and left as-is.

**Lambert bug fix (UserAccount.Parse + UserAccountTests):** Sound. Root cause: the original error-path test called `ArgInstruction.Parse` instead of `UserAccount.Parse` — masking the silent-success bug. Fix: `.End()` added to standalone `Parse` only, not to `GetParser()`. Position change from `(1,1,1)` to `(1,1,5)` is correct — with `.End()`, Sprache fails at column 5 (the `:` that cannot be consumed after `user` is matched by the user-only branch). `GetParser()` composability is unaffected.

**Key Sprache patterns confirmed:**
- `.XOr` vs `.Or` distinction: base uses `.XOr` (CMD/ENTRYPOINT); RunInstruction retains `.Or` (pre-existing). Both correct for their context.
- `.End()` on standalone `Parse()` only — established as the project pattern for full-input enforcement without breaking composition.
- `private new static` is the correct C# idiom for intentional member hiding without polymorphism.

### 2026-03-05 — Refactoring execution session complete

Team update (2026-03-05T16:04:05Z): Ripley completed review verdict approving Dallas L1+L2+L3 and Lambert T1+T2+BugFix. Dallas and Lambert completed assigned work tasks. All 599 tests passing, 0 warnings, 0 errors. Code review determined all changes production-ready. Changes documented in .squad/decisions.md. Session logs created in .squad/log/ and .squad/orchestration-log/. Ready to merge and ship.

### 2026-03-05 — Formal verification PRD decomposition

**Branch:** formal-verification

**Phase 0 decomposition completed.** 8 work items defined (P0-1 through P0-8) for property-based testing with FsCheck. Key architecture decisions:

1. **Generators produce strings, not tokens.** Tests exercise the public Parse API to catch real parser bugs. Generating token trees directly would bypass the parser.
2. **P0-2 (generators) is the critical path and bottleneck.** It's the only L-sized item. All 5 property tests (P0-3 through P0-7) depend on it and can run in parallel once generators are ready.
3. **Two-level round-trip testing:** Dockerfile-level (full text) and instruction-level (per-instruction). Both are necessary — they catch different bug classes.
4. **Escape character variants are first-class concerns.** Generators must test both `\` and `` ` `` escape chars. The backtick escape (Windows Dockerfiles) has historically been an edge-case source.
5. **VariableRefToken.GetUnderlyingValue adds `$` prefix** — token tree consistency property (P0-4) must account for this override. Same for IQuotableToken quote wrapping.
6. **Variable modifier semantics have a `:` prefix distinction** — `:` changes "unset" semantics to "unset or empty". Six modifiers total: `:-`, `:+`, `:?`, `-`, `+`, `?`.

**Key file paths for Phase 0:**
- `src/Valleysoft.DockerfileModel.Tests/Valleysoft.DockerfileModel.Tests.csproj` — add FsCheck packages
- `src/Valleysoft.DockerfileModel.Tests/PropertyTests.cs` — all 5 property tests (create)
- `src/Valleysoft.DockerfileModel.Tests/Generators/DockerfileArbitraries.cs` — FsCheck generators (create)
- `src/Valleysoft.DockerfileModel/Instruction.cs` — instruction name registry (18 instruction types)
- `src/Valleysoft.DockerfileModel/Tokens/VariableRefToken.cs` — modifier syntax and resolution logic
- `src/Valleysoft.DockerfileModel/DockerfileParser.cs` — construct splitting logic (parse isolation property)
- `src/Valleysoft.DockerfileModel/Dockerfile.cs` — Parse/ToString/ResolveVariables

**Phase 1 (Lean 4 scaffold) — 5 future work items sketched (P1-1 through P1-5).** Not starting yet. Requires Lean 4 expertise not currently on team.

**Decision document:** `.squad/decisions/inbox/ripley-formal-verification-decomposition.md`

### 2026-03-05 — Phase 0 Review Gate (P0-8) — APPROVE WITH NOTES

**Branch:** formal-verification

**Review completed.** 649 tests pass (599 existing + 50 new property tests). 0 build warnings, 0 errors.

**Files reviewed:**
- `src/Valleysoft.DockerfileModel.Tests/Valleysoft.DockerfileModel.Tests.csproj` — FsCheck 3.1.0 + FsCheck.Xunit 3.1.0 correctly added
- `src/Valleysoft.DockerfileModel.Tests/Generators/DockerfileArbitraries.cs` — 645 lines, 18 instruction generators + helpers
- `src/Valleysoft.DockerfileModel.Tests/PropertyTests.cs` — 742 lines, 50 [Fact] test methods

**Test count breakdown:**
- P0-3: 22 round-trip tests (18 instruction types + Dockerfile + VariableRef + 2 line continuation variants)
- P0-4: 7 token tree consistency tests (Dockerfile + 5 instruction types + VariableRef)
- P0-5: 3 variable resolution non-mutation tests (default, with overrides, explicit false)
- P0-6: 16 modifier semantics tests (all 6 modifiers x set/unset/empty combinations)
- P0-7: 2 parse isolation tests (first and second instruction independence)

**Sample counts:** 200 samples per test (round-trip, token tree, isolation), 50 samples per modifier test. Total: ~6000 random inputs per test run.

**Findings noted for future improvement:**
1. Generator diversity for escape chars — only the line continuation generators test backtick escape. Instruction-level generators all use default backslash. Expanding escape char coverage to all 18 instruction generators would catch more edge cases.
2. HEALTHCHECK generator does not exercise `--start-period` option. Low risk since the pattern is identical to `--interval`/`--timeout`/`--retries`.
3. Token tree consistency tests cover 5 of 18 instruction types plus Dockerfile and VariableRef. Expanding to all 18 would improve coverage.
4. The `[Fact]` + `Gen.Sample()` pattern trades FsCheck's built-in shrinking for simplicity. When a failure occurs, identifying the minimal failing input requires manual bisection. For a project this size, acceptable — but worth noting if flaky failures appear.
5. `DockerfileWithVariables` generator (P0-5) only produces `$VAR` references, not braced `${VAR}` or modifier forms. Expanding would strengthen the non-mutation property.

**Architecture patterns confirmed:**
- `Gen.Sample(size, count)` with `[Fact]` is the correct FsCheck 3.x C# pattern for custom generators
- LINQ query syntax for generators preserves shrinkability through FsCheck's `Gen` combinators
- `BodyInstruction()` correctly excludes STOPSIGNAL/MAINTAINER/SHELL for Dockerfile-level composition (excludeTrailingWhitespace issue)
- Token tree consistency assertion correctly handles both `VariableRefToken.$` prefix and `IQuotableToken` quote wrapping — verified these are mutually exclusive in the type hierarchy
- Parse isolation test correctly uses `Instruction.CreateInstruction` (internal, exposed via InternalsVisibleTo) for standalone parsing and trims trailing `\n` from Dockerfile-level parse for comparison

**Decision document:** `.squad/decisions/inbox/ripley-phase0-review.md`
