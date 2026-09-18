# Contributing

Contributions are welcome. Submit a pull request or open an issue as necessary.

## Developer Prerequisites

Install the .NET SDK specified by [`global.json`](global.json). From the
repository root, run `dotnet --version` to confirm that SDK resolution succeeds.

## Label pull requests and document breaking changes

Follow the [pull request labeling rules](AGENTS.md#pull-request-labels) and the
pinned [release-automation author guide][author-guide]. Use exactly one
`semver:major`, `semver:minor`, or `semver:patch` label and at most one category:
`enhancement`, `bug`, `documentation`, or `dependencies`. Legacy `type:*` labels
do not select categories. Use `skip-changelog` only for non-breaking changes
that package users do not need in release notes.

For each breaking change, add a new file named
`.changes/+short-kebab-slug.breaking.md` using the
[shared fragment template][fragment-template]. Include one H3 title and these
meaningful H4 sections, in order: Previous behavior, New behavior, Type of
breaking change, Reason for change, Recommended action, and Affected APIs.
Describe the actual change and migration steps; placeholders do not pass policy.
Apply `semver:major`, never `skip-changelog`. Keep fragments after release;
do not delete or rename them. Non-breaking changes need no fragment.

The **Migration policy** workflow requires a new valid fragment for major
changes and rejects a major change excluded from release notes. General
version/category label counts remain a contributor and reviewer responsibility.

For release setup, publishing, and recovery, see the
[maintainer guide](MAINTAINERS.md#releasing).

[author-guide]: https://github.com/mthalman/release-automation/blob/89e88a5cf239f9d08369a43e9f0e0d603b48cb4a/docs/author-guide.md
[fragment-template]: https://github.com/mthalman/release-automation/blob/89e88a5cf239f9d08369a43e9f0e0d603b48cb4a/docs/fragment-template.md
