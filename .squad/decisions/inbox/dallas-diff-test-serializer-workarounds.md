### 2026-03-06: TokenJsonSerializer workaround pattern for known tokenization differences
**Author:** Dallas (Core Dev)

#### Context
Differential testing between C# and Lean parsers found 314/900 mismatches (35%). These fall into two categories:
1. Pure serializer mapping gaps (C# OOP wrappers with no Lean counterpart)
2. Genuine tokenization differences (C# and Lean produce structurally different token trees)

#### Decision
**Workaround-per-instruction pattern**: Rather than applying broad transformations that might mask new bugs, the serializer uses instruction-type-specific methods (e.g., `SerializeLabel`, `SerializeExpose`) that apply targeted workarounds only where known differences exist. Each workaround is paired with a GitHub issue tracking the underlying C# fix.

**BooleanFlag maps to `keyValue`**: `BooleanFlag` (LinkFlag, KeepGitDirFlag) is not a transparent wrapper; it maps to `keyValue` kind, matching Lean's `booleanFlagParser` which produces `KeyValueToken`.

**UserAccount conditional transparency**: UserAccount with group -> `keyValue`; UserAccount without group -> transparent (inline children). This matches Lean's conditional wrapping behavior.

**Shell form whitespace splitting**: Applied only inside shell form command LiteralTokens (RUN, CMD, ENTRYPOINT, and CMD-inside-HEALTHCHECK), not all LiteralTokens. The `SplitStringByWhitespace` helper splits by space/tab boundaries.

#### Why this matters
The diff test suite is designed to be a bug-finding oracle. Workarounds must be precise enough to suppress known differences without hiding new bugs. The instruction-specific dispatch pattern achieves this.
