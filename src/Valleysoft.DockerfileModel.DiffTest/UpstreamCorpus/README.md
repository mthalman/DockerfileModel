# Pinned upstream corpus

This directory is separate from `RegressionCorpus` and generated FsCheck inputs.
See [Dockerfile compatibility](../../../docs/dockerfile-compatibility.md) for the
version policy, coverage boundaries, and maintainer responsibility.

## Files

- `generated/manifest.json`: source identity and hashes of generated files.
- `generated/cases/`: neutral instruction fixtures.
- `generated/sources/`: original upstream source bytes, including fixture goldens.
- `generated/coverage.json`: inclusion and exclusion decisions.
- `generated/`: applicable upstream license and attribution files.
- `known-deviations.json`: reviewed failures; the importer never writes this file.

The importer and its reproduction commands are documented in
[`tools/BuildKitCorpus`](../../../tools/BuildKitCorpus).

## Run the corpus

Build the .NET harness and Lean CLI first. From the repository root on Windows:

```powershell
dotnet build src\Valleysoft.DockerfileModel.DiffTest -c Release
Push-Location lean
lake build DockerfileModelDiffTest
Pop-Location

dotnet src\Valleysoft.DockerfileModel.DiffTest\bin\Release\net8.0\Valleysoft.DockerfileModel.DiffTest.dll `
  --upstream-only --lean-cli lean\.lake\build\bin\DockerfileModelDiffTest.exe
```

On Linux, use the executable without the `.exe` suffix. CI executes this command
inside a network namespace with no network access after building both binaries.

The summary reports separate counts for passes, expected failures, and
unexpected failures. The run succeeds with exit code 0 when every case either
passes or matches a reviewed entry in `known-deviations.json`. Expected failures
are recorded differences, not conformance passes. New differences, changed known outcomes,
and infrastructure errors make the command fail with a nonzero exit code.
An unexpected pass also fails the run: when a known difference disappears,
review and remove its stale entry.

`--compare` runs local regressions, this upstream corpus, then generated cases.
`--verify-upstream` validates metadata, hashes, source spans, and known-deviation
entries without starting Lean. Override locations with `--upstream-corpus` and
`--compatibility`.

Replay an exact case with `--replay-upstream <id> --lean-cli <path>`. Failure
output includes the complete command, including custom corpus and compatibility
paths. Replay retains the upstream expectation and reports expected failures.

Add `--upstream-report <path>` to save the observed outcomes and signatures as
JSON for investigation. This report does not modify or approve known deviations.

## Review a known deviation

Each entry has an exact fixture ID, input SHA-256, outcome signature, tracking
issue URL, and reason. The signature includes parser dispositions, canonical
JSON output, and crash type, but excludes platform-dependent error wording.

Investigate a new failure before adding an entry. Link a specific issue and
explain the discrepancy; do not baseline infrastructure errors. If behavior
changes or a case starts passing, remove or review the entry. A changed input
requires another review even when it has the same fixture ID.

Changes to `known-deviations.json` do not certify compatibility. Keep
case-specific details in the deviation entries and their linked issues.
