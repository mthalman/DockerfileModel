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

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
