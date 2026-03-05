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
