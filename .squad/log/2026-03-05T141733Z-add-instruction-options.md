# Session Log: ADD Instruction Options

**Date:** 2026-03-05T14:17:33Z
**Duration:** Full spawn cycle
**Agents:** Dallas (implementation), Lambert (test coverage)

## What Happened

Dallas implemented three new options for the ADD instruction (#103):
- `--checksum=<hash>` (KeyValueToken pattern, supports variables)
- `--keep-git-dir` (AggregateToken boolean flag pattern)
- `--link` (reused from COPY, no new implementation)

Lambert wrote 42 comprehensive tests covering all patterns and combinations.

## Decisions Made

1. Single constructor on AddInstruction (optional parameters only) — avoids overload ambiguity
2. Use existing optionalFlagParser chain in FileTransferInstruction (minimal change)
3. Flag order: --checksum (leading), then --keep-git-dir and --link (trailing after --chmod)
4. LinkFlag reused directly from COPY implementation

## Test Coverage

- ChecksumFlagTests: 7 tests
- KeepGitDirFlagTests: 2 tests
- AddInstructionTests: 27 new parse/create tests
- DockerfileBuilderTests: 5 new builder tests
- Total: 42 new tests, all pass

## Outcomes

- All 599 tests pass (557 pre-existing + 42 new)
- PR #174 created targeting dev branch
- Decisions merged to decisions.md
- Documentation complete
