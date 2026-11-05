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

Custom `ResolveVariables()` implementations may change the supplied variable
dictionary and resolution options, but must not edit source tokens. References
resolve right-to-left. The evaluator observes dictionary and option changes
before the next pending reference; values and syntax already consumed remain
frozen. Inline replacements happen as each reference resolves. Enabling inline
updates later does not retroactively replace references already consumed.

Source-token state is fixed during resolution. Callbacks must not change
`PrimitiveToken.Value`, `QuoteChar`, other source properties, or child sequences.
This includes assigning an aggregate's `Value`, even when the replacement text
is equivalent. Perform all source edits before starting resolution.
Resolver-owned inline replacements are the only exception. Guarded source
setters and collection operations reject prohibited edits before mutation.
Edits through a protected raw token list or a consumer-defined quote setter are
detected at callback boundaries and throw `InvalidOperationException`; they are
not rolled back.

`AggregateToken.Tokens` remains a live `IEnumerable<Token>`, backed by a guarded
`IList<Token>` view rather than a `List<Token>`. Use the list interface for
low-level edits outside resolution; casting the view to `List<Token>` is no
longer supported. Syntax-aware editable collections remain the preferred editing
surface.

Callbacks must complete dictionary and option changes before returning. A callback
may wait for a worker that performs those permitted changes. Guard checks run on
the synchronous resolution thread; they are not cross-thread locks. Do not edit
active source tokens from worker threads or continue mutations after the callback
returns. Separate resolutions may share immutable tokens, but callers must
synchronize writes to shared tokens outside resolution.

Perform source-token mutations before calling resolution. Put variable-dictionary
and option changes in resolver callbacks, not serializers or quote getters.
Behavior of mutating serializers/getters is unspecified; existing incidental
behavior is not a compatibility guarantee.

## Serialized ARG instructions

Non-inline stage resolution reparses ARG syntax into a detached instruction.
For an ordinary `ArgInstruction`, original declaration resolvers remain available
when each serialized declaration corresponds to one parsed declaration. If a
default serializer merges or splits declarations, the parsed declarations are
authoritative instead.

In this non-inline path, each declaration's effective name is consumed before
its override lookup or default resolution. A callback changing external name
serializer state afterward does not move that declaration's binding to another
name; later declarations still observe those changes.

For any `ArgInstruction` subclass, the complete serialized instruction is
authoritative in non-inline mode. Original declaration resolvers are not invoked
in that mode, even if the subclass delegates serialization to its base class.
Use an ordinary `ArgInstruction` with custom default tokens to retain those
resolvers in both modes, or opt into inline resolution when original subclass
tokens must be evaluated.

These rules apply to stage ARG instructions. Global ARGs are evaluated directly
from their declaration tokens.
