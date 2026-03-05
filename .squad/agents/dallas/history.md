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

### 2026-03-04 — Boolean flag pattern for COPY --link (Issue #115)

**Boolean flag token class**: A valueless flag like `--link` is represented as an `AggregateToken` subclass containing three tokens: `SymbolToken('-')`, `SymbolToken('-')`, `KeywordToken("link")`. There is no `KeyValueToken` base because there is no `=value`. The parser is built inline from `Symbol('-').AsEnumerable()` twice then `KeywordToken.GetParser(...)`.

**Key file**: `src/Valleysoft.DockerfileModel/LinkFlag.cs` — new file for the `--link` boolean flag token.

**CopyInstruction flag ordering**: When constructing a COPY instruction programmatically, the required flag order is: `--from` (if any), `--chown` (if any), `--chmod` (if any), `--link` (if any). The `CreateInstructionString` method in `FileTransferInstruction` takes a leading `optionalFlag` (before chown/chmod) and now also a `trailingOptionalFlag` (after chmod). COPY uses `fromFlag` as the leading optional and `linkFlag` as the trailing optional.

**Parser supports any order**: The Sprache `GetInnerParser` in `FileTransferInstruction.GetArgsParser` accepts flags in any order (via `.Many()` and `.Optional()` combinators), so round-trip fidelity is preserved regardless of the order flags appear in the source Dockerfile.

**Test files**: Pre-existing tests for `--link` already existed in `CopyInstructionTests.cs` and `DockerfileBuilderTests.cs`. These tests drove the implementation — they define the expected API surface and flag ordering.

### 2026-03-05 — COPY --link implementation complete (Issue #115)

**Team update (2026-03-05T04:13:15Z)**: Dallas completed LinkFlag implementation. All 532 tests pass. Lambert added comprehensive test coverage. Decisions documented in decisions.md. Ready for review.

**Key paths updated**:
- `src/Valleysoft.DockerfileModel/LinkFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/CopyInstruction.cs`
- `src/Valleysoft.DockerfileModel/FileTransferInstruction.cs`
- `src/Valleysoft.DockerfileModel/DockerfileBuilder.cs`

### 2026-03-04 — RUN --network and --security options (Issue #116)

**Key-value flag token pattern**: `--network=<value>` and `--security=<value>` are key-value flags, so they follow the exact `KeyValueToken<KeywordToken, LiteralToken>` pattern used by `PlatformFlag`, `IntervalFlag`, etc. Each gets its own class (`NetworkFlag.cs`, `SecurityFlag.cs`) with the standard `Parse`, `GetParser`, and internal token constructor.

**RunInstruction property pattern**: Followed the HealthCheckInstruction 3-tier property pattern for optional flags: `string? Network` -> `LiteralToken? NetworkToken` -> `private NetworkFlag? NetworkFlag`. Each tier delegates to the tier below using `SetOptionalLiteralTokenValue` and `SetOptionalKeyValueTokenValue` from `AggregateToken`. This requires storing `escapeChar` as a field on RunInstruction.

**Parser refactored to Options() pattern**: The `GetArgsParser` method was refactored from a dedicated mount-only `from mounts in ...` pattern to an `Options()` combinator pattern (like HealthCheckInstruction) that accepts `MountFlag`, `NetworkFlag`, or `SecurityFlag` in any order via `.Many().Flatten()`. This preserves round-trip fidelity for any flag ordering.

**Constructor overload collapse**: Removed the intermediate overloads `(string, IEnumerable<Mount>, char)` and `(string, IEnumerable<string>, IEnumerable<Mount>, char)` to avoid ambiguity with the new overloads that add `string? network, string? security` as optional parameters. The optional-parameter overloads subsume the old ones.

**GetFlagArgs replaced CreateMountFlagArgs**: The `CreateMountFlagArgs` method was replaced with `GetFlagArgs` (using `StringBuilder`, matching `HealthCheckInstruction.GetOptionArgs`), which appends all flag types in order: mounts, then network, then security.

**Key files created/modified**:
- `src/Valleysoft.DockerfileModel/NetworkFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/SecurityFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/RunInstruction.cs` (modified)
- `src/Valleysoft.DockerfileModel/DockerfileBuilder.cs` (modified)

### 2026-03-05 — RUN --network and --security implementation complete (Issue #116)

