# Variable resolution

`Dockerfile.ResolveVariables()` establishes ARG bindings and returns the last
processed instruction, not the complete Dockerfile. By default it leaves the
model unchanged. Set `ResolutionOptions.UpdateInline` to replace resolved
references, then call `Dockerfile.ToString()` for the complete document.

## ARG bindings

Global ARGs apply to FROM. Each stage has its own local ARG scope. An assigned
declaration replaces the previous binding; a bare declaration imports a global
value when available and otherwise keeps the current local binding. An empty
assignment counts as set. A null override leaves the ARG unset.

Ordinary defaults decode quote and escape syntax and omit comments, newlines,
and line continuations. A custom resolver's result is opaque data: quote
characters in its returned value are not stripped. Direct
`AggregateToken.ResolveVariables()` preserves surrounding syntax quotes and
does not establish document ARG scope.

## Custom token contract

Custom serialization and quote getters must be side-effect-free. Given the same
token state, external state, and formatting options, repeated reads must produce
the same result or exception. Reads must not mutate tokens, bindings, resolution
options, or state that changes subsequent reads. Instrumentation is permitted
when it does not change those results. Serialization call counts and invocation
order are not contractual.

Custom `ResolveVariables()` implementations may change pending primitive values,
the supplied environment, and resolution options. References resolve right-to-left.
The evaluator observes callback edits before the next pending reference; values
and syntax already consumed remain frozen. Inline replacements happen as each
reference resolves. Enabling inline updates later does not retroactively replace
references already consumed.

The token tree has a fixed structure during resolution. Callbacks must not add,
remove, reorder, or replace children, including changes made by assigning an
aggregate's `Value`. Perform structural edits before starting resolution.
Resolver-owned inline replacements are allowed. Public structural operations
reject prohibited edits before mutation. Edits through a protected raw token
list are detected at callback boundaries and throw `InvalidOperationException`;
they are not rolled back.

Callbacks must complete their mutations before returning. A callback may wait
for a worker that performs those mutations; concurrent edits that continue after
the callback returns are not supported. Separate resolutions may share immutable
tokens, but callers must synchronize writes to shared tokens.

Move scalar mutations out of serializers and quote getters into resolver
callbacks, and perform structural mutations before calling resolution. Behavior
of mutating serializers/getters is unspecified; existing incidental behavior is
not a compatibility guarantee.

## Serialized ARG instructions

Non-inline stage resolution reparses ARG syntax into a detached instruction.
For an ordinary `ArgInstruction`, original declaration resolvers remain available
when each serialized declaration corresponds to one parsed declaration. If a
default serializer merges or splits declarations, the parsed declarations are
authoritative instead.

For any `ArgInstruction` subclass, the complete serialized instruction is
authoritative in non-inline mode. Original declaration resolvers are not invoked
in that mode, even if the subclass delegates serialization to its base class.
Use an ordinary `ArgInstruction` with custom default tokens to retain those
resolvers in both modes, or opt into inline resolution when original subclass
tokens must be evaluated.

These rules apply to stage ARG instructions. Global ARGs are evaluated directly
from their declaration tokens.
