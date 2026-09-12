# Parsing, recovery, and diagnostics

## Parse results

`Dockerfile.Parse(text)` retains its existing throwing behavior. Use `TryParse` to
receive syntax errors as data instead:

```csharp
DockerfileParseResult result = Dockerfile.TryParse(text);
if (!result.Success)
{
    foreach (DockerfileDiagnostic diagnostic in result.Diagnostics)
    {
        Console.WriteLine(
            $"{diagnostic.Code}: {diagnostic.Message} " +
            $"({diagnostic.SourceSpan.Start.Line},{diagnostic.SourceSpan.Start.Column})");
    }
}
```

`TryParse` defaults to `DockerfileParseMode.Strict`: it stops at the first error
and returns a null `Dockerfile`. Null input and invalid option enum values are
argument errors, not syntax diagnostics. An empty input produces an empty model
with `Success == true`.

`Success` means there are no error-level diagnostics, not that a model is
available. Warnings do not make `Success` false. Diagnostics are read-only and
ordered by source location. Parsing does not perform semantic, capability, or
best-practice validation.

## Recovery and unknown-instruction options

Enable recovery to inspect valid instructions around malformed regions. In
recovery mode, `result.Dockerfile` is non-null even when `result.Success` is
false, including for wholly malformed input.

In this example, `result.Success` is false because `FROM` has no argument, but
the recovered model preserves the entire input:

```csharp
string text = "FROM scratch\nFUTURE-COPY source target\nFROM\nRUN echo ready\n";
DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions
{
    Mode = DockerfileParseMode.Recover,
    UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
});

Dockerfile model = result.Dockerfile!;
bool roundTrips = model.ToString() == text; // true
UnknownInstruction unknown = model.Items.OfType<UnknownInstruction>().Single();
MalformedConstruct malformed = model.Items.OfType<MalformedConstruct>().Single();
RunInstruction run = model.Items.OfType<RunInstruction>().Single();
```

`MalformedConstruct` retains the failed region verbatim and is **not** an
`Instruction`.
`UnknownInstruction` derives from `GenericInstruction`, but its arguments are
opaque: they are not validated or expanded by variable resolution. Its
`ArgLines` exposes the raw argument suffix, including whitespace and newlines.
The existing `GenericInstruction.Parse` and constructors still accept only
known instruction names.

Unknown-instruction handling is independent of recovery:

| Mode | Unknown behavior | Unknown instruction | Malformed known instruction |
| --- | --- | --- | --- |
| Strict | Error (default) | Error, no model | Error, no model |
| Strict | Preserve | Opaque instruction and warning | Error, no model |
| Recover | Error (default) | Malformed construct and error | Malformed construct and error |
| Recover | Preserve | Opaque instruction and warning | Malformed construct and error |

## Recovery boundaries and heredocs

Recovery boundaries account for line continuations, the active escape
directive, comments in continued instructions, quoted text, and heredocs.
`TryParse` recognizes heredocs in `RUN`, `COPY`, and `ADD`, including their
`ONBUILD` forms.

A missing heredoc terminator retains everything through EOF as one
malformed construct; instruction-looking lines inside its body cannot safely
be treated as later instructions. Unlike legacy `Parse`, `TryParse` reports
unterminated heredocs as errors. It also requires complete, lossless construct
parsing and rejects malformed escape directives rather than applying an unsafe
escape value. Recovery does not infer arbitrary custom frontend grammar:
unknown instructions use standard Dockerfile continuation boundaries, not
custom heredoc rules.

Heredoc delimiter names use shell quoting and backslash rules independently of
the Dockerfile escape directive. `#` can be part of a delimiter name. A delimiter
containing `<` does not open a heredoc, even when that character is quoted or
escaped.

Continued heredoc operators and delimiters retain their original text while
exposing their logical name and leading-tab stripping (`<<-`) behavior. An
unquoted terminal backslash stays in the original marker text but is omitted
from the decoded delimiter name. A delimiter that decodes to an empty string,
including an empty quoted string, does not open a heredoc.

## Source locations

`Dockerfile.Parse` and `Dockerfile.TryParse` populate
`DockerfileConstruct.SourceSpan` on top-level constructs in `Dockerfile.Items`.
Nested instructions, such as the instruction wrapped by `ONBUILD`, have no span.
Standalone instruction parsers and programmatically created constructs also
leave `SourceSpan` null.

A span describes the **original input**, including the construct's whitespace
and newline. Editing or reordering the model does not update spans; reparse the
edited text to obtain new locations. Tokens do not have spans.

`SourceSpan.Start` is inclusive and `End` is exclusive. Positions contain a
zero-based UTF-16 `Offset` and one-based `Line` and `Column`. Tabs count as one
column, as does each UTF-16 code unit. LF and CRLF each advance one line; a lone
CR counts as a column character, not a line boundary. For an unchanged construct:

```csharp
SourceSpan span = run.SourceSpan!.Value;
string original = text.Substring(span.Start.Offset, span.Length);
```

## Diagnostic codes

Diagnostic spans identify the offending keyword, failure position, or region
when a narrower location is unavailable. EOF failures can have zero-length
spans. Code strings and their named constants in `DockerfileDiagnosticCodes`
are stable; message wording is not a machine-readable contract.

| Code | Constant | Meaning |
| --- | --- | --- |
| DFP001 | InvalidSyntax | Invalid syntax or a construct that cannot be parsed without losing text |
| DFP002 | UnknownInstruction | Unknown name; a warning when preserved, otherwise an error |
| DFP003 | UnterminatedHeredoc | An opening heredoc marker has no terminator before EOF |
| DFP004 | InvalidParserDirective | A malformed initial directive or invalid escape value |
