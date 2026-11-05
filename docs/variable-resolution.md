# Variable resolution

Use `Dockerfile.ResolveVariables()` to substitute declared ARG values in
supported Dockerfile instructions. It leaves RUN, CMD, ENTRYPOINT, and SHELL
command text unchanged, even with inline updates enabled. It does not perform
shell expansion or execute a Docker build.

This page describes ARG scope, inline updates, and the requirements for custom
token implementations. See the
[`Dockerfile` API source](../src/Valleysoft.DockerfileModel/Dockerfile.cs) for
overloads and parameter details.

## Return value and inline updates

`Dockerfile.ResolveVariables()` returns the text of the last processed
instruction, not the complete Dockerfile. By default, resolution leaves the model
unchanged. To replace resolved references in the model, set
`ResolutionOptions.UpdateInline` to `true`. Then call `Dockerfile.ToString()` to
serialize the complete document.

For example, resolve an override without changing the parsed document:

```csharp
using System;
using System.Collections.Generic;
using Valleysoft.DockerfileModel;

string source = "ARG TAG=3.21\nFROM alpine:$TAG";
Dockerfile dockerfile = Dockerfile.Parse(source);
var overrides = new Dictionary<string, string?> { ["TAG"] = "3.22" };

Console.WriteLine(dockerfile.ResolveVariables(overrides));
Console.WriteLine(dockerfile.ToString() == source);
```

The output contains the resolved instruction and confirms that the model is
unchanged:

```text
FROM alpine:3.22
True
```

To update the same document, enable inline resolution:

```csharp
dockerfile.ResolveVariables(overrides, new ResolutionOptions { UpdateInline = true });
Console.WriteLine(dockerfile);
```

The reference changes, but the declared default stays `3.21`:

```dockerfile
ARG TAG=3.21
FROM alpine:3.22
```

If you call `AggregateToken.ResolveVariables()` directly, it uses the supplied
variable dictionary without establishing document ARG scope. That method
preserves surrounding syntax quotes in the returned text.

## Dockerfile ARG semantics

`Dockerfile.ResolveVariables()` implements the Dockerfile ARG rules below, with
the stage-inheritance limitation described in this section.
`Dockerfile.Parse()` preserves variable references without evaluating them.

Global ARGs supply values for FROM instructions. The resolver starts each stage
with an empty local ARG scope. Without overrides, declarations follow these
rules:

- A declaration with an assignment replaces the previous binding.
- A bare declaration imports a global value when one exists. Otherwise, it
  keeps the current local binding.
- An empty assignment counts as set.

Ordinary defaults decode quote and escape syntax. They omit comments, newlines,
and line continuations.

Unlike Docker builds, the resolver does not inherit stage-local ARG bindings
when a stage uses another stage as its base. For example, in the following
Dockerfile, Docker inherits `X=ok` in the second stage:

```dockerfile
FROM alpine AS base
ARG X=ok
FROM base
ENV RESULT=${X-fallback}
```

The library resolves the ENV instruction to `ENV RESULT=fallback`, not
`ENV RESULT=ok`. Declare the needed ARGs in each stage, supplying values through
defaults or overrides when using this resolver.

### Global imports and stage-local bindings

With inline resolution enabled and no overrides, this input demonstrates a
global import, a bare redeclaration, and a separate stage scope:

```dockerfile
ARG BASE=alpine
FROM $BASE AS build
ARG BASE
ARG FLAVOR=debug
ARG FLAVOR
ENV RESULT=$BASE/$FLAVOR
FROM $BASE AS final
ENV RESULT=${FLAVOR-fallback}
```

The resolved document is:

```dockerfile
ARG BASE=alpine
FROM alpine AS build
ARG BASE
ARG FLAVOR=debug
ARG FLAVOR
ENV RESULT=alpine/debug
FROM alpine AS final
ENV RESULT=fallback
```

Both FROM instructions use the global `BASE`. The build stage imports `BASE`
with `ARG BASE` and preserves `FLAVOR=debug` with `ARG FLAVOR`. The final stage
does not inherit `FLAVOR`, so `${FLAVOR-fallback}` uses the fallback.

### Unset and empty values

A bare ARG without a prior binding is unset. A quoted empty default is set to
the empty string. With inline resolution enabled, this input distinguishes
the `-` and `:-` modifiers:

```dockerfile
FROM alpine
ARG UNSET
ARG EMPTY=""
ENV RESULT=${UNSET-fallback}/${EMPTY-fallback}/${EMPTY:-fallback}
```

The resolved ENV instruction is:

```dockerfile
ENV RESULT=fallback//fallback
```

The `-` modifier uses its fallback only for an unset value. The `:-` modifier
uses its fallback for an unset or empty value. The quotes in `ARG EMPTY=""`
are syntax, not part of the bound value.

## Library overrides and custom values

