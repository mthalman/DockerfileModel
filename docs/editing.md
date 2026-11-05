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

`VOLUME` paths recognize variable references and supported variable modifiers in
both shell and JSON form, including paths created or updated through the editable
views. Typed `LiteralToken` paths and modifier values retain their
variable-recognition policy, including derived `LiteralToken` instances.
A modifier value constructed with
`canContainVariables: false` remains literal after JSON decoding, including
any dollar signs, quotes, or braces in its contents.
Derived literals that inherit the default rendering preserve these policies
in nested modifier values. A modifier's overridden `ResolveVariables()` is
called only when its branch is selected, even if its rendered text is empty.
Its result is JSON-escaped without recognizing new variable references.
Nested aggregate overrides, including literal and variable-reference overrides,
also retain virtual dispatch,
both directly in a path and inside selected modifier values, even when their
rendered text is empty. Each occurrence is evaluated
independently, including occurrences that share the same token instance.
Aggregate containers that inherit the default rendering and resolution also
preserve their descendants' overrides, variable policies, and quote protection.
References derived from `VariableRefToken` that inherit its rendering also retain
their nested modifier policies and overrides. Custom occurrences whose rendered
text consists only of folded continuations remain evaluable and retain their
physical replacement boundaries.
Custom occurrences sharing a decoded JSON character replace that character
together, preserving the order of their results and evaluating each occurrence.
An empty custom occurrence physically inside a JSON escape inserts its result
before the decoded character without splitting the escape or dropping the character.
Opaque literals and zero-width custom occurrences interrupt variable names and
reference syntax rather than being consumed as part of a reference.
Decoded single quotes and escapes still protect these references from evaluation.
Sibling callbacks run from right to left, matching ordinary aggregate resolution.
Changes a right-hand callback makes to the variable dictionary are visible to
left-hand references.
Custom dictionary lookups and comparers may also change resolution options,
including enabling inline publication during the first lookup.
Lookup changes to `RemoveEscapeCharacters` affect the reference performing that
lookup as well as references evaluated later.
Custom rendering still determines decoded modifier contents.
Changes to `RemoveEscapeCharacters` also affect subsequent references and source
formatting in that evaluation order.
Each path occurrence uses the value of `UpdateInline` after its evaluation.
A callback changing that option does not retroactively change whether
already evaluated path occurrences are replaced.
Before a later callback runs, eligible source-backed reference-token children
are replaced in their live slots. Callbacks can observe those publications but
cannot edit their token values or structure.
The same rule applies to reference children in selected modifiers, including
children of default aggregate containers. These children are published before
the next sibling callback runs, even if that callback prevents replacement of
the containing reference. Retained JSON modifier substitutions are encoded
non-variable literals, so later edits cannot reactivate their contents.
Source publication locations are tracked independently of live-reference binding.
Nested updates still reach the exact source occurrence when quoted or
JSON-escaped modifier text requires evaluation through a decoded token tree.
This includes custom references and required-variable failures without callbacks.
Inline removal updates the captured slots of shared modifiers. Later occurrences
skip removed children instead of reading a neighboring token or a stale index.
This tracking also covers removals through custom aggregates that delegate to
the ordinary resolver. Tracking subscriptions end when resolution returns or throws.
Whole-path inline publication during synchronous reentrant resolution also
updates active source locations, including paths whose decoded references split
an original string token or span multiple source tokens. Active calls share
occurrence identities rather than treating a child index as a permanent source
identity. Grouped source projections follow later publications, including those
started after an earlier reentrant call returns. Reused substitutions retain
their semantic values rather than expanding or JSON-escaping their encoded spelling again.
This includes custom literal and aggregate results as well as variable references.
Custom occurrences retain their source anchors even when their physical token
boundaries split a decoded JSON character.
Shared modifiers reuse the semantic values of earlier inline substitutions in
the same call, rather than treating their encoded JSON spelling as expanded text.
Published substitutions remain visible even when quoted or escaped syntax prevents
live binding to the original reference.
This also applies when a custom aggregate delegates to `base.ResolveVariables()`.
Semantic reads do not materialize the encoded text of a deferred substitution.
Nested JSON substitutions defer text materialization until their contents are
read. Reading `Tokens`, `Value`, or serialized text materializes ordinary editable
children and releases the deferred expansion tree. This avoids repeatedly
rendering growing modifier subtrees during inline publication.
Modifier selection reads the environment once per reference evaluation.
JSON modifier operands are captured before custom lookups, so reentrant inline
publication cannot detach an operand already selected by the outer evaluation.
Selected branches and required-variable error names do not change if a callback
updates the variable dictionary during their evaluation.
Resolution context passes the caller's original `ResolutionOptions` instance
without cloning it. Selected custom modifier overrides receive the caller's `UpdateInline`
and `RemoveEscapeCharacters` settings; the overrides manage their own side effects.
Required-variable errors distinguish a custom modifier returning `null`
(`'<empty>'`) from an aggregate modifier resolving to an empty string (`''`).
`ResolveVariables()` preserves the original model unless
`ResolutionOptions.UpdateInline` is `true`; inline resolution replaces expanded
references but retains untouched child tokens, including empty primitive tokens.
Empty primitive siblings retain their order around zero-width substitutions.
Repeated occurrences of the same empty token remain distinct, including when
only one occurrence is eligible for inline replacement.
Zero-width references inside nonempty nested literals are replaced only once.
Unrelated empty literals retain their identity, including those inside an
ancestor whose expanded references are replaced.
Inline expansion also retains untouched children of nonempty nested literals.
Non-inline resolution does not reconstruct the source token tree.
Inline reconstruction skips source ranges that cannot contain a replacement,
including unrelated sibling subtrees beside a zero-width substitution.
Untouched subtrees are retained without traversing them again during publication.
When every substitution is eligible for inline publication, returned text and
publication reuse the same reconstructed tokens.
JSON-form paths decode JSON escapes before recognizing variables and JSON-escape
substituted values. Unchanged JSON spelling and Dockerfile continuations are
retained. Decoded single quotes protect their contents from expansion; quote
spelling is retained rather than shell-unquoted. A decoded Dockerfile escape
character protects the following dollar sign from expansion. These rules also
apply inside modifier values, which may contain decoded newline characters.
Protected malformed reference text remains literal; active malformed references
are diagnosed during resolution rather than during JSON string tokenization.
Unterminated decoded quotes fail before substitutions are committed to that path.
Resolution parse-error positions refer to the complete physical path literal,
including its JSON quotes, escapes, and Dockerfile continuations.
Pattern modifiers (`#`, `##`, `%`, `%%`, `/`, and `//`) remain unsupported;
their unresolved references retain the original JSON spelling and token identity.
Unmatched Unicode
surrogates decode to U+FFFD, matching BuildKit's JSON decoder, while untouched
escape spelling remains intact. Legacy parser-accepted paths that are not valid
JSON strings retain token-based resolution.
JSON path resolution renders built-in literal and variable-reference trees
iteratively. This does not change the recursion limits of general token
`ToString()` calls.
Live-reference binding checks decoded spans using precomputed character counts,
without rescanning enclosing modifier text for every nested reference.