**Team update (2026-03-05T04:38:40Z)**: Dallas completed NetworkFlag and SecurityFlag implementation with 3-tier property pattern and Options() parser refactor. All 532 pre-existing tests pass. Lambert wrote 25 comprehensive tests (NetworkFlagTests, SecurityFlagTests, RunInstructionTests updates). Full suite: 557 tests pass. Decisions documented in decisions.md. Ready for review.

### 2026-03-05 — ADD --checksum, --keep-git-dir, --link options (Issue #103)

**Single constructor for AddInstruction**: Unlike CopyInstruction which kept a backward-compatible positional constructor, AddInstruction was refactored to a single constructor with all optional parameters. The base-class `FileTransferInstructionTests` factory lambda was updated to use named args (`changeOwner:`, `permissions:`, `escapeChar:`) to avoid overload ambiguity — matching the pattern CopyInstruction already uses.

**KeywordToken parses hyphens fine**: `KeywordToken.GetParser("keep-git-dir", escapeChar)` works correctly. The `StringToken` helper in ParseHelper uses `Parse.IgnoreCase` for each character sequentially, so hyphens inside keyword names parse without issue.

**optionalFlagParser for multiple ADD flags**: Rather than refactoring to the full Options() combinator pattern (as was done for RUN), ADD uses the existing `optionalFlagParser` mechanism in `FileTransferInstruction.GetInnerParser`. The three new ADD flags (ChecksumFlag, KeepGitDirFlag, LinkFlag) are chained with `.Or()` and passed as the optional parser. This keeps the change minimal and contained.

**Flag ordering in CreateInstructionString**: `--checksum` is passed as `optionalFlag` (leading, before chown/chmod). `--keep-git-dir` and `--link` are combined into a single `trailingOptionalFlag` string (after chmod). This reuses the existing two-slot signature without requiring further changes to `FileTransferInstruction`.

**ChecksumFlag**: `KeyValueToken<KeywordToken, LiteralToken>` — exact same pattern as NetworkFlag/SecurityFlag. Supports variable references in value (uses `LiteralWithVariables`).

**KeepGitDirFlag**: `AggregateToken` — same pattern as LinkFlag. Three inner tokens: `SymbolToken('-')`, `SymbolToken('-')`, `KeywordToken("keep-git-dir")`.

**LinkFlag reuse**: No new class needed. ADD wires in the existing `LinkFlag` class from CopyInstruction, following the same property pattern.

**Key files created/modified**:
- `src/Valleysoft.DockerfileModel/ChecksumFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/KeepGitDirFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/AddInstruction.cs` (modified — single constructor, 3 new flag properties)
- `src/Valleysoft.DockerfileModel/DockerfileBuilder.cs` (modified — AddInstruction builder extended)
- Test files: AddInstructionTests.cs, ChecksumFlagTests.cs, KeepGitDirFlagTests.cs, DockerfileBuilderTests.cs (pre-written by Lambert, constructor lambda updated)

**Test count**: 557 → 599 (42 new tests). All pass.

### 2026-03-05 — Code smell analysis of refactor branch

**Scope**: Analyzed all files in `src/Valleysoft.DockerfileModel/` against 8 smell categories.

**Key findings**:

**Resolved by refactor branch (already fixed)**:
- Flag classes (ChangeModeFlag, ChecksumFlag, IntervalFlag, LinkFlag, KeepGitDirFlag, NetworkFlag, PlatformFlag, RetriesFlag, SecurityFlag, StartPeriodFlag, TimeoutFlag) previously each had fully duplicated Parse/GetParser logic. The `BooleanFlag` and `KeywordLiteralFlag` base classes added on this branch eliminated all that duplication. Each concrete flag class is now 24 lines, clean, with no duplicated logic.
- CMD/ENTRYPOINT previously had identical `GetArgsParser` + `GetCommandParser` methods. The `CommandInstruction` base class added on this branch pulls up the shared `Command` property and `ResolveVariables` override. However, the two private parser methods remain duplicated between CmdInstruction and EntrypointInstruction (not yet extracted to base).

**Remaining smells**:

1. **Near-duplicate GetArgsParser/GetCommandParser** in `CmdInstruction.cs` and `EntrypointInstruction.cs` — identical implementations, could be extracted to `CommandInstruction`.

2. **Boolean flag property boilerplate** — The 3-property tier pattern for boolean flags (public `bool`, public `TFlag?`, private `TFlag?`) in `CopyInstruction` and `AddInstruction` is structurally identical for each flag (`Link`, `KeepGitDir`). No obvious extraction path without generics or source generators.

