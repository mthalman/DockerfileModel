# Adopt typed parser directives and BuildKit-compatible header behavior

**Version introduced:** 3.0.0

## Previous behavior

Known directives parsed as instances of exactly `ParserDirective`. Full-file
parsing also recognized arbitrary directive names, so a custom declaration did
not stop subsequent directives from taking effect. Duplicate supported
directives were accepted, and legacy `Dockerfile.Parse` accepted invalid escape
values. `Dockerfile.EscapeChar` selected the first escape directive object
anywhere in `Items`, regardless of its placement.

`Dockerfile.TryParse` treated a value-less line such as `#escape=` as a directive
error. Recovery represented invalid directive regions as `MalformedConstruct`.

Standalone directive parsing could accept a prefix without consuming the entire
input and allowed non-ASCII letters in names. Values were parsed as
non-whitespace text, and value access or edits could apply literal-token quote
handling. Legacy full-file parsing treated a final directive without a newline
as a comment.

## New behavior

Known directives parse as `SyntaxDirective`, `EscapeDirective`, or
`CheckDirective`, all derived from `ParserDirective`. This also applies to the
generic `DockerfileBuilder.ParserDirective` methods. The concrete
`ParserDirective` constructor remains available.

Full-file parsing recognizes only `syntax`, `escape`, and `check` in the initial
header. An unknown directive-shaped comment, ordinary comment, blank line, or
instruction ends scanning; later directive-shaped lines are comments.
`Dockerfile.EscapeChar` interprets this effective serialized header rather than
searching all directive objects.

Duplicate supported names are rejected case-insensitively. Recognized escape
values must be a single backslash or backtick. `Dockerfile.Parse` throws
`ParseException`; strict `TryParse` returns a null model and DFP004. Recovery
preserves the offending line as a `Comment` with DFP004, retains the preceding
valid escape setting, and ends header scanning.

In the active header, `"#escape=\n"` is an ordinary comment that ends scanning
without a directive diagnostic. In contrast, `"#escape= \n"` contains a space
after `=` and is recognized as an invalid escape directive:
`Dockerfile.Parse` throws, and `Dockerfile.TryParse` reports DFP004.

`ParserDirective.Parse` requires its input to contain exactly one directive,
with an optional final newline. The constructor likewise rejects arguments
that produce trailing content. `GetParser()` remains composable; append `.AtEnd()`
when complete input consumption is required.

Names start with an ASCII letter and contain only ASCII letters or digits.
`DirectiveValue` retains the entire value, including internal spaces and literal
quotes. Final directives without a newline are recognized by both full-file
parsing entry points.

## Type of breaking change

This is a behavioral compatibility change affecting runtime types, accepted
input, construct classification, error handling, value interpretation, and
effective escape selection. Existing generic public member signatures remain
available, but callers that depend on the previous behavior must migrate.

## Reason for change

Directive recognition and escape selection must agree with BuildKit's header
rules across parsing entry points and programmatically edited models. Typed
directives provide structured access without discarding raw text or inferring
which features a frontend version supports.

## Recommended action

Replace exact-type checks such as `item.GetType() == typeof(ParserDirective)`
with assignability checks when handling all directives:

```csharp
bool isDirective = item is ParserDirective;
```

`OfType<ParserDirective>()` continues to include all typed directives. Update
tests and serializers that depend on exact runtime types or construct counts,
including at EOF.

Keep supported directives before custom comments, blank lines, and instructions.
Use at most one of each supported name, and configure the builder's `EscapeChar`
consistently with the active escape directive. Standalone
`ParserDirective.Parse("#custom=value")` still models custom declarations, but
full-file parsing treats them as comments.

To inspect invalid headers without throwing, use recovery:

```csharp
DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions
{
    Mode = DockerfileParseMode.Recover
});
```

Inspect `result.Diagnostics` and their `SourceSpan` values; do not use
`OfType<MalformedConstruct>()` as the sole way to find errors. A recovered model
can be non-null while `Success` is false. Do not rely on DFP004 to detect
value-less directive-shaped comments.

Use ASCII names for standalone custom directives and `Dockerfile.Parse` for
multiple lines. Treat `DirectiveValue` as literal text rather than a single
decoded word. Use `SyntaxDirective.Frontend.Reference` for the effective frontend
reference and `CheckDirective.TryGetOptions` for effective check settings.
Neither API resolves floating versions or validates frontend feature support.

## Affected APIs

- `ParserDirective.Parse`, `GetParser`, its constructor, `DirectiveValue`, and
  edits through `DirectiveValueToken`.
- `Dockerfile.Parse`, `Dockerfile.TryParse`, `Dockerfile.Items`, and
  `Dockerfile.EscapeChar`.
- `DockerfileBuilder.ParserDirective` string and token-callback overloads.
- Recovery consumers of `DockerfileParseResult.Diagnostics`, DFP004,
  `Comment`, and `MalformedConstruct`.
