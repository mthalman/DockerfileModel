# Phase 0 Review Gate — P0-8 Verdict

**Author:** Ripley (Lead)
**Date:** 2026-03-05
**Branch:** formal-verification

## Verdict: APPROVE WITH NOTES

Phase 0 property-based testing with FsCheck is ready to ship. All 649 tests pass (599 existing + 50 new). 0 build warnings, 0 errors.

## What Was Reviewed

### 1. Test Project Configuration (csproj)
- FsCheck 3.1.0 and FsCheck.Xunit 3.1.0 correctly added
- No version conflicts with existing dependencies
- Package versions are current and compatible

### 2. Generators (DockerfileArbitraries.cs — 645 lines)
- 18 instruction generators covering all Dockerfile instruction types
- Variable reference generator with all 6 modifier forms
- Dockerfile-level composition generator with correct preamble/body structure
- Line continuation generators for both backslash and backtick escape chars
- Primitive generators (Identifier, SimpleAlphaNum, PathSegment, ImageName, etc.)
- STOPSIGNAL/MAINTAINER/SHELL correctly excluded from Dockerfile-level body composition

### 3. Property Tests (PropertyTests.cs — 742 lines)
- **P0-3:** 22 round-trip tests verifying Parse(text).ToString() == text
- **P0-4:** 7 token tree consistency tests verifying aggregate toString == concat(children.toString) with VariableRefToken $ prefix and IQuotableToken wrapping
- **P0-5:** 3 variable resolution non-mutation tests
- **P0-6:** 16 modifier semantics tests covering all 6 modifiers across set/unset/empty states
- **P0-7:** 2 parse isolation tests verifying parsing context does not leak between instructions

## Correctness Assessment

All properties correctly verify their stated invariants:

- **Round-trip:** Tests the exact property claimed. Each instruction type gets its own test method.
- **Token tree consistency:** The assertion logic correctly accounts for both decorating overrides (`VariableRefToken` adds `$` prefix, `IQuotableToken` adds quote wrapping). Verified these types are mutually exclusive in the hierarchy.
- **Non-mutation:** Tests three paths: default options, with overrides, and explicit `UpdateInline = false`.
- **Modifier semantics:** All 6 modifiers tested with the correct expected behavior for each state (set/unset/empty). The `declareArg` parameter correctly distinguishes "unset" from "set with null value" for non-colon modifiers.
- **Parse isolation:** Uses `Instruction.CreateInstruction` (internal, correctly exposed via InternalsVisibleTo) for standalone parsing. Correctly handles trailing `\n` difference between standalone and Dockerfile-level parsing.

## Notes for Future Improvement

These are not blockers. Ship now, address opportunistically:

1. **Escape char coverage in generators (Dallas):** Only line continuation generators test backtick escape. All 18 instruction generators use the default backslash. Adding `escapeChar` as a generator parameter would significantly expand coverage.

2. **Token tree consistency coverage (Lambert):** Currently covers 5 of 18 instruction types plus Dockerfile and VariableRef. Expanding to all 18 instruction types would catch more structural invariant violations.

3. **HEALTHCHECK generator gap (Dallas):** Does not exercise `--start-period` option. Low risk — same pattern as the 3 options already tested.

4. **Variable reference diversity in P0-5 (Lambert):** `DockerfileWithVariables` only generates `$VAR` (simple) references. Adding `${VAR}` and `${VAR:-default}` forms would strengthen the non-mutation property.

5. **Shrinking tradeoff documented:** The `[Fact]` + `Gen.Sample()` pattern does not benefit from FsCheck's automatic shrinking. If property test failures become hard to debug, consider switching to `Prop.ForAll` with an `Arbitrary<T>` registration for the affected tests.

## Integration Assessment

- New tests are completely additive — no existing test code was modified
- `PropertyTests` class is cleanly separated from the existing per-instruction test files
- `Generators/` subdirectory is a clean organizational choice
- Build time impact: ~9.7 seconds total (previously ~570ms for 599 tests). The 50 property tests add ~9 seconds due to 200 samples each. Acceptable for CI.
