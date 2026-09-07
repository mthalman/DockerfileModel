# Contributing

Contributions are welcome. Submit a pull request or open an issue as necessary.

## Developer Prerequisites

* [.NET 5.0 SDK](https://docs.microsoft.com/dotnet/core/install/)

## Versioning and releases

Package and assembly versions are derived from Git tags by
[MinVer](https://github.com/adamralph/minver). Create a tag on the commit to
release and push it to start the release workflow:

* Stable release: `v1.2.3`
* Prerelease: `v1.2.3-preview.1`

The `v` prefix is omitted from the resulting package version. For example,
`v1.2.3` produces `Valleysoft.DockerfileModel.1.2.3.nupkg`.

Untagged commits use MinVer's deterministic development version. After a stable
release, MinVer increments the patch version and adds an `alpha.0` prerelease
identifier and the Git commit height, so an untagged build cannot be mistaken
for a stable release.
