# Decision: Multi-Heredoc Token Partitioning Strategy

**Date:** 2026-03-10
**Author:** Dallas (Core Dev)
**Status:** Implemented
**Scope:** HeredocToken, ParseHelper.HeredocTokenParseImpl

## Context

Issue #245 requires supporting multiple heredoc markers per instruction (e.g., `RUN <<FILE1 cmd1 && <<FILE2 cmd2`). The existing `HeredocTokenParseImpl` only handled a single heredoc per instruction.

## Decision

Chose **Option B — Multiple HeredocTokens with explicit metadata** over Ripley's original split-token design (HeredocMarkerToken/HeredocBodyToken). The split-token approach would have required new token classes, changes to instruction parsers, and updates to the differential testing serializer. Option B is a minimal, backward-compatible change.

## Token Partitioning for Round-Trip Fidelity

The key challenge: in multi-heredoc, markers appear on the same command line but bodies appear sequentially afterward. For `Parse(text).ToString() == text` to hold, the text must be partitioned across HeredocTokens such that concatenation reproduces the original.

**Partition strategy:**
- **First HeredocToken** — contains its marker + the entire rest-of-command-line (including subsequent markers and interleaved text) + newline + its own body + its closing delimiter. Properties computed from child tokens (backward compatible).
- **Subsequent HeredocTokens** — contain only their body lines + closing delimiter as child tokens. Store explicit metadata (`body`, `delimiterName`, `chomp`, `isQuoted`) because these can't be computed from their limited child tokens.

This partition preserves round-trip fidelity: HT1.ToString() + HT2.ToString() + ... = original text.

## Trade-offs

- First heredoc's child tokens include other markers in its rest-of-line StringToken. This is consistent with how single-heredoc COPY already works (destination absorbed into rest-of-line StringToken).
- Subsequent heredocs have limited child tokens (no marker in children). The explicit metadata fields compensate for this.
- Single-heredoc path is preserved exactly as-is (zero behavioral change for existing code).

## Files Changed

- `src/Valleysoft.DockerfileModel/Tokens/HeredocToken.cs`
- `src/Valleysoft.DockerfileModel/ParseHelper.cs`
- `src/Valleysoft.DockerfileModel.Tests/HeredocTests.cs`
