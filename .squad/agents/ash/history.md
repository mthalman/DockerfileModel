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
Team update (2026-03-06T00:12:22Z): Phase 5 Capstone proofs completed: 12 new theorems in Capstone.lean, token_concat_length fixed in RoundTrip.lean, proof coverage documented. Total: 55 proved, 4 documented sorries. Build: 19 jobs, 0 errors. — decided by Dallas
