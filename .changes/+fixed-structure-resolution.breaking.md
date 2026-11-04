### Reject callback edits to source tokens during variable resolution

#### Previous behavior

Resolution callbacks could edit source token values, quotes, or child lists
while the resolver was traversing them. Results depended on the
resolution entry point and edit timing. `AggregateToken.Tokens` exposed the
underlying `List<Token>` through its `IEnumerable<Token>` return value.

#### New behavior

Callbacks may change variable dictionaries and resolution options, but may not
edit source token values, quotes, child lists, or active document constructs.
Source edits throw
`InvalidOperationException`. Guarded setters and views reject before mutation;
protected raw-list edits and custom quote implementations that bypass native setters
are detected at callback boundaries without rollback. Already-published inline
substitutions remain published.

`Tokens` returns a cached live guarded view. Its declared signature remains
`IEnumerable<Token>`, and the view supports `IList<Token>` writes outside
resolution, but it is no longer a `List<Token>`. Library-owned inline
substitutions and synchronous reentrant resolution remain supported.
Serializers and quote getters must be side-effect-free and repeatable.

#### Type of breaking change

This is a behavioral compatibility change, including the runtime type returned
by `Tokens`. Public method and property signatures are unchanged. Existing code
that casts `Tokens` to `List<Token>` must change its cast.

#### Reason for change

A fixed source gives callback evaluation and inline publication a
consistent contract without restarting or reconciling arbitrary source edits.
Guarded public views also avoid full native-tree scans at every callback.

#### Recommended action

Move source token edits before or after resolution. Inside callbacks, update
only the environment or options. Use the owner's syntax-aware collections for
validated edits outside resolution.

For intentional low-level edits outside resolution, replace
`(List<Token>)literal.Tokens` with `(IList<Token>)literal.Tokens`. Do not assume
that catching a rejected raw-list edit restores the source; repair or discard
the edited model before reusing it.

See the [resolution callback contract](../docs/editing.md#resolution-callbacks)
for callback ordering, protected-list detection, and reentrancy boundaries.

#### Affected APIs

Both `Dockerfile.ResolveVariables` overloads and
`AggregateToken.ResolveVariables`, including instruction, `LiteralToken`, and
`VariableRefToken` resolution, use the immutable-source callback contract.
`AggregateToken.Tokens` returns the guarded live view. Token setters,
syntax-aware collections, and document construction reject source writes
to an active source from callbacks. Consumer serializers and quote getters
must remain side-effect-free.
