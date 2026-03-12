# Decision: Multi-Heredoc Token Partitioning Strategy

**Date:** 2026-03-10
**Author:** Dallas (Core Dev)
**Status:** ~~Implemented~~ **Superseded** by heredoc marker/body token split (2026-03-10)
**Scope:** ~~HeredocToken, ParseHelper.HeredocTokenParseImpl~~ → HeredocMarkerToken, HeredocBodyToken, Heredoc, ParseHelper.HeredocTokenParseImpl

## Context

Issue #245 requires supporting multiple heredoc markers per instruction (e.g., `RUN <<FILE1 cmd1 && <<FILE2 cmd2`).

## Current Design (supersedes original decision below)

The implementation now uses a **split marker/body token architecture**:
- **`HeredocMarkerToken`** — inline in the command stream, decomposes to `SymbolToken('<') + SymbolToken('<') + [SymbolToken('-')] + [quote SymbolTokens] + HeredocDelimiterToken`
- **`HeredocBodyToken`** — sequential after the command line, contains body `StringToken` + closing `HeredocDelimiterToken` + optional trailing `NewLineToken`
- **`Heredoc`** — semantic wrapper pairing marker+body positionally via `HeredocList` property
- **`HeredocDelimiterToken`** — extends `IdentifierToken`, used in both marker and body tokens

This design provides uniform metadata access regardless of position: all markers have full token decomposition, all bodies have consistent structure. `Heredocs` is derived as `HeredocBodyTokens.Select(h => h.Content)`.

## Original Decision (obsolete — retained for historical context)

Originally chose multiple `HeredocToken` instances with explicit metadata fields on subsequent tokens. This was superseded because it created an asymmetric token structure where first vs. subsequent heredocs had fundamentally different internal representations.

## Files Changed (current)

- `src/Valleysoft.DockerfileModel/Tokens/HeredocMarkerToken.cs`
- `src/Valleysoft.DockerfileModel/Tokens/HeredocBodyToken.cs`
- `src/Valleysoft.DockerfileModel/Tokens/HeredocDelimiterToken.cs`
- `src/Valleysoft.DockerfileModel/Heredoc.cs`
- `src/Valleysoft.DockerfileModel/ParseHelper.cs`
- `src/Valleysoft.DockerfileModel.Tests/HeredocTests.cs`
