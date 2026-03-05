# Project Context

- **Owner:** Matt Thalman
- **Project:** Valleysoft.DockerfileModel — a .NET library for parsing and generating Dockerfiles with full fidelity. Parsed content round-trips character-for-character, including whitespace.
- **Stack:** C# (.NET Standard 2.0 / .NET 6.0), Sprache parser combinator, xUnit, NuGet
- **Created:** 2026-03-04

## Key Test Patterns

- xUnit with Theory/InlineData for data-driven tests
- Each instruction type has a corresponding test file (e.g., FromInstructionTests.cs)
- ScenarioTests.cs has integration-level examples
- TestHelper.cs provides shared utilities like ConcatLines()
- Round-trip fidelity: parse → ToString() must match original input exactly

## Key Paths

- `src/Valleysoft.DockerfileModel.Tests/` — test project (net8.0)
- `src/Valleysoft.DockerfileModel/` — library under test

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
