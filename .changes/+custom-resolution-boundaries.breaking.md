### Define custom variable-resolution boundaries

#### Previous behavior

Custom serialization and quote getters had no explicit side-effect contract.
Non-inline stage ARG resolution attempted to associate subclass declaration
tokens with emitted text, sometimes invoking their original custom resolvers.

#### New behavior

Serializers and quote getters must be side-effect-free and repeatable for the
same inputs. Resolver callbacks may still mutate pending primitive values,
bindings, and options before returning, but cannot add, remove, reorder, or
replace token children. Resolver-owned inline replacements remain supported.
Non-inline stage resolution of an `ArgInstruction` subclass evaluates its parsed
serialized text, without invoking the original declarations' custom resolvers.

#### Type of breaking change

Behavioral compatibility changes for consumer-defined token implementations and
stage ARG instruction subclasses. Public method signatures are unchanged.

#### Reason for change

Side effects during validation invalidate the values being checked. Serializer
call observations cannot establish which returned strings an arbitrary
instruction serializer emitted.

#### Recommended action

Move scalar mutations into `ResolveVariables()` callbacks and perform structural
edits before resolution. Keep serializers and quote getters repeatable. Use an
ordinary `ArgInstruction` containing custom default tokens to retain original resolver
behavior in both modes, or use inline resolution when evaluating original
subclass tokens is required. See [Variable resolution](../docs/variable-resolution.md).

#### Affected APIs

`Dockerfile.ResolveVariables()`, custom `Token.GetUnderlyingValue()`,
`IQuotableToken.QuoteChar`, `AggregateToken.ResolveVariables()`, and stage
`ArgInstruction` subclasses.
