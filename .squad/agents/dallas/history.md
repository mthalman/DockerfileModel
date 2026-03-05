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
