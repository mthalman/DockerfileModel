# Migrate to collection editing

This upcoming breaking change replaces collection getter types with
`EditableList<T>` and makes their writes syntax-aware. See the
[editing reference](editing.md) for the complete API and trivia contracts.

## Understand validation boundaries

This change adds collection editing only. Existing scalar property setters
remain unchanged; their behavior does not acquire collection validation or
atomicity guarantees. Collection edits do not automatically update stage
references or preserve their bindings.

Scalar setters and direct token changes can make the model disagree with how
its serialized text would parse. Collection edits do not repair those earlier
inconsistencies, and a successful edit does not prove that the entire model
matches its serialized text.

## Recompile consumers

Recompile assemblies that reference the changed collection getters, even when
their source code still compiles unchanged:

```csharp
IList<DockerfileConstruct> items = dockerfile.Items;
IReadOnlyList<string> sources = copy.Sources;
```

The affected properties are `Dockerfile.Items`; instruction value and token
collections for sources, excludes, mounts, variables, labels, ARG declarations,
ports, exec values, volume paths, generic argument lines, comments, and heredocs.
`Mount.Entries` adds an editable view of mount fields.
`TokenList<TToken>` now derives from `EditableList<TToken>`. The protected
`AggregateToken.GetComments()` return type also changes to
`EditableList<string?>`; update derived code and recompile.

The `ICommentable` interface itself keeps its existing `IList<string?> Comments`
and `IEnumerable<CommentToken> CommentTokens` signatures. External implementers
do not need a library-owned editable collection.

## Update removal method groups

`Remove`, `RemoveAt`, and `Clear` each expose one public method with an optional
`TriviaDisposition` parameter defaulting to `Preserve`. Ordinary calls such as
`items.Clear()` are unchanged, but method-group conversion does not omit optional
parameters. Replace assignments such as `Action clear = dockerfile.Items.Clear`
with a lambda, or bind through a standard collection interface:

```csharp
Action clear = () => dockerfile.Items.Clear();
Action clearViaInterface = ((ICollection<DockerfileConstruct>)dockerfile.Items).Clear;
```

The explicit `ICollection<T>.Remove(T)`, `ICollection<T>.Clear()`, and
`IList<T>.RemoveAt(int)` implementations retain their standard signatures and
preserve trivia.

## Replace unsupported-write workarounds

Use the owner's collection instead of reconstructing an entire instruction:

```csharp
copy.Sources.Add("generated/");
copy.Sources.Move(0, 1);
copy.SourceTokens.Replace(0, new LiteralToken("source/"));
```

Standard list writes and index assignment preserve trivia by default. They can
now throw before mutation when syntax, required cardinality, ownership, or
trivia cannot be preserved. Code that treated `IsReadOnly == false` as permission
to create an incomplete intermediate state must assemble a complete replacement
first. Clearing a required operand list no longer means leaving an invalid
instruction to repair in a later call.

Collection views remain live. Each call to `GetEnumerator()` captures the
current element sequence. Later collection edits do not change that enumerator's
sequence. Referenced model objects are not cloned.

## Distinguish JSON values from JSON syntax

For JSON-form exec arguments, file-transfer sources, and volume paths, string
views supply semantic values. They currently reject new values containing
double quotes, backslashes, or U+0000–U+001F rather than encoding them. Use the
corresponding token view to supply valid encoded JSON string syntax instead.
Token adoption preserves valid JSON escapes and Dockerfile continuations,
including skipped physical comment lines, within the existing operand grammar.
Invalid representations throw before
changing the owner or incoming token, regardless of trivia policy. Existing
operands are not rewritten.

## Make destructive intent explicit

```csharp
copy.Sources.RemoveAt(0, TriviaDisposition.Discard);
dockerfile.Items.Clear(TriviaDisposition.Discard);
```

Removing an instruction with embedded comments under Preserve can promote
comments to standalone items. Do not assume removal always decreases
`Items.Count` by exactly one. Replacement can also promote comments after the
replacement item, which remains at the selected index.
`Clear()` either empties the collection or fails unchanged, including when
preserving embedded trivia or a byte-order mark (BOM)
would require leftover items. Successful `Items.Clear()` leaves `Count == 0`;
`Clear(Discard)` explicitly selects full removal, including any BOM.

Typed-token insertion adopts the supplied token and rejects repeated identities
within the edited owner's tree. Use `Move` to relocate an existing item.
Semantic value insertion copies data into the owner's grammar instead of
adopting an arbitrary caller token tree.

Preserved trivia can remain shared with outgoing removed or replaced objects.
For example, an outgoing instruction can still reference a promoted comment,
or an outgoing heredoc can reference closing-line trivia used by its replacement.
Mutating these shared tokens through the outgoing object changes the surviving
model. Do not assume independently mutable or reusable outgoing objects:
ownership validation is local to the edited tree, not global across trees.

## Check adopted token implementations and context

Structural edits reject unsupported effective serialization overrides and custom
`IQuotableToken` implementations, including interface reimplementations on
built-in token subclasses. Subclasses inheriting supported built-in
serialization and quoting implementations remain eligible. Use supported token
implementations for both existing owners and incoming objects.

Construct adopted instructions, mounts, and context-bearing operands with the
owner's escape character, including nested commands. A `Mount.Entries` insertion
or replacement checks the entry root and its context-bearing descendants against
the mount's context. Adoption does not retokenize objects to change that context.
Complete built-in heredoc pairs are exempt: their delimiters use fixed-backslash
grammar and their bodies remain raw, so they can be adopted across backslash and
backtick owners. The heredoc constructor's `escapeChar` remains accepted for
compatibility and context bookkeeping, not as an adoption precondition.

## Replace complete heredoc definitions

Construct a complete `Heredoc` and insert or replace it through `Heredocs`.
Collection operations keep opening markers and bodies paired; do not mutate
markers and bodies independently.

```csharp
run.Heredocs[0] = new Heredoc(
    "BUILD_SCRIPT", "echo ready\n", HeredocQuoteKind.SingleQuoted);
```

Use constructor arguments to select the replacement's name, raw content,
quoting, and chomp behavior. For a transition that a collection cannot represent,
such as removing the last definition from a marker-only RUN, prepare a complete
replacement instruction and replace it through `Items`.

The first heredoc added to a shell-form RUN is placed textually before its
existing command, after flags and preceding header trivia, regardless of `#`
characters. Existing marker positions are not changed by this first-insertion
rule. This is not a guarantee of shell executability or a particular pipeline
input recipient. For another attachment point or compound-command form,
construct and replace a complete instruction with explicit redirection placement.

`Expand` reports Dockerfile parser metadata, not selected-shell execution.
Constructed `Unquoted` definitions report true even when names require
backslash escapes; `SingleQuoted` and `DoubleQuoted` report false. Shell behavior
can differ, including in Bash.

## Resume raw builder construction explicitly

When a builder shares the edited document, its automatic newline follows the
next appended construct; it does not repair the preceding boundary. Before
resuming appends, ensure the preceding construct is complete and terminated.
For a complete instruction left without a final newline, use
`builder.NewLine().RunInstruction("echo next")`. This preserves the builder's
existing raw-construction behavior. See the
[mixed-use example](editing.md#resume-a-builder-after-collection-edits).

## Refresh derived information

Call `Analyze()` again after editing. Old analysis values remain snapshots, and
`SourceSpan` continues to describe the original input. Do not interpret it as
the construct's new location.
