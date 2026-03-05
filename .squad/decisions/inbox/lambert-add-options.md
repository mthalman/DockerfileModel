# Lambert Decision — ADD instruction options test patterns

**Date:** 2026-03-05
**Author:** Lambert (Tester)
**Branch:** squad/103-add-instruction-options

## Decision

### AddInstructionTests restructured to parallel CopyInstructionTests pattern

`AddInstructionTests` was a thin shell (just delegating to the base class). To add ADD-specific tests for `--checksum`, `--keep-git-dir`, and `--link`, I restructured it to match the `CopyInstructionTests` pattern exactly:

- Added `AddInstructionParseTestScenario` class (holds `EscapeChar`), with `[Theory] [MemberData(nameof(ParseTestInput))]` routing through `AddInstruction.Parse`.
- Added `AddInstructionCreateTestScenario` class (holds `Sources`, `Destination`, `ChangeOwner`, `Permissions`, `Checksum`, `KeepGitDir`, `Link`, `EscapeChar`) and a corresponding `[Theory]` that constructs via the new constructor parameters.
- Renamed original base-class delegation methods to `ParseBase` / `CreateBase` with `ParseTestInputBase` / `CreateTestInputBase` member data names — same pattern as `CopyInstructionTests`.

### Expected API surface for Dallas

The tests assume the following API (Dallas to implement):

**AddInstruction constructor:**
```csharp
public AddInstruction(IEnumerable<string> sources, string destination,
    UserAccount? changeOwner = null, string? permissions = null,
    char escapeChar = Dockerfile.DefaultEscapeChar,
    string? checksum = null, bool keepGitDir = false, bool link = false)
```

**AddInstruction properties:**
- `string? Checksum` — get/set, nullable string, null when absent
- `LiteralToken? ChecksumToken` — get/set, nullable token, null when absent
- `bool KeepGitDir` — get/set, false when absent, true when `--keep-git-dir` present
- `bool Link` — get/set, false when absent, true when `--link` present

**DockerfileBuilder.AddInstruction:**
```csharp
public DockerfileBuilder AddInstruction(IEnumerable<string> sources, string destination,
    UserAccount? changeOwnerFlag = null, string? permissions = null,
    string? checksum = null, bool keepGitDir = false, bool link = false)
```

### Flag construction order in generated output

Based on the `CopyInstruction` precedent (where `--link` is last among all flags), the expected construction order for ADD is:
1. `--checksum=...` (before the file-transfer shared flags)
2. `--chown=...` (shared)
3. `--chmod=...` (shared)
4. `--keep-git-dir`
5. `--link`

This is reflected in the create scenario tests. If Dallas uses a different order, the create scenario `TokenValidators` and `Validate` lambdas will need updating to match.

## Rationale

Keeping ADD tests in exact structural parity with COPY tests makes future maintenance straightforward. The three new flags follow the two established patterns (`KeyValueToken` for checksum, bare `AggregateToken` for boolean flags) already used in this codebase.
