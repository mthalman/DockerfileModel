# Contributing

Contributions are welcome. Submit a pull request or open an issue as necessary.

## Developer Prerequisites

Install the .NET SDK specified by [`global.json`](global.json). From the
repository root, run `dotnet --version` to confirm that SDK resolution succeeds.
Install the .NET 8 runtime to run the sample and differential-test projects;
the unit tests use .NET 10. Check installed runtimes with `dotnet --list-runtimes`.
Packaged-documentation verification also requires PowerShell 7.4 or newer.

## Build and test

From the repository root, build and test the solution, including the consumer
examples:

```powershell
dotnet build src\Valleysoft.DockerfileModel.sln -c Release
dotnet test src\Valleysoft.DockerfileModel.sln -c Release --no-build -v normal
```

For a focused unit-test run, select a test class or method:

```powershell
dotnet test src\Valleysoft.DockerfileModel.Tests -c Release --no-build --filter "FullyQualifiedName~FromInstructionTests.Parse"
```

Follow the [Lean setup, proofs, and differential testing guide](lean/README.md)
when changing parser behavior. For changes to the pinned upstream corpus, use
the [BuildKit importer procedure](tools/BuildKitCorpus/README.md).

## Verify documentation and examples

After the Release build above, run this command from the repository root in
PowerShell 7.4 or newer:

```powershell
.\tools\Documentation\Verify-Documentation.ps1
```

The script packs the existing Release build with `--no-build`, checks the
packaged README and XML API documentation, tests rejection of invalid package
documentation, and runs the consumer examples against the package on .NET 8
and .NET 10. It uses a temporary package feed and cache, can restore test
dependencies from NuGet.org, and removes its temporary files when it finishes.
A successful run ends with:

```text
Packaged README/XML and package-backed examples verified for both target scenarios.
```

Keep examples consistent with the maintained
[usage examples](src/Valleysoft.DockerfileModel.Samples/UsageExamples.cs) and
[advanced examples](src/Valleysoft.DockerfileModel.Samples/AdvancedExamples.cs).
These checks do not validate Markdown links or rendering; check changed
destinations and anchors separately.

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

[author-guide]: https://github.com/mthalman/release-automation/blob/90551757fe8b061d4dff1a4cab12f10e58f07201/docs/author-guide.md
[fragment-template]: https://github.com/mthalman/release-automation/blob/90551757fe8b061d4dff1a4cab12f10e58f07201/docs/fragment-template.md
