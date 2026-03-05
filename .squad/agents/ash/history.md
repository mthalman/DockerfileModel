# Project Context

- **Owner:** Matt Thalman
- **Project:** Valleysoft.DockerfileModel — a .NET library for parsing and generating Dockerfiles with full fidelity. Parsed content round-trips character-for-character, including whitespace.
- **Stack:** C# (.NET Standard 2.0 / .NET 6.0), Sprache parser combinator, xUnit, NuGet
- **Created:** 2026-03-04

## Key Docs Info

- Published as NuGet package: Valleysoft.DockerfileModel
- Directory.Build.props has shared packaging properties (license, authors, repo URL)
- Public API surface: Dockerfile.Parse(), DockerfileBuilder, StagesView, ImageName
- XML doc comments on public types

## Key Paths

- `src/Valleysoft.DockerfileModel/` — library
- `src/Directory.Build.props` — NuGet metadata
- `src/Valleysoft.DockerfileModel.Tests/ScenarioTests.cs` — usage examples

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
