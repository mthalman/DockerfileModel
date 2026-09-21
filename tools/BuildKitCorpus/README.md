# Import the pinned BuildKit parser corpus

This maintainer tool imports instruction cases from the BuildKit source commit
in the root `upstream-compatibility.json`. It does not execute Dockerfiles,
build images, run either downstream parser, or change the compatibility pin.
Normal .NET corpus execution uses the checked-in output and needs neither Go
nor network access.

## Regenerate and check

Install Git and Go 1.26.3 or newer. You do not need an existing BuildKit
checkout: without `--source`, the importer clones the pinned frontend release
into a temporary directory, verifies its source commit, and removes the
checkout when it finishes. A moved release tag fails verification rather than
advancing the pin.

Importing can access the network for Go dependencies and source acquisition.
`--check` regenerates in memory and compares every output byte and filename,
reporting missing, changed, or extra files without modifying the corpus.
Regular import replaces generated artifacts, including deleting stale files
within `generated` only. Neither mode edits maintained known deviations.

Run these PowerShell commands from the repository root:

```powershell
Push-Location tools\BuildKitCorpus
go test ./...
go run . --repo-root ..\..
go run . --repo-root ..\.. --check
Pop-Location
```

A successful `--check` prints the source identity and artifact counts followed
by `check=true`.

### Use an existing checkout

To avoid cloning BuildKit, pass `--source`. The checkout must have a clean Git
status, the pinned commit checked out, and the matching
`dockerfile/<frontendVersion>` tag. The importer dereferences annotated tags
and requires the exact pinned commit.

From the repository root, run the following commands, replacing
`C:\src\buildkit` with your checkout path:

```powershell
Push-Location tools\BuildKitCorpus
go run . --repo-root ..\.. --source C:\src\buildkit
go run . --repo-root ..\.. --source C:\src\buildkit --check
Pop-Location
```

### Run full-source integration tests

Use the same verified checkout to exercise full-source integration in addition
to the self-contained tests. From the repository root:

```powershell
Push-Location tools\BuildKitCorpus
$env:BUILDKIT_CORPUS_SOURCE = 'C:\src\buildkit'
go test -v ./...
Pop-Location
```

The integration test generates twice, checks byte identity, verifies every
manifest hash, and verifies every case against its source line range.

## Review a frontend upgrade

The primary version stream is stable `dockerfile/X.Y.Z` frontend releases, not
BuildKit backend releases, labs, or moving tags. The initial frontend 1.27.0
resolves to `dddd5621af04ea57823085c93a063383f71d3173`; the Go module's v0.33.0
is another tag at that same commit.

After reviewing the selected release, resolve its tag to the exact commit and
edit the root compatibility metadata. In this directory, run
`go get github.com/moby/buildkit@<full-source-commit>` and `go mod tidy`, then
regenerate, check, and review the entire diff. Commit the metadata, Go module
files, and corpus together. This tool never performs those pin edits itself.
It verifies the running binary's dependency against the effective Go module,
rejects replacements, checks the module origin's exact Git SHA, and runs
`go mod verify`. A version-only metadata update fails until the source tag,
source commit, dependency, and generated corpus agree.

A new negative fixture, inline test function, stale selector, orphan/missing
golden, unknown fixture file, or applicable license/notice change requires an
explicit importer review. New positive Dockerfiles with goldens are imported
automatically. No case is excluded because C# or Lean currently rejects it.

## Extraction and coverage boundaries

The initial pin contains **33 positive fixture directories**, four negative
fixture directories, one line-metadata fixture, and 19 Go test functions in five
test files. The importer produces **290 instruction cases: 288 accept and two
reject**. These counts describe this pin, not permanent selection limits.

Positive files must parse with BuildKit and match their checked-in
`AST.Dump() + "\n"` goldens byte for byte. Instruction inputs use inclusive
`StartLine`/`EndLine` spans, retaining their actual bytes and final line
terminators, not normalized `Node.Original` strings. Each slice is reparsed
with its effective escape directive. Its complete relevant AST must match,
including flags, attributes, original instruction text, and heredoc attachments;
document line coordinates and preceding comments are intentionally ignored.
UTF-8 must be valid. BOMs, CRLF, continuations, and missing final newlines are
preserved. Synthetic tests cover heredoc extraction even though the initial
file fixtures do not cover the upstream inline heredoc tables.

Negative files must fail upstream parsing. Two explicitly reviewed ranges are
also required to fail independently: `env_no_value/Dockerfile` line 3 and
`shykes-nested-json/Dockerfile` line 1. Empty and comment-only negative documents
are excluded because their failure is document-level. Other lines in negative
documents are not claimed as positive instruction coverage.

`coverage.json` inventories every fixture, golden, source test file, imported
case, excluded source range, and Go test function. `go/parser` discovers test
functions; an explicit classification is required for each. Inline tables,
helper APIs, document metadata, warnings, and scanner failures are not claimed
as instruction coverage. Unsupported instruction names are explicitly excluded
with a reason; none occur in the initial pin. Only the harness's 18 instruction
types are executable. Low-level parser acceptance does not imply downstream
instruction validation or successful Docker builds.

## Generated artifact contract

All output resides in
`src\Valleysoft.DockerfileModel.DiffTest\UpstreamCorpus\generated`.
Paths inside JSON use `/` on all platforms.
Source paths are enumerated with NUL-delimited Git output, preserving Unicode
and whitespace without depending on the `core.quotePath` setting.

| Artifact | Contract |
| --- | --- |
| `manifest.json` | Schema 1, repository, frontend version, source commit, required `importerGoModSha256` and `importerGoSumSha256` hashes, sorted exhaustive `{path, sha256}` entries excluding itself, and importer module identity. |
| `cases/<idHash>.json` | `id`, uppercase `instructionType`, `inputBase64`, `escapeCharacter`, `expectedParse` (`accept` or `reject`), `sourcePath`, inclusive 1-based `startLine`/`endLine`, and lowercase `inputSha256`. IDs are `<sourcePath>#L<start>-L<end>`. |
| `sources/<pathHash>.source` | Exact original Git blob bytes, read with `git show` to avoid working-tree newline conversion. Includes fixture Dockerfiles, AST goldens, test source files, and the upstream license. |
| `coverage.json` | Human-readable file/range and inline-function coverage and exclusion inventory. |
| `LICENSE`, `ATTRIBUTION.md` | Original Apache-2.0 license, source attribution, and notice that JSON cases are extracted representations. |
| `.gitattributes` | Disables text conversion for generated artifacts so Git preserves source bytes and integrity hashes on Windows. |

`idHash` and `pathHash` are the first 16 lowercase hexadecimal characters of
SHA256 over the UTF-8 fixture ID and original upstream path, respectively.
Short filenames avoid Windows path-length limits without shortening provenance.
Hash-name collisions fail importing, including collisions between identical
source bytes from different paths. Full paths and IDs remain in the JSON and
coverage inventory.

Generated JSON and attribution use LF, contain no timestamps or absolute
machine paths, and have deterministic ordering. The initial source audit found
no applicable NOTICE, parser-subtree license override, or embedded fixture
copyright/license/notice markers. Future matches fail for maintainer review;
original comments are preserved in the source snapshots.

The two dependency hashes cover the exact bytes of
`tools\BuildKitCorpus\go.mod` and `tools\BuildKitCorpus\go.sum`. These external
files are not members of the generated `files` inventory. Their hashes let the
offline runner verify root or packaged dependency files without invoking Go.
This directory's `.gitattributes` keeps both files LF across Git checkouts.
