# Dockerfile compatibility

The upstream corpus is a checked-in collection of Dockerfile instruction
examples from BuildKit, Docker's build engine. Tests run each example through
this library's C# parser and a separate Lean reference parser. They check
whether each parser accepts or rejects the input as BuildKit did when the
example was imported. When both accept, the tests also compare their token
representations, including text and whitespace.

These upstream examples complement the repository's random-input tests.
Neither local parser is assumed correct when they disagree.

## The compatibility target

[`upstream-compatibility.json`](../upstream-compatibility.json) records the
official **stable Dockerfile frontend** version targeted by this repository and
its exact source commit in `moby/buildkit`. It does not track the BuildKit
backend release, a moving image tag such as `docker/dockerfile:1`, or the labs
channel.

Maintainers own the compatibility commitment. Updating this file proposes a
new target; passing tests alone does not establish compatibility. The maintainer
reviews upstream changes, updates the parser and tests where necessary, and
approves the new target before merging. See the
[upgrade procedure](../MAINTAINERS.md#upgrade-dockerfile-compatibility).

The current corpus is a conformance baseline, not a claim of complete support
for every feature of the named frontend. Its
[known deviations](../src/Valleysoft.DockerfileModel.DiffTest/UpstreamCorpus/known-deviations.json)
are executable, issue-linked differences. An unconditional compatibility claim
requires resolving violations within the promised scope; recording an expected
failure does not make the behavior compatible.

## Scope of the commitment

The library parses, represents, edits, and round-trips Dockerfile text. It does
not execute Docker builds or promise all of BuildKit's platform capabilities,
entitlements, runtime validation, or build semantics. Stable frontend syntax,
typed model support, variable resolution, and preservation of unknown text are
different capabilities: preserving an option does not imply a typed API for it.

Labs features and arbitrary custom frontends are outside the stable-version
commitment. A newer target does not certify every historical frontend version.
Reading `#syntax` metadata does not infer compatibility or select a different
parser at runtime.

## What the upstream corpus checks

The Go importer uses the pinned upstream parser to validate its file-based
fixtures and extract original instruction spans. The resulting neutral cases
record source paths, line ranges, input hashes, escape characters, and upstream
accept/reject expectations. Original source bytes, upstream goldens, coverage
decisions, attribution, and applicable license notices remain in the
[generated corpus](../src/Valleysoft.DockerfileModel.DiffTest/UpstreamCorpus/generated).

Both C# and Lean execute the same instruction bytes. Each must agree with
BuildKit's expected acceptance. When both accept, their existing canonical token
JSON must match. Mutual rejection is **not** a pass when BuildKit accepts.
The comparison does not equate either token model with BuildKit's AST.
Token-shape disagreements do not by themselves identify a production parser bug.

The instruction-level adapter does not test whole-file directive placement,
document-wide state, or every inline/generated upstream test. The generated
coverage inventory identifies exclusions rather than counting them as passes.
Low-level BuildKit parsing also accepts some constructs that its typed
instruction or conversion layer rejects; those layers are not interchangeable.

Known deviations match one fixture ID, input hash, and observed outcome
signature. New differences, changed signatures, unexpected passes, stale entries,
and infrastructure errors fail the run. Expected failures are reported separately
from passes. Imported inputs cannot be minimized or promoted as local regressions.

## How updates are discovered

Renovate watches published `dockerfile/X.Y.Z` releases in `moby/buildkit` and
opens non-automerge PRs. Explicit filtering excludes backend releases, labs,
and release candidates. A version-only PR intentionally fails corpus consistency
checks until its source pin and generated metadata are refreshed.

There is **no automated detection of syntax changes**. Upstream features can
appear in instruction parsing, expansion, conversion, inline tests, or channel
gates without changing the imported fixtures. Maintainers review those sources
and add focused tests for changes absent from the corpus.

Normal corpus execution uses checked-in data and built C#/Lean binaries.
It requires neither Go, an upstream checkout, nor network access. Importing and
verifying regeneration are separate maintainer/build tasks that may fetch the
pinned source and restore Go dependencies.
