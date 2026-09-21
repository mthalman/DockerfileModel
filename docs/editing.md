# Collection editing

Use the model's `EditableList<T>` properties for syntax-aware collection edits.
There is no separate editor object. Collection operations validate the proposed
structural edit before modifying the model. A rejected operation leaves the
model unchanged; successful operations preserve unrelated text and objects.

This guide describes structural syntax editing, not a complete Dockerfile build
validator. Collection edits can intentionally change build semantics, including
stage order and reference bindings. Existing scalar property setters are
unchanged and do not acquire the collection editing guarantees.

## Edit collections

Import `Valleysoft.DockerfileModel` and `System.Linq`. Token examples also
require `Valleysoft.DockerfileModel.Tokens`.

```csharp
Dockerfile dockerfile = Dockerfile.Parse(
    "FROM alpine AS build\nCOPY src/ /app/\nRUN echo ready\n");
CopyInstruction copy = dockerfile.Items.OfType<CopyInstruction>().Single();
RunInstruction run = dockerfile.Items.OfType<RunInstruction>().Single();

copy.Sources.Add("generated/");
dockerfile.Items.Insert(dockerfile.Items.IndexOf(run), new WorkdirInstruction("/output"));
dockerfile.Items.Move(dockerfile.Items.IndexOf(copy), dockerfile.Items.Count - 1);
```

Collections remain live views over tokens. Reading either a semantic value view
or its token view after an edit reflects the same syntax. Each call to
`GetEnumerator()` captures the current element sequence. Later collection edits
do not change that enumerator's sequence. Referenced model objects are not cloned.

```csharp
copy.Sources[0] = "source/";
copy.SourceTokens[0] = new LiteralToken("replacement/");
run.Mounts.Add(Mount.Parse("type=cache,target=/root/.nuget"));
run.Mounts[0].Entries.Add(new MountEntry("sharing", "locked"));
```