3. **`CreateInstructionString` in `FileTransferInstruction`** builds a `TokenBuilder` object that is never used (lines 126-129) — the string formatting is done separately. Dead/vestigial code in lines 126-129.

4. **`Dockerfile.ResolveVariables` private method** (lines 97-160) is 60+ lines and does multiple things: stage iteration, arg accumulation, instruction resolution, and inline update. Long method, medium extraction risk.

5. **`GetTrailingWhitespaceToken` / `GetLeadingWhitespaceToken`** in `ParseHelper.cs` — structurally near-identical (one uses `.Reverse().TakeWhile().Reverse()`, the other just `.TakeWhile()`).

**Key file paths for flag architecture**:
- `src/Valleysoft.DockerfileModel/BooleanFlag.cs` — base for valueless flags
- `src/Valleysoft.DockerfileModel/KeywordLiteralFlag.cs` — base for --key=value flags
- `src/Valleysoft.DockerfileModel/CommandInstruction.cs` — shared base for CMD/ENTRYPOINT/SHELL/RUN

### 2026-03-05 — Refactor branch analysis session complete

**Team update (2026-03-05T15:16:02Z)**: Ripley completed cross-file refactoring analysis of refactor branch. Verdict: production-ready. Analyzed 6 architectural patterns; 3 already optimal, 3 flagged for documentation or cleanup. One low-medium risk actionable finding: remove dead MountFlag parser in CmdInstruction/EntrypointInstruction. Dallas and Lambert performed parallel code smell and test analysis. 5 implementation findings prioritized (P1-P5): extract Cmd/Entrypoint parsers, remove dead TokenBuilder, document flag patterns. 6 test findings prioritized (T1-T6): consolidate 37 ParseTestScenario subclasses, extract 39 RunParseTest methods, consolidate flag validators, fill LinkFlagTests gap. All decisions documented in decisions.md.

### 2026-03-05 — Library cleanup L1+L2+L3 complete

**What changed:**

**L1+L2 (combined) — Extract shared parsers to CommandInstruction, remove dead mount code:**
- Added `protected static GetArgsParser(char escapeChar)` and `protected static GetCommandParser(char escapeChar)` to `CommandInstruction.cs`. The new `GetArgsParser` is the cleaned version (no `MountFlag.GetParser().Many()` dead code). Added `using static Valleysoft.DockerfileModel.ParseHelper;` to `CommandInstruction.cs`.
- Removed the now-duplicate `private static GetArgsParser` and `private static GetCommandParser` from both `CmdInstruction.cs` and `EntrypointInstruction.cs`. Both subclasses' `GetInnerParser` now resolve `GetArgsParser(escapeChar)` via inheritance to the base class.
- `RunInstruction` and `ShellInstruction` have their own `GetArgsParser`/`GetCommandParser` with different behavior (`RunInstruction` uses `.Or()` not `.XOr()`; `ShellInstruction` parses exec-form only). Added `private new static` to both to explicitly suppress the CS0108 hiding warnings.
- The dead code was: `MountFlag.GetParser(escapeChar).AsEnumerable(), escapeChar).Many()` — CMD and ENTRYPOINT never supported mounts. This was silently matching zero mounts on every parse without benefit.

**L3 — Delete dead TokenBuilder in FileTransferInstruction.CreateInstructionString:**
- Removed lines 126-135 of `FileTransferInstruction.cs`: the `TokenBuilder builder = new()` construction and population that was built but never referenced. The return value came from string interpolation below it. Zero behavior change.

**Key invariant verified:** All 599 tests pass. Round-trip fidelity unchanged.

**Lesson on `private new static`:** When a base class adds a `protected static` method with the same name as an existing `private static` in a derived class, C# emits CS0108. The fix is `private new static` on the derived class method. This is purely a compiler annotation — `private` methods are never virtually dispatched regardless. The `new` keyword here is purely to suppress the warning cleanly.

### 2026-03-05 — Refactoring execution session complete

Team update (2026-03-05T16:04:05Z): Dallas completed L1+L2+L3 library cleanup. Lambert completed T1+T2 test consolidation + UserAccount.Parse bugfix. Ripley completed full code review and approved all changes. All 599 tests passing. Changes documented in .squad/decisions.md. Session logs created in .squad/log/ and .squad/orchestration-log/. Production-ready to merge and ship.

### 2026-03-05 — FsCheck property-based testing infrastructure (P0-1 + P0-2)

