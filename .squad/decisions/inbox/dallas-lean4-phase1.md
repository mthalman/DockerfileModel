### 2026-03-05: Lean 4 Formal Verification Phase 1 — Token Model Specification
**Author:** Dallas (Core Dev)
**Date:** 2026-03-05
**Branch:** formal-verification-lean

#### Context
Phase 0 (FsCheck property-based testing) is complete and merged. Phase 1 creates the Lean 4 formal specification project that models the C# token hierarchy and proves fundamental properties about token concatenation.

#### Decisions

**1. Single recursive `toString` instead of `getUnderlyingValue` + `toString`**

The C# codebase uses two methods: `Token.GetUnderlyingValue()` (virtual, overridden in `VariableRefToken`) and `Token.ToString()` (sealed, adds quote wrapping). In the Lean model, these are combined into a single `Token.toString` function to avoid mutual recursion. The `variableRef` kind case handles the `$` prefix, and the `quoteInfo` match handles quote wrapping. This simplifies termination checking and proof structure.

**2. Token modeled as two-constructor inductive with kind tags**

Rather than a flat inductive with 13+ constructors (one per C# token subclass), we use:
- `Token.primitive (kind : PrimitiveKind) (value : String)` — 4 primitive kinds
- `Token.aggregate (kind : AggregateKind) (children : List Token) (quoteInfo : Option QuoteInfo)` — 9 aggregate kinds

This mirrors the C# two-level hierarchy and allows proofs to be stated generically over all non-variableRef aggregate kinds.

**3. Quote info as optional field rather than separate type**

`Option QuoteInfo` on the `aggregate` constructor models the C# `IQuotableToken` interface. Only `literal` and `identifier` kinds would realistically have `some` quote info, but the type system doesn't enforce this — the proofs handle all cases uniformly.

**4. Lean 4 v4.27.0 toolchain**

Pinned to the stable v4.27.0 release (January 23, 2025). This is a well-tested release with good support for nested inductive types and structural recursion over `List Token` children.

**5. Proof strategy: `unfold` + `rfl` for concrete kinds, `cases` + `simp_all` for general theorems**

Specialized theorems for individual kinds (keyword, literal, etc.) can be proved by `unfold Token.toString; rfl` because the nested matches fully reduce. The general theorem parameterized over `kind ≠ .variableRef` uses `cases kind <;> simp_all` to discharge all 9 cases.

**6. CI integration: independent `lean` job**

The lean job runs independently from the .NET build job. It installs elan, then runs `lake build` in the `lean/` directory. This keeps lean proof checking decoupled from the .NET build and avoids adding lean as a dependency of the main build.

#### Files Changed
- `lean/` — entire new directory (7 Lean files + lakefile + toolchain)
- `.github/workflows/ci.yml` — added `lean` job

#### Result
8 formal theorems proving token concatenation properties. 7 executable test suites covering primitive identity, aggregate concatenation, variableRef prefix, quote wrapping, Dockerfile concatenation, instruction name mapping, and recursive tree consistency.