The `variableOverrides` parameter of `Dockerfile.ResolveVariables()` supplies
values for declared ARG names. An entry takes precedence over the declaration's
default or imported binding. A null override leaves the ARG unset, even if the
declaration has a default. See the override example under
[Return value and inline updates](#return-value-and-inline-updates).

Custom token resolvers are a library extension. Their return values are treated
as data rather than Dockerfile syntax, so resolution does not strip quote
characters from those values.

## Custom token contract

### Serializers and quote getters

Custom serialization includes implementations of `Token.GetUnderlyingValue()`.
A quote getter is the getter of `IQuotableToken.QuoteChar`, including custom
implementations and overrides. It returns the token's surrounding quote
character, or `null` when the token has no surrounding quotes.

Custom serializers and quote getters must be side-effect-free and repeatable.
For unchanged token state, external state, and formatting options, repeated
reads must return the same result or throw the same exception.

Reads must not mutate tokens, bindings, resolution options, or state that
changes subsequent reads. You can instrument reads if the instrumentation does
not change their results. Do not rely on serialization call counts or invocation
order. Put changes to variable dictionaries and resolution options in resolver
callbacks, not serializers or quote getters.

Behavior of mutating serializers or quote getters is unspecified. Do not rely
on behavior that happens to work with a particular implementation.

### Resolver callbacks

Custom `ResolveVariables()` implementations may change the supplied variable
dictionary and resolution options. They must not edit source tokens.

References resolve from right to left. Before resolving the next pending
reference, the evaluator observes dictionary and option changes from callbacks.
Values and syntax already consumed remain unchanged. When inline updates are
enabled, the library replaces each reference as it resolves. Enabling inline
updates later does not replace references that have already been consumed.

For example, a custom modifier default can update a binding used by a reference
to its left. This example calls token resolution directly with an explicit
dictionary:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel;
using Valleysoft.DockerfileModel.Tokens;

LiteralToken literal = new("${VALUE:-old}/${SET:-done}", canContainVariables: true);
literal.Tokens.OfType<VariableRefToken>().Last().ModifierValueToken =
    new UpdatingDefaultToken();

var variables = new Dictionary<string, string?> { ["VALUE"] = "old" };
Console.WriteLine(literal.ResolveVariables(Dockerfile.DefaultEscapeChar, variables));
Console.WriteLine(literal);

sealed class UpdatingDefaultToken : LiteralToken
{
    public UpdatingDefaultToken() : base("done") { }

    public override string? ResolveVariables(
        char escapeChar,
        IDictionary<string, string?>? variables = null,
        ResolutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables["VALUE"] = "new";
        return "done";
    }
}
```

The right-hand default runs first and sets `VALUE` to `new`. The left-hand
reference then reads that binding. The source tokens remain unchanged:

```text
new/done
${VALUE:-old}/${SET:-done}
```

### Source edits and errors

Perform all source-token edits before resolution. During resolution, callbacks
must not change `PrimitiveToken.Value`, `QuoteChar`, other source properties,
or child sequences. This includes assigning an aggregate's `Value`, even when
the replacement text is equivalent. Only library-owned inline replacements may
change source tokens during resolution.

Guarded setters and collection operations reject prohibited edits with
`InvalidOperationException` before changing the source. For edits through a
protected raw token list or a custom quote setter, the guard detects changes at
callback boundaries and throws `InvalidOperationException`. It does not roll
back those changes.

### Token collections

`AggregateToken.Tokens` reflects changes to the aggregate's children rather than
capturing a snapshot. Edit child tokens only outside resolution. Attempts to
add, remove, or replace children through this collection during resolution throw
`InvalidOperationException`. Prefer syntax-aware editable collections when
editing tokens.

### Threading

Callbacks must complete dictionary and option changes before returning. A
callback may wait for a worker that performs those permitted changes.

Guard checks run on the synchronous resolution thread. They are not cross-thread
locks. Do not edit active source tokens from worker threads or continue mutations
after a callback returns. Separate resolutions may share immutable tokens.
Synchronize writes to shared tokens outside resolution.

## Serialized stage ARG instructions

In non-inline mode, stage ARG resolution reparses serialized ARG syntax into a
detached instruction. The choice of declaration resolvers depends on the
instruction type and serialized declaration boundaries.

For an ordinary `ArgInstruction`, resolution uses the original declaration
resolvers when each serialized declaration corresponds to one parsed declaration.
If a default serializer merges or splits declarations, resolution uses the parsed
declarations instead.

In this non-inline path, resolution consumes each declaration's effective name
before looking up its override or resolving its default. If a callback later
changes external state used by a name serializer, the declaration's binding keeps
the consumed name. Later declarations still observe those changes.

For an `ArgInstruction` subclass, resolution uses the complete serialized
instruction as the source of truth in non-inline mode. It does not invoke the
original declaration resolvers, even if the subclass delegates serialization to
its base class.

To retain custom default-token resolvers in both modes, use an ordinary
`ArgInstruction` with custom default tokens. If you need to evaluate the original
tokens of an instruction subclass, use inline resolution.

These rules apply to stage ARG instructions. Global ARGs are evaluated directly
from their declaration tokens.