**P0-1: FsCheck packages added.**
Added `FsCheck 3.1.0` and `FsCheck.Xunit 3.1.0` to `src/Valleysoft.DockerfileModel.Tests/Valleysoft.DockerfileModel.Tests.csproj`. Build and all 599 existing tests pass unchanged.

**P0-2: DockerfileArbitraries generators created.**
Created `src/Valleysoft.DockerfileModel.Tests/Generators/DockerfileArbitraries.cs` with FsCheck 3.x Gen-based generators for:
- All 18 instruction types (FROM, RUN, CMD, ENTRYPOINT, COPY, ADD, ENV, ARG, EXPOSE, HEALTHCHECK, LABEL, MAINTAINER, ONBUILD, STOPSIGNAL, USER, VOLUME, WORKDIR, SHELL)
- Variable references ($VAR, ${VAR}, ${VAR:-default}, ${VAR:+alt}, ${VAR:?err}, etc.)
- Complete valid Dockerfiles (preamble + FROM + body instructions)
- Line continuation variants (backslash and backtick)
- Primitive generators: Identifier, SimpleAlphaNum, PathSegment, ImageName, StageName, PortNumber, Signal, Duration, AbsolutePath

**PropertyTests.cs created** with 22 property-based tests (200 samples each), all verifying round-trip fidelity: `Parse(text).ToString() == text`.

**FsCheck 3.x C# API lesson:** In FsCheck 3.x, the C#-friendly API lives in `FsCheck.Fluent` namespace. The `Gen` static class there provides LINQ-compatible extension methods (`Select`, `SelectMany`, `Where`, `ListOf`, `ArrayOf`, `OneOf`, `Elements`, `Constant`, `Choose`, `Sample`). The `[Property]` attribute from FsCheck.Xunit works for simple cases, but for custom generators from `Gen<T>`, the cleanest C# approach is `[Fact]` + `Gen.Sample(size, count)` to draw values and assert manually. There is no C#-friendly `ForAll` in FsCheck 3.x — `Prop.ForAll` is in the `FSharp` namespace and takes `FSharpFunc`.

**Dockerfile-level round-trip limitation discovered:** Instructions that use `ArgTokens` with `excludeTrailingWhitespace: true` (STOPSIGNAL, MAINTAINER, SHELL) do NOT consume the trailing `\n` during parsing. When these instructions appear as intermediate constructs in a multi-instruction Dockerfile, the `\n` between them and the next instruction is silently dropped. The Dockerfile body generator therefore excludes these three instruction types to maintain round-trip fidelity. Individual instruction round-trip tests work fine since they don't include trailing `\n`.

**Test count:** 599 -> 621 (22 new property tests). All pass. 0 build warnings.

**Key files created:**
- `src/Valleysoft.DockerfileModel.Tests/Generators/DockerfileArbitraries.cs` (new)
- `src/Valleysoft.DockerfileModel.Tests/PropertyTests.cs` (new)
- `src/Valleysoft.DockerfileModel.Tests/Valleysoft.DockerfileModel.Tests.csproj` (modified — FsCheck packages)

### 2026-03-05 — Fix trailing newline loss in STOPSIGNAL, MAINTAINER, SHELL (Issue #176)

**Root cause:** `StopSignalInstruction`, `MaintainerInstruction`, and `ShellInstruction` all passed `excludeTrailingWhitespace: true` to `ArgTokens()` in their `GetArgsParser` methods. This caused the trailing `\n` to be consumed and discarded during parsing. When these instructions appeared as intermediate lines in a multi-instruction Dockerfile, the newline between them and the next instruction was silently dropped, breaking round-trip fidelity.

**Fix:** Removed the `excludeTrailingWhitespace: true` parameter from all three files, so they now use the default `excludeTrailingWhitespace: false` — matching the behavior of every other instruction type. This was a one-line change per file.

**Key insight:** This bug was originally documented during P0-2 FsCheck work (see "Dockerfile-level round-trip limitation discovered" learning above). The Dockerfile body generator had to exclude these three instruction types as a workaround. With this fix, that workaround is no longer necessary.

**Files changed:**
- `src/Valleysoft.DockerfileModel/StopSignalInstruction.cs` (line 55)
- `src/Valleysoft.DockerfileModel/MaintainerInstruction.cs` (line 56)
- `src/Valleysoft.DockerfileModel/ShellInstruction.cs` (line 41)

**Test count:** 649 tests pass. 0 build warnings, 0 errors.
