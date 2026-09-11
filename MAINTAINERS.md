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
3. Publish the draft. GitHub creates its proposed `v*` tag on `main`, which
   starts the release workflow and publishes the matching NuGet package.

For a prerelease, create a GitHub prerelease with a tag such as
`v1.2.3-preview.1`. Creating the tag starts the same package release workflow.
Publishing a prerelease starts a new draft range, so the later stable release
notes contain only changes made after that prerelease. The prerelease remains
the release-note record for the changes it introduced.

Published GitHub Releases are the release-note system of record; this repository
does not maintain a `CHANGELOG.md`.

## Package contract and API baseline

CI uses the reusable post-pack gate `.github/scripts/Validate-Package.ps1`.
It validates both archives' IDs, versions,
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
