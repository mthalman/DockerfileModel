### Adopt syntax-aware editable model collections

#### Previous behavior

Model collection getters exposed `IList<T>` or `IReadOnlyList<T>`. Instruction
collections and `TokenList<TToken>` generally rejected structural writes even
when `IsReadOnly` was false; some allowed index replacement. `Dockerfile.Items`
was an ordinary mutable list that could assemble incomplete syntax without
structural validation. Callers reconstructed tokens or entire instructions for
many collection edits.

#### New behavior

`Dockerfile.Items` and editable instruction collections return
`EditableList<T>`, implementing both `IList<T>` and `IReadOnlyList<T>`. Standard
writes preserve trivia and validate proposed structural edits before mutation.
Optional trivia policies, replacement methods, and indexed moves provide
structural edits without exposing token splices. `TokenList<TToken>` derives
from `EditableList<TToken>`.

JSON-form string views accept semantic values and currently reject new values
that require JSON escaping (double quotes, backslashes, and U+0000–U+001F).
Corresponding token views accept valid encoded JSON string representations,
preserving JSON escapes and Dockerfile continuations within the existing
operand grammar. Both trivia policies
reject invalid newcomers before mutation.

Invalid required-list clearing, repeated token identities within the edited
tree, incompatible parsing context, and unpreservable trivia throw without
partial mutation. Removal and replacement can promote embedded comments into
standalone items. `Clear` either empties the selected collection or throws
unchanged. See [trivia policies and shared ownership](../docs/editing.md#choose-a-trivia-policy)
for preservation rules, including sharing with removed or replaced objects.

`Mount.Entries` supports field-level list edits. Paired `Heredocs` collections
insert, replace, move, and remove complete definitions created with the
`Heredoc` constructor. See the [mount and heredoc reference](../docs/editing.md#edit-mounts-and-heredoc-collections)
for escape-context requirements, representation limits, and shell caveats.

Collection views remain live. Each call to `GetEnumerator()` captures the
current element sequence. Later collection edits do not change that enumerator's
sequence. Referenced model objects are not cloned.

Existing scalar setters remain unchanged. Collection edits can change stage
bindings and do not repair inconsistencies caused by earlier scalar or low-level
changes. See [validation boundaries](../docs/editing.md#validation-boundaries)
and [supported token implementations](../docs/editing.md#supported-token-implementations)
before editing models with custom tokens or prior low-level mutations.

#### Type of breaking change

This is a binary API compatibility change: collection property getter return
types and the protected `AggregateToken.GetComments` return type change.
Existing interface assignments generally remain source-compatible, but
dependent assemblies must be recompiled. It is also a behavioral change for
collection mutation, validation, trivia preservation, and ownership.
The public `Remove`, `RemoveAt`, and `Clear` methods take optional trivia-policy
parameters. Ordinary calls remain source-compatible, but method groups bound
through the concrete collection type must include the policy parameter or use
a lambda or standard collection interface.

#### Reason for change

Structural writes need to manage instruction-specific separators, required
operands, comments, and related tokens together. A dedicated collection exposes
these operations consistently while preventing partial or silently destructive
edits.

#### Recommended action

Recompile all dependent assemblies. Replace instruction-reconstruction
workarounds with the owner's editable collection, for example:

```csharp
copy.Sources.Add("generated/");
copy.Sources[0] = "source/";
dockerfile.Items.Move(dockerfile.Items.IndexOf(copy), dockerfile.Items.Count - 1);
```

Do not clear required operands as an intermediate step; prepare a valid
replacement instead. Select `TriviaDisposition.Discard` when removal should
discard incidental trivia. Use `Move` rather than inserting the same token
twice.

To bind `Clear` to an `Action`, use
`Action clear = () => dockerfile.Items.Clear()` or bind through
`ICollection<DockerfileConstruct>`.

Follow [the collection editing migration](../docs/migrations/structural-editing.md)
to check adopted objects, replace heredocs, resume builder appends, and refresh
analysis. The [editing reference](../docs/editing.md) contains the complete
contracts and examples.

#### Affected APIs

- `Dockerfile.Items`.
- `FileTransferInstruction.Sources`, `SourceTokens`, and `Heredocs`;
  `CopyInstruction` and `AddInstruction` exclude value/token collections;
  `RunInstruction.Mounts` and `Heredocs`.
- `EnvInstruction.Variables` and `VariableTokens`; `LabelInstruction.Labels`
  and `LabelTokens`; `ArgInstruction.Args` and `ArgTokens`.
- `ExposeInstruction.Ports` and `PortTokens`; `ExecFormCommand.Values` and
  `ValueTokens`; `VolumeInstruction.Paths` and `PathTokens`;
  `GenericInstruction.ArgLines`.
- `Instruction.Comments` and `CommentTokens`, `TokenList<TToken>`, and
  protected `AggregateToken.GetComments`.
- New `EditableList<T>`, `TriviaDisposition`, `MountEntry`, and
  `HeredocQuoteKind` types; `Mount.Entries`; and `Heredoc` construction and
  `RawContent` inspection for complete-definition collection edits.

The existing `ICommentable` interface signatures and existing scalar setters
remain available. External `ICommentable` implementers do not need to construct
an `EditableList`.
