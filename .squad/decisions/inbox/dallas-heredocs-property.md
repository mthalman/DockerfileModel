# Decision: HeredocToken.Body as the projection target for Heredocs property

**Date:** 2026-03-09
**Author:** Dallas (Core Dev)
**Status:** Implemented

## Context

The task required adding a simple-typed `Heredocs` property (`IEnumerable<string>`) to `FileTransferInstruction` and `RunInstruction`, analogous to how `Command` returns the command string from a `CommandToken`.

## Decision

Rather than inlining the body-extraction logic in both instruction classes, I added a `Body` property directly on `HeredocToken`. This encapsulates the knowledge of HeredocToken's internal token layout (marker, optional rest-of-line, newline, body lines, closing delimiter) in a single place. The `Heredocs` property on both instruction classes is then a one-liner: `HeredocTokens.Select(h => h.Body)`.

## Alternatives Considered

1. **Inline extraction in each instruction class** — would duplicate the token-structure logic in two places and require updates in both if the HeredocToken layout ever changes.
2. **Static helper method** — less discoverable, and the body is intrinsically a property of the heredoc itself.

## Consequences

- `HeredocToken` now has a public `Body` property, which is a read-only convenience accessor. It does not affect round-trip fidelity since it derives from existing tokens without modifying them.
- Any future instruction types that contain heredocs can reuse `HeredocToken.Body` without reimplementing extraction logic.
