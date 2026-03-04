# AGENTS.md

This file provides guidance to AI coding agents when working with code in this repository.

## Project Overview

Valleysoft.DockerfileModel is a .NET library for parsing and generating Dockerfiles with full fidelity — parsed content round-trips character-for-character, including whitespace. Published as NuGet package `Valleysoft.DockerfileModel`.

## Build Commands

All commands run from the `src/` directory:

```bash
cd src
dotnet restore
dotnet build -c Release --no-restore
dotnet test --no-restore -v normal -c Release
```

Run a single test:
```bash
dotnet test --no-restore -c Release --filter "FullyQualifiedName~FromInstructionTests.Parse"
```

## Architecture

The library uses a **token-based model** with the [Sprache](https://github.com/sprache/Sprache) parser combinator library. The hierarchy is:

- **Dockerfile** — top-level container, parses via `Dockerfile.Parse(string)`. Contains a list of `DockerfileConstruct` items.
- **DockerfileConstruct** — base class for all elements: `Instruction`, `Comment`, `ParserDirective`, `Whitespace`.
- **Instruction** — base for all Dockerfile instructions (FROM, RUN, COPY, etc.). Each instruction type has its own class (e.g., `FromInstruction`, `RunInstruction`).
- **Token** — fine-grained elements that compose constructs. Includes `KeywordToken`, `LiteralToken`, `VariableRefToken`, `LineContinuationToken`, etc. Stored in `Tokens/` subdirectory.

Key supporting classes:
- **DockerfileBuilder** — fluent API for constructing Dockerfiles programmatically.
- **StagesView / Stage** — organizes a Dockerfile by multi-stage build stages (global ARGs + per-stage groupings).
- **ImageName** — parses image references into registry, repository, tag, and digest components.
- **ParseHelper** (`ParseHelper.cs`) — large internal helper containing Sprache parser definitions for all instruction formats.

Variable resolution: `Dockerfile.ResolveVariables()` resolves ARG references either globally or for a specific instruction. Non-mutating by default; pass `UpdateInline = true` to modify the model in place.

## Testing

Tests use **xUnit** with `[Theory]`/`[InlineData]` for data-driven tests and `[Fact]` for simple cases. Each instruction type has a corresponding test file (e.g., `FromInstructionTests.cs`). `ScenarioTests.cs` contains integration-level examples demonstrating API usage. `TestHelper.cs` provides shared utilities like `ConcatLines()`.

## Project Layout

- `src/Valleysoft.DockerfileModel/` — library targeting `netstandard2.0` and `net6.0` (C# 10, nullable enabled)
- `src/Valleysoft.DockerfileModel.Tests/` — test project targeting `net8.0`
- `global.json` — pins .NET SDK version
- `src/Directory.Build.props` — shared MSBuild properties (license, authors, repo URL)