`RemoveEscapeCharacters` affects returned JSON text, including single-quoted
contents and trailing escape characters, without rewriting untouched path
contents, even when `UpdateInline` is `true`. Inline substitutions stay
literal across later primitive-token edits. Assigning the path's `Value` or
updating it through `Paths` reparses its contents and makes new references
eligible for resolution again.
Assignments through `Value` or `Paths` replace child tokens and are therefore
structural edits. Perform them before or after resolution, not from a callback,
even when assigning equivalent text.
Nesting a previously resolved literal under another path or modifier preserves its
substitution protection; flattening it during inline publication preserves that
protection too.
Successful path replacement also releases tracking references to detached
substitutions; rejected edits retain the existing tracking state.
Individual child edits maintain substitution occurrence counts, preserving
protection for retained aliases without rescanning the path after every
incremental publication.
If a JSON escape spans untouched text and an inline substitution, its decoded
character remains literal and cannot introduce variable, quote, or escape syntax.
Collection adoption and scalar token replacement bind direct token resolution
to the receiving owner's context. Instruction resolution uses that instruction's
context independently of the token's latest binding, so sharing a token with a
non-VOLUME instruction does not change the original VOLUME's resolution policy.
Synchronous reentrant calls retain each active literal's context, including calls
that re-enter an outer literal through another literal's callback.
Document `ARG` evaluation uses the `ARG` context for defaults shared with `VOLUME`.
Direct variable-reference resolution uses the modifier's non-JSON context even
when the modifier literal is also shared with a JSON `VOLUME` path.
The token and its contents remain shared mutable objects.

