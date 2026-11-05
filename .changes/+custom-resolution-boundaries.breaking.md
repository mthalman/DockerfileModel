### Define custom variable-resolution boundaries

Custom token implementations must keep source tokens unchanged during variable
resolution. Custom serializers and getters of `IQuotableToken.QuoteChar` must be
side-effect-free and repeatable. Non-inline stage ARG resolution uses an
instruction subclass's serialized text instead of its original declaration
resolvers. These requirements affect custom token implementations, stage ARG
subclasses, and code that casts `AggregateToken.Tokens` to `List<Token>`.

#### Previous behavior

Custom serializers and quote getters had no explicit side-effect contract.
Resolver callbacks could change source-token values and structure while the
library evaluated pending references.

Non-inline stage ARG resolution reparsed serialized instruction text and
evaluated the parsed declarations. It did not invoke the original subclass
declarations' custom resolvers.

`AggregateToken.Tokens` exposed the underlying `List<Token>`.

#### New behavior

Custom implementations must follow these rules:

- Serializers and quote getters must be side-effect-free. For unchanged token
  state, external state, and formatting options, repeated reads must produce the
  same result or exception.
- Resolver callbacks may change the supplied variable dictionary and resolution
  options before returning. They must not change source-token properties or
  children. This includes assigning `PrimitiveToken.Value`, `QuoteChar`, or an
  aggregate's `Value`, even when the replacement text is equivalent. Guarded
  setters and collection operations reject these edits with
  `InvalidOperationException`. Library-owned inline replacements are supported.
- `AggregateToken.Tokens` reflects changes to the aggregate's children but does
  not expose the underlying `List<Token>`. Its guarded collection rejects edits
  during resolution. The declared `IEnumerable<Token>` type is unchanged.
- Non-inline stage resolution reparses an `ArgInstruction` subclass's serialized
  text and evaluates the parsed declarations. It does not invoke the original
  declarations' custom resolvers. This preserves the previous behavior for
  instruction subclasses.

#### Type of breaking change

This is a behavioral breaking change for code that mutates source tokens during
resolution, relies on side effects in serializers or quote getters, or casts
`Tokens` to `List<Token>`. Public method signatures are unchanged.

#### Reason for change

Keeping source tokens unchanged prevents callbacks from invalidating syntax
already captured for pending references. Side-effect-free serializers and quote
getters allow the library to inspect tokens without changing resolution results.

A custom instruction serializer can combine or omit declaration text. The
library cannot reliably map that output back to the original declaration tokens,
so non-inline resolution uses the serialized instruction as the source of truth.

#### Recommended action

Update affected custom implementations as follows:

- Move source-token edits before resolution. Put variable-dictionary and option
  changes in `ResolveVariables()` callbacks, not serializers or quote getters.
- Make serializers and quote getters side-effect-free and repeatable.
- For low-level edits outside resolution, replace `List<Token>` casts of
  `AggregateToken.Tokens` with `IList<Token>`. Prefer syntax-aware editable
  collections when possible.
- To retain custom default-token resolvers in both modes, use an ordinary
  `ArgInstruction` with custom default tokens. To evaluate an instruction
  subclass's original tokens, use inline resolution.

See [Variable resolution](../docs/variable-resolution.md) for the full contract
and examples.

#### Affected APIs

`Dockerfile.ResolveVariables()`, custom `Token.GetUnderlyingValue()`,
`IQuotableToken.QuoteChar`, `AggregateToken.ResolveVariables()`, and stage
`ArgInstruction` subclasses.
