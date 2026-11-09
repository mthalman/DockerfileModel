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

From your .NET application's project directory, install it with:

```text
dotnet add package Valleysoft.DockerfileModel
```

Parse a Dockerfile, edit its collections, and serialize the result:

```csharp
using System;
using System.Linq;
using Valleysoft.DockerfileModel;

Dockerfile dockerfile = Dockerfile.Parse(
    "FROM alpine AS build\nCOPY src/ /app/\n");
CopyInstruction copy = dockerfile.Items.OfType<CopyInstruction>().Single();
copy.Sources.Add("generated/");
dockerfile.Items.Insert(dockerfile.Items.IndexOf(copy) + 1, new RunInstruction("echo ready"));
Console.WriteLine(dockerfile);
```

The output preserves the existing formatting and adds the new source and instruction:

```dockerfile
FROM alpine AS build
COPY src/ generated/ /app/
RUN echo ready
```

The maintained [usage examples](https://github.com/mthalman/DockerfileModel/blob/main/src/Valleysoft.DockerfileModel.Samples/UsageExamples.cs)
cover round trips, image edits, ARG resolution, stages, and fluent construction.
The [advanced examples](https://github.com/mthalman/DockerfileModel/blob/main/src/Valleysoft.DockerfileModel.Samples/AdvancedExamples.cs)
cover coordinated edits, dependency analysis, recovery, and nested comments.

## Guides

See [Collection editing](https://github.com/mthalman/DockerfileModel/blob/main/docs/editing.md) for document items, value/token lists,
comments, mount entries, paired heredocs, and trivia policies. Existing scalar
property setters are unchanged. Upgrading consumers should read the
[versioned migration guides](https://github.com/mthalman/DockerfileModel/blob/main/docs/migrations/README.md).

Use `Dockerfile.Analyze()` to inspect stage dependencies and external image references
without modifying the model. See [Stage dependencies and image inventory](https://github.com/mthalman/DockerfileModel/blob/main/docs/stage-analysis.md)
for examples, API contracts, resolution rules, and limitations.

See [Parsing, recovery, and diagnostics](https://github.com/mthalman/DockerfileModel/blob/main/docs/parsing.md) for parse options, recovery behavior, diagnostic codes, and source locations.

See [Parser directives and frontend metadata](https://github.com/mthalman/DockerfileModel/blob/main/docs/parser-directives.md) for typed
syntax, escape, and check directives, header placement rules, and lossless editing.

See [Native AOT and trimming](https://github.com/mthalman/DockerfileModel/blob/main/docs/native-aot.md) for supported targets and
restrictions on consumer-defined subclasses.

See [Variable resolution](https://github.com/mthalman/DockerfileModel/blob/main/docs/variable-resolution.md) for ARG scopes, callback
mutation boundaries, and custom serializer requirements.

See [Dockerfile compatibility](https://github.com/mthalman/DockerfileModel/blob/main/docs/dockerfile-compatibility.md) for the pinned
stable frontend target, upstream conformance corpus, known limitations, and
maintainer-reviewed upgrade policy.
