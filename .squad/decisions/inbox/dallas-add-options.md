# Decision: ADD --checksum, --keep-git-dir, --link Implementation Approach
**Author:** Dallas (Core Dev)
**Date:** 2026-03-05

## Context

The Docker `ADD` instruction supports three new options not yet modeled: `--checksum=<hash>` (key-value), `--keep-git-dir` (boolean), and `--link` (boolean). The `--exclude` option was explicitly deferred as not yet stable syntax.

## Decisions

### Single constructor for AddInstruction

AddInstruction was refactored to a single public constructor with all optional parameters (`changeOwner`, `permissions`, `checksum`, `keepGitDir`, `link`, `escapeChar`) rather than providing a backward-compat positional overload alongside the new extended one. Two overloads with overlapping optional parameters produce C# overload-ambiguity errors when named arguments are used. The base-class `FileTransferInstructionTests` factory lambda was updated to use named args (`changeOwner:`, `permissions:`, `escapeChar:`), matching the pattern CopyInstruction already established.

### optionalFlagParser chain (not full Options() refactor)

ADD uses the existing `optionalFlagParser` mechanism in `FileTransferInstruction.GetInnerParser`, chaining `ChecksumFlag | KeepGitDirFlag | LinkFlag` with `.Or()`. This is simpler than the full Options() combinator pattern used by RunInstruction, because `FileTransferInstruction.FlagOption` already handles arbitrary-order flag parsing via `.Many()` + `.Optional()`. No changes to `FileTransferInstruction` were needed.

### Flag ordering in CreateInstructionString

`--checksum` is the leading `optionalFlag` (placed before `--chown`/`--chmod`). `--keep-git-dir` and `--link` are combined into the `trailingOptionalFlag` slot (placed after `--chmod`). This reuses the two-slot signature without requiring `FileTransferInstruction` changes.

### ChecksumFlag: KeyValueToken pattern

`ChecksumFlag` extends `KeyValueToken<KeywordToken, LiteralToken>` with `isFlag: true` and `canContainVariables: true`. Identical pattern to NetworkFlag/SecurityFlag/PlatformFlag.

### KeepGitDirFlag: AggregateToken pattern

`KeepGitDirFlag` extends `AggregateToken` containing `SymbolToken('-')`, `SymbolToken('-')`, `KeywordToken("keep-git-dir")`. Identical pattern to `LinkFlag`. `KeywordToken.GetParser` handles hyphens inside keyword names correctly via `Parse.IgnoreCase` on each character.

### LinkFlag reused

The existing `LinkFlag` class (created for COPY --link) is reused directly for ADD --link. No new class.

## Files Changed

- `src/Valleysoft.DockerfileModel/ChecksumFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/KeepGitDirFlag.cs` (new)
- `src/Valleysoft.DockerfileModel/AddInstruction.cs` (modified)
- `src/Valleysoft.DockerfileModel/DockerfileBuilder.cs` (modified)
