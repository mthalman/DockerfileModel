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

The legacy `type:feature`, `type:bug`, and `type:docs` labels are not category
aliases in the shared infrastructure. Use the canonical labels above.

Require a new migration fragment for each breaking change, following the
[contributor guidance](CONTRIBUTING.md#label-pull-requests-and-document-breaking-changes).
Never combine `semver:major` with `skip-changelog`. Retain fragments after
publication; do not delete or rename them.

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
Dependabot manages only release-automation updates and applies `dependencies`,
`semver:patch`, and `skip-changelog`. Adjust the version label and remove
`skip-changelog` when an update changes significant release behavior.

## Versioning

### Upgrade Dockerfile compatibility

[`upstream-compatibility.json`](upstream-compatibility.json) records the stable
Dockerfile frontend target and its exact BuildKit source commit. Renovate opens
version-update PRs after the configured release age; it does not detect language
changes or certify compatibility. These PRs must not automerge.

Before accepting an update:

1. Review all intervening `dockerfile/X.Y.Z` releases in `moby/buildkit`.
   Inspect changes to command/parser code and tests, typed instruction flags
   and nested option handling, shell expansion, Dockerfile-to-LLB conversion,
   inline integration tests, directives, and stable/labs build gates. Follow
   relevant changes into dependencies or backend capabilities. Do not restrict
   review to `parser/testfiles` or assume release notes are exhaustive.
2. Resolve the frontend release tag to its commit, dereferencing annotated tags,
   and update `sourceCommit`. Never substitute the latest backend release or
   silently follow a moved tag. Use the
   [importer instructions](tools/BuildKitCorpus/README.md) to align the Go module
   dependency with that commit and regenerate the corpus.
3. Review fixture, coverage, license, and attribution changes. Implement required
   parser/model changes and add regression tests for relevant behavior absent
   from upstream file fixtures. Keep execution semantics separate from parsing
   and typed API support.
4. Run importer tests and check mode, .NET tests, and the C#/Lean corpus and
   differential comparisons. Review every new difference and stale expected
   failure. Update the [compatibility limitations](docs/dockerfile-compatibility.md);
   do not turn a failing supported behavior into an unconditional guarantee by
   adding an expected-failure entry.
5. Record the reviewed changes, tests, and remaining limitations in the PR.
   Confirm the proposed frontend version, source commit, importer dependency,
   and corpus identity agree before human approval and merge.

A version-only Renovate edit intentionally fails the metadata consistency check.
Corpus execution is offline; importing and regeneration checks can use network
access to acquire the pinned source and dependencies.

The bot's initial dependency/patch labels are not the final release decision.
Apply the highest-impact semantic-version label and appropriate category for the
actual changes. Do not use `skip-changelog` for user-facing compatibility
improvements, parser fixes, or features.

### Package versions

Package and assembly versions are derived from Git tags by
[MinVer](https://github.com/adamralph/minver). Release tags use the stable format
`vMAJOR.MINOR.PATCH`. The `v` prefix is omitted from the resulting package version.
For example, `v1.2.3` produces `Valleysoft.DockerfileModel.1.2.3.nupkg`.

Untagged commits use MinVer's deterministic development version. After a stable
release, MinVer increments the patch version and adds an `alpha.0` prerelease
identifier and the Git commit height, so an untagged build cannot be mistaken
for a stable release.

## Releasing

The repository consumes [release-automation][installation] with its default
paths and labels: fragments in `.changes`, generated guides in `docs/migrations`,
state in `.github/migration-guides.json`, and the generated PR branch
`automation/migration-guides`. Let automation create guides and state; do not
seed them manually.

Releases use one stable stream from `main`, tags exactly matching
`vMAJOR.MINOR.PATCH`, and version-only titles such as `2.1.0`. Prerelease
publication is not supported. The shared workflows do not merge PRs or create
tags.

### Manage release notes

Release Drafter collects pull requests merged to `main` since the latest
published release and uses their labels to organize release notes and select
the next version.

If no semantic-version label is present, Release Drafter proposes a patch
release. If more than one is present, the highest version change wins. Pull
requests labeled `semver:major` appear under Breaking Changes; other pull
requests without a category appear under Maintenance. Pull requests with
`skip-changelog` are excluded from both the release notes and version
resolution.

If you correct labels after a pull request merges, manually run **Release
Drafter**. Follow the documentation PR process below if the run requires updated
guides, and confirm that drafting succeeds before using the refreshed draft.

Published GitHub Releases are the release-note system of record; this repository
does not maintain a `CHANGELOG.md`.

### Activate the workflows

Before the first release, allow the pinned reusable workflows and their Actions
in repository settings, enable **Allow GitHub Actions to create and approve pull
requests**, and ensure the eight labels listed above exist.
[Configure trusted publishing](#configure-trusted-publishing) and preserve the
`nuget.org` environment's approval controls.

The **Migration policy** workflow reads proposed files as data through
`pull_request_target`; do not add PR code execution to that workflow.
After merging the workflows, exercise policy with a test PR. Confirm that a
major change without a fragment and a major change labeled `skip-changelog`
both fail. Discover the deployed policy check's actual nested name before
making it required in a ruleset.

Run **Release Drafter** and follow the generated documentation PR path below.
Existing drafts must be refreshed by a successful shared drafting run before
tagging; older drafts lack the preparation metadata required by publication.
Verify the [tag-publishing lifecycle][tag-publishing] in a test repository,
including approvals, draft visibility, rejection cases, and reruns. Committed
workflow files alone do not verify live publication.

### Prepare and publish a release

1. Run **Release Drafter**, or let a push to `main` trigger it. It selects the
   latest default-branch commit and resolves the version from PR labels.
2. If generated guides or state need changes, inspect the draft documentation
   PR. A human must mark it ready for review to trigger CI; automated updates
   return it to draft. Review and merge it. The drafting run intentionally
   fails while these exact files are unmerged and leaves the old draft
   unchanged. Rerun drafting after merge if it does not run automatically.
3. After drafting succeeds, inspect the single draft's `tag_name`,
   `target_commitish`, notes, and migration links:

   ```powershell
   gh api repos/mthalman/DockerfileModel/releases --paginate --jq '.[] | select(.draft) | {tag_name, target_commitish, html_url}'
   ```

4. After reviewing CI and release content, have a human create and push the
   draft's exact new tag at its exact `target_commitish` SHA, not an assumed
   current branch tip. Do not move, delete, or recreate a tag to bypass a gate.
5. Open the **Release** workflow run. Preparation validates the tag and draft;
   validation builds, tests, and packs the prepared commit, checking the MinVer
   package version.
6. After validation succeeds, approve the `nuget.org` deployment if approval is
   required. Publishing uses NuGet OIDC, uploads the package to the draft, and
   finalization rechecks and publishes that existing GitHub Release without
   rewriting its notes.
7. Wait for the publishing job to succeed, then confirm that the expected package
   version is available on NuGet.org and the GitHub Release is published with
   its package asset.

The entire tag workflow shares the `release-drafter` concurrency group with
the reusable drafting pipeline. Both use `queue: max` with cancellation disabled
so new drafting runs do not replace queued publication runs. GitHub permits up
to 100 pending runs in this mode; additional runs are canceled when the queue is
full. Do not add that group to the drafting caller; the reusable workflow
already owns it.

### Recover a failed release

Rerun the original valid tag-creation workflow rather than pushing a moved tag.
An already-published prepared release skips the build and publishing jobs.
For a partial failure, inspect NuGet and the GitHub Release before retrying:
`--skip-duplicate` tolerates an existing package and asset upload uses
`--clobber`, but finalization cannot roll back either side effect. Confirm that
any existing package is the intended artifact.

GitHub API checks and publication are not atomic. Keep approvals and repository
write access restricted. If the default branch advances with workflow changes,
publishing an older prepared commit may require workflow-modification
authorization that `GITHUB_TOKEN` cannot receive. Follow the pinned
[credential requirements][publication-credentials] rather than bypassing the
checks or retagging.

### Upgrade release automation

Keep both reusable workflow refs, both publication Action refs, and the shared
documentation links in this file, `CONTRIBUTING.md`, and `AGENTS.md` pinned to
the same reviewed commit when upgrading. Verify that the selected published
stable tag resolves to that commit, and pair each workflow and Action SHA with
its matching version comment, such as `# v1.0.1`.

Dependabot checks for release-automation updates weekly and groups the four
refs into one upgrade PR. Renovate excludes this dependency and continues to
manage other dependencies. Grouping does not enforce four-way equality; verify
that every ref uses the same SHA and matching tag comment before merging.

Dependabot's GitHub Actions ecosystem does not update Markdown links; update
those links manually in the same PR. Review the
[upstream upgrade guidance][upgrading] for release verification and compatibility
checks. No custom Release Drafter configuration is loaded.

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

[installation]: https://github.com/mthalman/release-automation/blob/90551757fe8b061d4dff1a4cab12f10e58f07201/docs/installation.md
[tag-publishing]: https://github.com/mthalman/release-automation/blob/90551757fe8b061d4dff1a4cab12f10e58f07201/docs/tag-publishing.md
[publication-credentials]: https://github.com/mthalman/release-automation/blob/90551757fe8b061d4dff1a4cab12f10e58f07201/docs/tag-publishing.md#choose-credentials-and-verify-draft-visibility
[upgrading]: https://github.com/mthalman/release-automation/blob/90551757fe8b061d4dff1a4cab12f10e58f07201/docs/upgrading.md