Adding a second variable to a legacy `ENV name value` converts the affected
assignment region to assignment syntax without changing the existing value's
meaning. `ARG` distinguishes a missing value from an explicit empty value.
Generic argument lines exclude physical CR/LF; collection operations manage
continuations and validate the known instruction grammar.

`TokenList<TToken>` derives from `EditableList<TToken>` and retains its existing
read helpers. `TokenBuilder.Tokens` remains a low-level construction list, not a
structural-editing collection.

## Resolution callbacks

Variable resolution keeps active source tokens unchanged by callbacks. This contract
applies to document, instruction, aggregate, literal, and variable-reference
resolution, in both inline modes. A document operation protects the document's
construct membership and the token trees of its constructs, not only the
instruction currently being expanded.

Resolver overrides and custom dictionary lookups or comparers may change
variable dictionaries and `ResolutionOptions`. They may also perform synchronous
reentrant resolution. They must not assign source token values or quotes,
including `StringToken.Value`, reference names, modifier contents, or
`QuoteChar`. Assignments to source properties are rejected even when assigning
the current value.

Callbacks also must not add, remove, reorder, or replace children of an active source,
or change an active document's construct membership. Violations throw
`InvalidOperationException`. Guarded setters and collection views reject the
edit before publication. A subclass can still access its protected raw
`List<Token>`; edits through that list are detected after the callback returns
or throws, including edits that restore the original sequence. Detection does
not roll back those raw edits. Custom quote implementations that bypass native
setters, including quotable primitive subclasses, are also checked at callback
boundaries without rollback. An exception
does not undo eligible inline substitutions already published by the library.

`AggregateToken.Tokens` returns a cached, live guarded view, not a `List<Token>`.
Its declared `IEnumerable<Token>` signature is unchanged. For low-level child
editing outside resolution, cast to `IList<Token>` rather than `List<Token>`:

```csharp
IList<Token> children = (IList<Token>)literal.Tokens;
children.Add(new StringToken("/suffix"));
```

These low-level writes do not validate grammar or preserve separators; prefer
the owner's syntax-aware editable collections. The guarded view prevents native
token lists from escaping, so ordinary callbacks do not require repeated scans
of native source trees. Protected lists in consumer-defined subclasses still
require callback-boundary checks. The cost of those checks grows with the number
of such containers and callbacks.
Removing or replacing children through the guarded view also releases their
JSON-substitution tracking; removed values are not retained until another resolution.

The library may replace or remove expanded references as part of
`UpdateInline`; these library-owned publications are not callback edits.
Callbacks may edit unrelated trees. A completed reentrant operation releases
protection of its independent source before returning to its caller.

Do not access a shared source concurrently from another thread during resolution.
The mutable model and its synchronous guard do not provide cross-thread locking.

Custom serializers and quote getters must be side-effect-free and repeatable.
Consumer overrides must obey the callback contract. The library enforces it
when dispatching overrides; a direct call that stays entirely inside consumer
code without entering library resolution remains that implementation's
responsibility.

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
if retaining trivia would require leftover items. Use `Clear(Discard)` for an
explicit complete removal.

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

## Choose line endings for new content

Parsing preserves LF (`"\n"`), CRLF (`"\r\n"`), mixed line endings, and the
absence of a final newline. Edits preserve existing text outside the edited
region. When document collection edits need a new separator, they infer its
style from nearby structural boundaries, not raw heredoc payloads. With no
existing boundary to infer from, they use LF.

`DockerfileBuilder.DefaultNewLine` sets the default for automatic newlines,
explicit `NewLine()` calls, and `TokenBuilder` instances created for callbacks,
including nested callbacks. Set it to `"\n"` or `"\r\n"` to generate the same
line endings on every host:

```csharp
DockerfileBuilder builder = new() { DefaultNewLine = "\r\n" };
builder.FromInstruction("alpine").RunInstruction("echo hello");
```

Without configuration, both `DockerfileBuilder` and `TokenBuilder` use
`Environment.NewLine`: CRLF on Windows and LF on Linux. Wrapping an existing
document in a builder does not infer a new default from that document.
Changing `DefaultNewLine` affects subsequently generated separators; it does
not normalize existing text, supplied command strings, or heredoc payloads.

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

Structural editing rejects consumer-defined `Dockerfile` and `Token`
subclasses, including subclasses that inherit built-in serialization and quote
behavior. This applies to existing and incoming objects; ordinary reads and
serialization remain available. Restricting edits to library-defined runtime
types lets validation avoid invoking consumer code and inspecting runtime
method metadata.

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
