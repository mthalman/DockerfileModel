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
   builds, tests, and checks that the tag matches the package version before
   requesting approval, then
   publishes to NuGet, publishes the accumulated draft, and attaches the package.

For a prerelease, push a tag such as `v1.2.3-preview.1`. The workflow publishes
the accumulated draft as a prerelease and does not mark it Latest.
Publishing a prerelease starts a new draft range, so the later stable release
notes contain only changes made after that prerelease. The prerelease remains
the release-note record for the changes it introduced.

Published GitHub Releases are the release-note system of record; this repository
does not maintain a `CHANGELOG.md`.

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
