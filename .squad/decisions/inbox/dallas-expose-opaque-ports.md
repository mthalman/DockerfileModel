# Decision: EXPOSE Port Specs Are Opaque Values

**Author:** Dallas (Core Dev)
**Date:** 2026-03-09
**PR:** #252 — branch `squad/242-expose-multi-port`

## Context

BuildKit's parser treats `80/tcp` as a single opaque string — it does NOT decompose port and protocol into separate tokens. The prior C# implementation diverged by splitting `80/tcp` into `LiteralToken("80")`, `SymbolToken("/")`, `LiteralToken("tcp")`. The Lean spec already matched BuildKit.

## Decision

**Treat EXPOSE port specs as opaque values.** Each whitespace-separated argument to EXPOSE is a single `LiteralToken` regardless of whether it contains a `/protocol` suffix.

### What Changed

**Parser:** `GetArgsParser` changed from a two-stage combinator (port literal excluding `/`, then optional `/protocol`) to:
```csharp
ArgTokens(LiteralWithVariables(escapeChar).AsEnumerable(), escapeChar).AtLeastOnce().Flatten()
```
The `/` is no longer excluded from the literal parser, so `80/tcp` is consumed as a single token.

**Removed APIs:**
- `GetProtocolTokenForPort(LiteralToken portToken)` — protocol is now part of the opaque string
- `SetProtocolForPort(LiteralToken portToken, string? protocol)` — same reason
- `GetProtocolTokenForPortInternal` — private helper, no longer needed
- `ValidatePortToken` — no longer needed
- `FilterPortTokens` — no longer needed (all LiteralTokens are port specs)
- `IsSlashSymbol` — no longer needed

**Constructor:** Changed from `(string port, string? protocol = null, char escapeChar = ...)` to `(string portSpec, char escapeChar = ...)`. Callers pass `"80/tcp"` directly.

**Kept APIs:**
- `IList<string> Ports` — projected from PortTokens, each string is the full opaque spec
- `IList<LiteralToken> PortTokens` — the underlying tokens

### Token Structure Before vs After

| Input | Before | After |
|-------|--------|-------|
| `EXPOSE 80` | `[Keyword, WS, Literal("80")]` | `[Keyword, WS, Literal("80")]` |
| `EXPOSE 80/tcp` | `[Keyword, WS, Literal("80"), Symbol("/"), Literal("tcp")]` | `[Keyword, WS, Literal("80/tcp")]` |
| `EXPOSE 80/tcp 443/udp` | `[Keyword, WS, Literal("80"), Symbol("/"), Literal("tcp"), WS, Literal("443"), Symbol("/"), Literal("udp")]` | `[Keyword, WS, Literal("80/tcp"), WS, Literal("443/udp")]` |

## Rationale

- Matches BuildKit's parser behavior exactly
- Aligns with the Lean formal spec
- Simpler implementation — 50 lines vs 185 lines
- Round-trip fidelity preserved (all 708 tests pass)
- The `Ports` and `PortTokens` list APIs already existed for multi-port support and remain unchanged

## Migration Guide

| Before | After |
|--------|-------|
| `instruction.Port` | `instruction.Ports[0]` |
| `instruction.Protocol` | Parse from `instruction.Ports[0]` (e.g., split on `/`) |
| `instruction.PortToken` | `instruction.PortTokens[0]` |
| `instruction.ProtocolToken` | N/A — protocol is part of the opaque port spec |
| `new ExposeInstruction("80", "tcp")` | `new ExposeInstruction("80/tcp")` |
| `instruction.SetProtocolForPort(token, "tcp")` | `instruction.Ports[i] = instruction.Ports[i].Split('/')[0] + "/tcp"` |

## Files Changed

- `src/Valleysoft.DockerfileModel/ExposeInstruction.cs` — reworked (185 → 50 lines)
- `src/Valleysoft.DockerfileModel/DockerfileBuilder.cs` — updated builder method signature
- `src/Valleysoft.DockerfileModel.Tests/ExposeInstructionTests.cs` — complete test rewrite