Construct adopted instructions, mounts, and context-bearing operands with the owner's
escape character, including any commands nested inside adopted instructions.
Typed adoption does not retokenize supplied objects to change their parsing
context. Complete built-in heredoc pairs are context-independent and are exempt
from this matching requirement, as are context-independent trivia and punctuation.
See [Heredoc escape context](#heredoc-escape-context).

### Collection contract

`public class EditableList<T> : IList<T>, IReadOnlyList<T>` has no public
constructor. Obtain an owner-bound instance from a model property.
`IsReadOnly` is false, but a write can fail if the result would be invalid.

The public list members are:

```csharp
public T this[int index] { get; set; }
public int Count { get; }
public bool IsReadOnly { get; }
public void Add(T item);
public void Insert(int index, T item);
public bool Remove(T item, TriviaDisposition trivia = TriviaDisposition.Preserve);
public void RemoveAt(int index, TriviaDisposition trivia = TriviaDisposition.Preserve);
public void Clear(TriviaDisposition trivia = TriviaDisposition.Preserve);
public bool Contains(T item);
public int IndexOf(T item);
public void CopyTo(T[] array, int arrayIndex);
public IEnumerator<T> GetEnumerator();
```

`ICollection<T>.Remove(T)` and `Clear()`, plus `IList<T>.RemoveAt(int)`, are
implemented explicitly and forward to the public methods with
`TriviaDisposition.Preserve`. Calls such as `items.Clear()` work directly on
`EditableList<T>` through its optional parameter. The explicit non-generic
`IEnumerable.GetEnumerator` is also supported.

The additional operations are:

```csharp
public void Replace(int index, T item, TriviaDisposition trivia = TriviaDisposition.Preserve);
public void ReplaceItem(T item, T replacement, TriviaDisposition trivia = TriviaDisposition.Preserve);
public void Move(int oldIndex, int newIndex);
```

- Standard writes, including index assignment and calls through `IList<T>`, use
  `TriviaDisposition.Preserve`. `Discard` explicitly permits removal of
  incidental trivia in the selected region.
- `Insert` accepts `0` through `Count`. `Move` uses a final index from `0` through
  `Count - 1`. A move validates and applies the reorder atomically, retaining
  identity and owned trivia without removal's comment-promotion behavior.
- `ReplaceItem` requires a unique match. Use indexed `Replace` when values
  repeat. `Remove` removes the first match and returns false if no match exists.
- String values compare ordinally. Key/value projections compare ordinal keys
  and nullable values. Token and model-object views compare underlying identity.
- Semantic insertion copies values into the owner's syntax. Typed insertion
  adopts the supplied object after validation. A token already present elsewhere
  in the edited tree cannot be inserted again; use `Move` to relocate it. This is
  an owner-local check, not a guarantee of exclusive ownership across objects.
- A same-position move or same-object replacement is a validated no-op.
- `Clear` is atomic. It either leaves an empty collection or throws unchanged.
  Clearing required operands does not delete the instruction or leave it
  temporarily incomplete.

### Editable collection properties

Each property below is an `EditableList<T>` with the indicated element type.
Paired value/token views are synchronized.

| Owner | Property and element type |
| --- | --- |
| `Dockerfile` | `Items`: `DockerfileConstruct` |
| `FileTransferInstruction` (`COPY`, `ADD`) | `Sources`: `string`; `SourceTokens`: `LiteralToken`; `Heredocs`: `Heredoc` |
| `CopyInstruction`, `AddInstruction` | `Excludes`: `string`; `ExcludeFlagTokens`: `ExcludeFlag` |
| `RunInstruction` | `Mounts`: `Mount`; `Heredocs`: `Heredoc` |
| `EnvInstruction` | `Variables`: `IKeyValuePair`; `VariableTokens`: `KeyValueToken<Variable, LiteralToken>` |
| `LabelInstruction` | `Labels`: `IKeyValuePair`; `LabelTokens`: `KeyValueToken<LabelKeyToken, LiteralToken>` |
| `ArgInstruction` | `Args`: `IKeyValuePair`; `ArgTokens`: `ArgDeclaration` |
| `ExposeInstruction` | `Ports`: `string`; `PortTokens`: `LiteralToken` |
| `ExecFormCommand` | `Values`: `string`; `ValueTokens`: `LiteralToken` |
| `VolumeInstruction` | `Paths`: `string`; `PathTokens`: `LiteralToken` |
| `GenericInstruction` | `ArgLines`: `string` |
| `Instruction` | `Comments`: `string?`; `CommentTokens`: `CommentToken` |
| `Mount` | `Entries`: `MountEntry` |

`Sources` excludes the destination and heredoc definitions. Clearing ordinary
sources is valid only when another source kind still makes the instruction
valid. JSON-form lists retain their form and repair their own commas and
brackets. Mount lists can be empty; a mount's entry list cannot.

For JSON-form exec values, sources, and volume paths, string views accept
semantic values, not JSON-encoded syntax. New values containing double quotes,
backslashes, or control characters U+0000–U+001F currently throw because these
views do not implement JSON escaping. Token views accept valid encoded JSON
string representations, including JSON escapes and Dockerfile continuations
with intervening physical comment lines. They validate the prospective
double-quoted representation without changing incoming tokens on rejection.
These checks apply to insertions and non-self replacements under either trivia
policy; they do not rewrite existing operands or change shell-form editing.
Existing operand grammar limits still apply: JSON `\"` escapes are not supported
with a backtick Dockerfile escape directive.

Adding a second variable to a legacy `ENV name value` converts the affected
assignment region to assignment syntax without changing the existing value's
meaning. `ARG` distinguishes a missing value from an explicit empty value.
Generic argument lines exclude physical CR/LF; collection operations manage
continuations and validate the known instruction grammar.

`TokenList<TToken>` derives from `EditableList<TToken>` and retains its existing
read helpers. `TokenBuilder.Tokens` remains a low-level construction list, not a
structural-editing collection.

## Choose a trivia policy

```csharp
copy.Sources.RemoveAt(0, TriviaDisposition.Discard);
dockerfile.Items.Remove(run, TriviaDisposition.Preserve);
dockerfile.Items.Clear(TriviaDisposition.Discard);
```

`TriviaDisposition` has two values: `Preserve = 0` and `Discard = 1`.
Preserve keeps comments, blank lines, and continuations at legal boundaries.
It throws if the selected syntax cannot retain that trivia safely. Discard
affects only incidental trivia inside the edit region, not unrelated constructs.
Explicitly selected comments, whitespace constructs, and heredoc bodies are
payload: removing them does not preserve them merely because they resemble
trivia.

Standalone comments and blank lines are independent `Items`; removing or moving
an instruction does not implicitly consume them. Removing an instruction with
embedded comments under Preserve can promote those comments into new standalone
items. Consequently, removal can change `Items.Count` by more than minus one.
Replacement can also promote embedded comments after the replacement item,
which remains at the selected index.
After a successful `Items.Clear()`, `Count` is zero. Preserve rejects the clear
if retaining embedded trivia or the document's byte-order mark (BOM) would
require leftover items. Use `Clear(Discard)` for an explicit complete removal.
Other document edits preserve an existing BOM.

Preserved trivia is reused rather than cloned. A removed instruction can still
reference a comment promoted into the document, and a replaced heredoc can still
reference closing-line trivia used by its replacement. Mutating those shared
tokens through the outgoing object can change the surviving model. Removed or
replaced objects are not guaranteed to be independently mutable or reusable;
ownership checks do not detect sharing with other trees.

Insertion repairs only required seams and uses local newline style. Existing
line endings and EOF termination remain unchanged where no new boundary is
needed. Header edits must preserve valid directive placement and escape context;
they do not silently reinterpret existing instructions with another escape
character. See [Parser directives](parser-directives.md).

## Edit mounts and heredoc collections

### Mount entries

```csharp
public sealed class MountEntry
{
    public MountEntry(string key, string? value = null,
        char escapeChar = Dockerfile.DefaultEscapeChar);
    public string Key { get; }
    public string? Value { get; }
    public bool IsBareKeyword { get; }
    public KeywordToken KeyToken { get; }
    public KeyValueToken<KeywordToken, LiteralToken>? KeyValueToken { get; }
}
```

`Mount.Entries` retains duplicate keys and their order. A null value denotes a
bare keyword; an empty string denotes an explicit empty assignment. Reads from
a mount are token-backed. Replace an entry to change its key, value, or form.
For example, replace a selected type entry with `new MountEntry("type", "cache")`.
Use an indexed edit to select one occurrence when keys repeat.

Inserted or replacement entry roots and their context-bearing descendants must
match the mount's escape character, including bare keywords. Matching serialized
values alone is not sufficient. Supply the mount's context when constructing
entries:

```csharp
Mount mount = Mount.Parse("target=/cache", escapeChar: '`');
mount.Entries.Add(new MountEntry("readonly", escapeChar: '`'));
```

Mount editing accepts bare entries, empty assignments, duplicates, simple quoted
values, and variable-bearing values. It rejects representations whose builder
decoding introduces comma or double-quote ambiguities in CSV field boundaries.
The existing mount parser cannot consume quoted values containing spaces;
`MountEntry` construction rejects them rather than extending that grammar.

### Paired heredoc construction and replacement

```csharp
public enum HeredocQuoteKind
{
    Unquoted = 0,
    SingleQuoted = 1,
    DoubleQuoted = 2
}
```

The existing `Heredoc` type gains:

```csharp
public Heredoc(string name, string rawContent,
    HeredocQuoteKind quote = HeredocQuoteKind.Unquoted,
    bool chomp = false, char escapeChar = Dockerfile.DefaultEscapeChar);
public string RawContent { get; }
```

```csharp
run.Heredocs.Add(new Heredoc("SCRIPT", "echo ready\n"));
run.Heredocs[0] = new Heredoc(
    "BUILD_SCRIPT", "echo ready\n", HeredocQuoteKind.SingleQuoted);
```

Heredoc collection edits insert, replace, move, or remove the opening marker and
body together. To change the name, content, quoting, or chomp setting, construct
a complete replacement and assign it through `Heredocs`. Objects read from the
collection remain stable while their underlying pair remains present.
Existing `Name`, `Content`, `Chomp`, and `Expand` reads remain available;
`Content` applies chomp, while `RawContent` retains tabs and exact body bytes.
Body content must be empty or end in LF/CRLF before its closing delimiter. The
library does not silently append bytes to supplied content.

Construction and collection edits validate pairing, delimiter collisions, and
quote/chomp compatibility. Repeated delimiter names are allowed when pairs
remain valid. Construction does not add a final newline; collection insertion
adds required local boundaries.

`Expand` is Dockerfile parser metadata, following the
[pinned BuildKit heredoc parser](https://github.com/moby/buildkit/blob/b7c2f61e845cbf42d9be4e2a675a67b076c1cec9/frontend/dockerfile/parser/parser.go#L415-L458).
Constructed `Unquoted` definitions report `Expand == true`, including names such
as `END WORD` that construction backslash-escapes. `SingleQuoted` and
`DoubleQuoted` definitions report false. This metadata does not guarantee what
the selected shell expands during execution; Bash, for example, can interpret
an escaped delimiter differently.

### Heredoc escape context

Complete built-in heredoc pairs use fixed-backslash delimiter quoting and raw
body text. They can be adopted by backslash- or backtick-configured instructions
without matching the pair's construction context or retokenizing its contents.
The constructor still accepts and validates `escapeChar` for compatibility and
context bookkeeping; it does not select the delimiter grammar or impose a
receiving-owner precondition. The receiving collection supplies header and
closing-line boundaries using its own context.

### First RUN heredoc placement

Adding the first heredoc to a shell-form RUN places its marker textually before
the existing command, after flags and preceding header trivia. For example,
adding `SCRIPT` to `RUN cat` produces the header `RUN <<SCRIPT cat`. This rule
does not depend on whether the command contains `#`, and it does not relocate
already-parsed markers.

The library does not interpret the selected shell or choose which command in a
pipeline receives heredoc input. Prefix placement is not executable in every
shell command form. For a different redirection position or compound-command
syntax, construct a complete instruction with explicit redirection placement
and replace the original through `Items`.

### Representation limits

Adding a heredoc does not implicitly convert JSON file-transfer or exec RUN
syntax to shell form. Removing the last heredoc from a marker-only RUN cannot
leave an empty command. To make that transition, construct a complete
replacement instruction and replace the original through `Items`. Shell
redirections with inseparable operators or descriptor prefixes can be rejected
rather than guessed. Existing marker/body enumerations remain inspection APIs,
not independently editable halves.

Legacy standalone file-transfer parsing can represent regular-source gaps
before heredoc markers as opaque `StringToken` nodes. Heredoc collection edits
reject those ambiguous mappings. For mixed ordinary/heredoc sources, use
`Dockerfile.TryParse` to obtain the diagnostic parser's literal source nodes,
then inspect its diagnostics before editing.

## Edit comments and document constructs

`Instruction.Comments` and `CommentTokens` enumerate comments in depth-first
token order. Null or empty comment text represents an empty comment marker;
embedded CR/LF is invalid. Use the collection's indexed operations
to select a position among comments. Adding the first comment uses a legal
instruction continuation boundary.

To position a comment relative to an operand or an existing comment, use these
`Instruction` methods:

```csharp
public void InsertCommentBefore(Token anchor, string text);
public void InsertCommentAfter(Token anchor, string text);
```

For example, insert a comment before the second port:

```csharp
ExposeInstruction instruction = ExposeInstruction.Parse("EXPOSE 80 443");
instruction.InsertCommentBefore(instruction.PortTokens[1], "HTTPS");
```

The anchor must be a direct semantic child token or an existing comment from
the same instruction, selected by reference identity. Whitespace and
continuation tokens are not valid anchors. Supply comment text without the
leading `#`; an empty string creates an empty comment, but null and embedded
CR/LF are invalid for these methods.

Both methods require a legal instruction continuation boundary.
`InsertCommentAfter` cannot simply append a comment after the final operand.
If the insertion cannot preserve valid instruction boundaries, it throws
without changing the model. Required continuation syntax uses the instruction's
escape character and local newline style.

The existing `ICommentable` contract is unchanged:
`IList<string?> Comments` and `IEnumerable<CommentToken> CommentTokens`.
`Instruction` implements those properties explicitly, so external implementers
do not need to construct `EditableList` instances.

Use `Items` for standalone comments, whitespace, parser directives, and opaque
unknown or malformed constructs. Replace immutable `CheckDirectiveOptions` by
constructing new options and a `CheckDirective`, then replacing the item.
Unknown or malformed syntax is opaque: move, replace, or remove the whole
construct rather than editing guessed operands.

## Resume a builder after collection edits

`DockerfileBuilder` shares its document and remains a raw construction API.
Its automatic newline follows the newly appended construct; it does not repair
the boundary before that construct. Before resuming builder appends after an
edit, ensure the preceding construct is complete and correctly terminated.
When the edit leaves a complete instruction without its final newline, add
that boundary explicitly:

```csharp
DockerfileBuilder builder = new() { DefaultNewLine = "\n" };
builder.FromInstruction("alpine");
builder.Dockerfile.Items.Add(RunInstruction.Parse("RUN echo one"));
builder.NewLine().RunInstruction("echo two");
```

The result is:

```dockerfile
FROM alpine
RUN echo one
RUN echo two
```

`NewLine()` supplies a boundary; it does not complete an unfinished instruction
or heredoc.

## Validation boundaries

Scalar setters and direct token changes can make the model disagree with how
its serialized text would parse. Collection edits do not repair those earlier
inconsistencies, and a successful edit does not prove that the entire model
matches its serialized text.

Collection edits validate structural syntax, not Dockerfile build behavior.
They do not automatically update stage references or preserve their bindings.

## Supported token implementations

Structural editing rejects unsupported effective serialization overrides and
custom implementations of `IQuotableToken`, including interface
reimplementations on subclasses of built-in tokens. Subclasses that inherit
supported built-in serialization and quoting implementations remain eligible.
This boundary applies to existing and incoming tokens; extending a model type
does not automatically make custom serialization or quote accessors safe for
structural editing.

## Errors, identity, and compatibility

| Condition | Exception |
| --- | --- |
| Null required input | `ArgumentNullException` |
| Invalid index or enum value | `ArgumentOutOfRangeException` |
| Invalid argument, foreign anchor, or newly supplied semantic JSON value requiring unsupported escaping | `ArgumentException` |
| Invalid cardinality, cyclic token tree, ambiguous owner-local token ownership, incompatible context, invalid incoming JSON token syntax, unpreservable trivia, or unsupported token implementation/collection representation | `InvalidOperationException` |

Individual operations are atomic; a sequence of separate public calls is not a
transaction. No public transaction, clone-and-commit, or generic token-splice
API is provided. Concurrent mutation and catastrophic runtime failures are
outside the atomicity guarantee.

Collection operations work on standalone instructions and commands using their
retained parsing context. No document argument is needed for a local collection
edit.

`SourceSpan` remains original-input provenance, not an edited coordinate.
Previously returned analysis snapshots do not change; analyze again after
editing. Stage projections are views of underlying models, not separate edit
owners. Existing builders remain construction APIs.

Changing collection getter types to `EditableList<T>` is a binary API break,
even though ordinary assignments to `IList<T>` and `IReadOnlyList<T>` remain
source-compatible. Recompile dependent assemblies and review code that assumed
structural writes were unsupported or that `Items` accepted incomplete syntax.
See [Collection editing migration](structural-editing-migration.md).
