# Dockerfile Model Library

This .NET library provides a structured model of the Dockerfile syntax for the purposes of parsing and generating Dockerfiles. It provides full fidelity of the file contents meaning that the parsed content can be output back to a string and produce the same content character-for-character, including whitespace. This makes it ideal for parsing as well as workflows that require programmatically modifying existing Dockerfiles.

## Key Features

* A high-level model for interacting with Dockerfile constructs.
* Access to the underlying tokens that make up the higher-level model.
* Full fidelity for input and output: the model's output is identical to the Dockerfile input.
* Resolve ARG values that are referenced throughout a Dockerfile.
* Ability to further organize a Dockerfile model in terms of its stages.
* Static stage dependency analysis and an inventory of external image references.
* Opt-in recovery with structured diagnostics, opaque unknown instructions, and original-source spans.
* Typed parser directives and frontend image/version metadata, without inferring feature compatibility.
* Trivia-preserving structural edits through live editable collections.

## Usage

The library is available as a NuGet package: [Valleysoft.DockerfileModel](https://www.nuget.org/packages/Valleysoft.DockerfileModel/).

For code examples, check out the [scenario tests](https://github.com/mthalman/DockerfileModel/blob/main/src/Valleysoft.DockerfileModel.Tests/ScenarioTests.cs) which demonstrate how the API can be used for various scenarios.

Edit document and instruction collections directly:

```csharp
Dockerfile dockerfile = Dockerfile.Parse(
    "FROM alpine AS build\nCOPY src/ /app/\n");
CopyInstruction copy = dockerfile.Items.OfType<CopyInstruction>().Single();
copy.Sources.Add("generated/");
dockerfile.Items.Insert(dockerfile.Items.IndexOf(copy) + 1, new RunInstruction("echo ready"));
```

See [Collection editing](docs/editing.md) for document items, value/token lists,
comments, mount entries, paired heredocs, and trivia policies. Existing scalar
property setters are unchanged. Upgrading consumers should read the
[collection editing migration](docs/structural-editing-migration.md).

Use `Dockerfile.Analyze()` to inspect stage dependencies and external image references
without modifying the model. See [Stage dependencies and image inventory](docs/stage-analysis.md)
for examples, API contracts, resolution rules, and limitations.

See [Parsing, recovery, and diagnostics](docs/parsing.md) for parse options, recovery behavior, diagnostic codes, and source locations.

See [Parser directives and frontend metadata](docs/parser-directives.md) for typed
syntax, escape, and check directives, header placement rules, and lossless editing.

See [Dockerfile compatibility](docs/dockerfile-compatibility.md) for the pinned
stable frontend target, upstream conformance corpus, known limitations, and
maintainer-reviewed upgrade policy.
