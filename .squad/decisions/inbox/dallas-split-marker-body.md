# Decision: Split Marker/Body Heredoc Architecture

**Date:** 2026-03-10
**Author:** Dallas (Core Dev)
**Status:** Implemented
**Issue:** #245

## Context

The original `HeredocToken` bundled the `<<MARKER`, rest-of-line text, newline, body lines, and closing delimiter into a single aggregate token. This design had structural problems:

1. For multi-heredoc instructions like `RUN <<FILE1 cat > /f1 && <<FILE2 cat > /f2`, the second `<<FILE2` marker was buried as plain text inside the first HeredocToken's rest-of-line StringToken.
2. COPY/ADD destination paths (e.g., `/app/script.sh` in `COPY <<EOF /app/script.sh`) were absorbed into the heredoc token, making `Destination` return null.
3. The multi-heredoc workaround used explicit metadata fields on non-first tokens, creating an asymmetric data model.

## Decision

Replace the single `HeredocToken` with two sibling token types at the instruction level:

- **`HeredocMarkerToken`** — represents `<<[-][QUOTE]DELIM[QUOTE]` inline in the command stream, as a peer of KeywordToken and StringToken.
- **`HeredocBodyToken`** — represents body lines + closing delimiter, appearing after the command line's NewLineToken.

A **`Heredoc`** semantic wrapper pairs markers with bodies by position (first marker = first body).

## Token Structure

For `RUN <<EOF\necho hello\nEOF\n`:
```
KeywordToken("RUN"), WhitespaceToken(" "), HeredocMarkerToken("<<EOF"), NewLineToken("\n"), HeredocBodyToken(...)
```

For `COPY <<EOF /dest\nhello\nEOF\n`:
```
KeywordToken("COPY"), WhitespaceToken(" "), HeredocMarkerToken("<<EOF"), WhitespaceToken(" "), LiteralToken("/dest"), NewLineToken("\n"), HeredocBodyToken(...)
```

For `RUN <<A <<B\nbody_a\nA\nbody_b\nB\n`:
```
KeywordToken("RUN"), WhitespaceToken(" "), HeredocMarkerToken("<<A"), WhitespaceToken(" "), HeredocMarkerToken("<<B"), NewLineToken("\n"), HeredocBodyToken(...A...), HeredocBodyToken(...B...)
```

## Consequences

- **Round-trip fidelity preserved**: `Parse(text).ToString() == text` for all inputs.
- **Multi-heredoc naturally supported**: Each marker and body is its own token; no special metadata needed.
- **COPY/ADD destination works**: Destination text is tokenized as WhitespaceToken + LiteralToken segments in the command stream, not absorbed.
- **Breaking change**: `HeredocToken` type removed. Code referencing it must use `HeredocMarkerToken`/`HeredocBodyToken`.
- **New API surface**: `HeredocList` property returns `IReadOnlyList<Heredoc>` with paired marker+body objects.
- **All 1038 tests pass** including 204 heredoc-specific tests.
