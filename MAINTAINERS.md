# Maintainer guide

## Label pull requests

Apply exactly one semantic-version label based on the highest-impact public
change:

- `semver:major` for breaking public API or behavior
- `semver:minor` for backward-compatible public functionality
- `semver:patch` for fixes, documentation, dependencies, tests, build changes,
  or maintenance

Apply at most one release-note category:

- `enhancement` for features
- `bug` for fixes
- `documentation` for documentation-only changes
- `dependencies` for dependency updates
- No category for maintenance, refactoring, tests, or infrastructure

The existing `type:feature`, `type:bug`, and `type:docs` labels are accepted as
aliases for `enhancement`, `bug`, and `documentation`. Apply only the canonical
label to new pull requests.

For mixed pull requests, classify by the highest-impact public change. For
example, a test-heavy pull request that fixes a product bug is a `bug`. A
dependency pull request that updates production and tooling dependencies remains
a `dependencies` change.

Apply `skip-changelog` to internal-only test dependency updates, CI action
updates, build or tooling changes, and repository administration that package
users do not need to know about. Do not apply `skip-changelog` to production
dependency updates, user-facing fixes, features, documentation, or significant
release behavior.

Renovate automatically applies `dependencies` and `semver:patch`, plus
`skip-changelog` for internal dependency and build-tool updates. Replace its
semantic-version label when an update has a higher public impact.

## Versioning and releases

Package and assembly versions are derived from Git tags by
[MinVer](https://github.com/adamralph/minver):

* Stable release: `v1.2.3`
* Prerelease: `v1.2.3-preview.1`

The `v` prefix is omitted from the resulting package version. For example,
`v1.2.3` produces `Valleysoft.DockerfileModel.1.2.3.nupkg`.

Untagged commits use MinVer's deterministic development version. After a stable
release, MinVer increments the patch version and adds an `alpha.0` prerelease
identifier and the Git commit height, so an untagged build cannot be mistaken
for a stable release.

## Manage release notes

[Release Drafter](https://github.com/release-drafter/release-drafter) collects
pull requests merged to `main` since the latest published release in an
unpublished GitHub Release and uses their labels to organize the release notes
and select the next version.

If no semantic-version label is present, Release Drafter proposes a patch
release. If more than one is present, the highest version change wins. Pull
requests without a category appear under Maintenance. Pull requests with
`skip-changelog` are excluded from both the release notes and version
resolution.

If labels are corrected after a pull request is merged, manually run the
Release Drafter workflow to update the draft immediately.

To publish a stable release:

1. Review the accumulated draft on the GitHub Releases page.
2. Confirm that its proposed version and release notes are correct.
3. Leave the draft unpublished and push the chosen `v*` tag at the reviewed
   commit on `main`.
4. Approve deployment to the protected `nuget.org` environment. The workflow
   builds, tests, and validates the package before requesting approval, then
   publishes to NuGet, publishes the accumulated draft, and attaches the packages.

For a prerelease, push a tag such as `v1.2.3-preview.1`. The workflow publishes
the accumulated draft as a prerelease and does not mark it Latest.
Publishing a prerelease starts a new draft range, so the later stable release
notes contain only changes made after that prerelease. The prerelease remains
the release-note record for the changes it introduced.

Published GitHub Releases are the release-note system of record; this repository
does not maintain a `CHANGELOG.md`.

### Release operating constraints and recovery

This workflow follows the two-job design in
[mthalman/DockerRegistryClient#154](https://github.com/mthalman/DockerRegistryClient/pull/154).
The validation job packs once, runs the package-contract gate, attests the
archives, and uploads them with one-day retention. The protected publish job
downloads those artifacts; it does not check out source or rebuild packages.

Release **one version at a time, in increasing version order**. Keep exactly
one accumulated draft, and pause merges to `main` and manual Release Drafter
runs from the tag push until publication completes. The workflow does not
freeze release notes: Release Drafter can otherwise change the draft while a
release awaits approval. Review the draft before approving publication.

For a partial publication failure, use **Re-run failed jobs** within the
one-day artifact retention window. Package and symbol pushes separately skip
already-published versions. An existing GitHub Release for the tag is reused;
asset uploads use `--clobber` so the original validated artifacts can replace
incomplete uploads. This assumes ordinary mutable GitHub Releases.

Do not use a full rebuild as an automatic recovery mechanism after any package
has been published. Archive bytes are not guaranteed to be reproducible, and
this workflow does not compare them with previously published bytes. If the
artifacts expire or the draft changes, stop and reconcile the release manually
using the original artifacts, or release corrected contents under a new version.
Do not push another release tag until the pending release is resolved.

Out-of-order recovery of an older draft requires manual handling: publishing a
stable draft marks it Latest. The workflow intentionally does not implement
version-aware Latest selection or automatic draft reconstruction.

## Configure trusted publishing

Complete this one-time setup before the first release:

1. Create a protected GitHub Actions environment named `nuget.org` and
   configure required reviewers or other deployment protection rules.
2. Add a Trusted Publishing policy to the NuGet.org account `thalman`:

   | Setting | Value |
   | --- | --- |
   | Repository owner | `mthalman` |
   | Repository | `DockerfileModel` |
   | Workflow file | `release.yml` |
   | Environment | `nuget.org` |

The environment name must match exactly. `NuGet/login` exchanges the job's OIDC
token for a short-lived API key. Remove the old `NUGET_ORG_API_KEY` secret after
trusted publishing is configured and a release succeeds.

## Package contract and API baseline

CI and releases use the reusable prerequisite gate
`.github/scripts/Validate-Package.ps1`. It validates both archives' IDs, versions,
file allowlists, target assemblies, production dependency groups, README, XML
documentation, portable PDB identities and checksums, and SourceLink repository
and commit mappings. A temporary consumer compiles for `netstandard2.0` and
`net10.0` using a `PackageReference`, an empty package cache, and source mapping
that restricts this package to the local publication feed. Its restored archive
must have the same digest as the publication package.

To run the gate locally from the repository root with PowerShell 7:

```powershell
dotnet build src -c Release -p:ContinuousIntegrationBuild=true
dotnet pack src\Valleysoft.DockerfileModel -c Release --no-build -p:ContinuousIntegrationBuild=true -o src\artifacts
.\.github\scripts\Validate-Package.ps1 -PackageDirectory src\artifacts -ExpectedCommit (git rev-parse HEAD)
```

Pass `-ExpectedVersion 1.2.3` to require that exact version. Without it, CI
validates the Git-derived development version.
The artifact-dependent xUnit test is deliberately skipped during ordinary
pre-pack tests; the validation script enables it and propagates any failure.

SDK package validation runs during `dotnet pack` and compares the package's
public API with the pinned NuGet baseline `2.0.0`, including parameter names.
It also checks compatibility between target frameworks. Do not advance
`PackageValidationBaselineVersion` simply to silence a failure. Intentional
breaks require a reviewed major-version decision and documented, narrow
suppressions, or an explicit baseline update after the corresponding release
exists. Review baseline updates together with changes to the package contract
and consumer fixture. Missing XML comments on existing APIs are tolerated
(`CS1591`); malformed comments and other build warnings still fail the build.
