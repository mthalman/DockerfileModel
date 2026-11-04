### Define custom variable-resolution boundaries

#### Previous behavior

Custom serialization and quote getters had no explicit side-effect contract.
Non-inline stage ARG resolution attempted to associate subclass declaration
tokens with emitted text, sometimes invoking their original custom resolvers.
Resolver callbacks could change source-token values and structure while pending
references were being evaluated.

#### New behavior

Serializers and quote getters must be side-effect-free and repeatable for the
same inputs. Resolver callbacks may change the variable dictionary and resolution
options before returning, but cannot edit source tokens. This prohibits scalar
changes such as `PrimitiveToken.Value` and `QuoteChar`, as well as child-list
edits. Resolver-owned inline replacements remain supported.
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

Perform all source-token edits before resolution. Use `ResolveVariables()`
callbacks only for variable-dictionary and option changes. Keep serializers and
quote getters repeatable. Use an ordinary `ArgInstruction` containing custom
default tokens to retain original resolver
behavior in both modes, or use inline resolution when evaluating original
subclass tokens is required. See [Variable resolution](../docs/variable-resolution.md).

#### Affected APIs

`Dockerfile.ResolveVariables()`, custom `Token.GetUnderlyingValue()`,
`IQuotableToken.QuoteChar`, `AggregateToken.ResolveVariables()`, and stage
`ArgInstruction` subclasses.
