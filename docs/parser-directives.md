# Parser directives and frontend metadata

The library models `syntax`, `escape`, and `check` directives without interpreting
which features a frontend version supports. It preserves the original directive
text, including casing, whitespace, and line endings.

## Typed directives

Known directives parsed by `ParserDirective.Parse`, `Dockerfile.Parse`, and
`Dockerfile.TryParse` use the corresponding subclass:

| Name | Model | Structured access |
| --- | --- | --- |
| `syntax` | `SyntaxDirective` | `Frontend` metadata |
| `escape` | `EscapeDirective` | `EscapeChar` |
| `check` | `CheckDirective` | `TryGetOptions(out options, out error)` |

The concrete `ParserDirective` constructor and its string/token properties remain
available. Standalone generic parsing still accepts arbitrary directive names:
`ParserDirective.Parse("#custom=value")` returns a generic directive. Full-file
parsing treats that same line as a comment and stops scanning the header.

Directive keys are case-insensitive. Values are literal text, not shell strings:
quotes, variable expressions, and escape characters are not decoded.

## Frontend metadata is descriptive

```csharp
Dockerfile file = Dockerfile.Parse(
    "#syntax=docker/dockerfile:1.20.0-labs\nFROM scratch\n");

DockerfileFrontendMetadata frontend = file.Frontend;
string? image = frontend.Image;       // docker/dockerfile
string? tag = frontend.Tag;           // 1.20.0-labs
string? version = frontend.Version;   // 1.20.0
DockerfileFrontendChannel? channel = frontend.Channel; // Labs
```

`SyntaxDirective.Frontend` exposes the same metadata for a standalone directive.
`Reference` is the effective image reference; `Image`, `Tag`, and `Digest` expose
its components without dropping a tag when a digest is also present.
The entire raw directive value remains available as `DirectiveValue`.

`Kind` distinguishes `Bundled` (no effective syntax directive), `Official`
(`docker/dockerfile` and recognized Docker Hub-qualified forms), `Custom`, and
`Unresolved` (for example, a variable expression or malformed reference).
Unresolved references retain their text rather than becoming a guessed image.

`Version` is numeric tag text, not a resolved version. Tags `1`, `1.20`, and
`1.20.0` retain those exact version components. A numeric custom tag can also
expose version text, without asserting that the custom frontend uses any
particular versioning policy. `Channel` is assigned only for recognized
official stable/labs naming forms.

Metadata does **not** determine whether a tag exists, resolve floating tags,
verify digest identity, contact a registry, or inspect the host's bundled
frontend. It does not provide feature-support queries or compatibility
validation. A declared old version never prevents the library from parsing a
newer instruction feature that its grammar recognizes.

Each metadata read returns an immutable snapshot. Reading again after a token
edit reflects the new declaration; a previously returned snapshot does not
change.

## Check settings

```csharp
CheckDirective check = CheckDirective.Parse(
    "#check=skip=JSONArgsRecommended,FutureCheck;experimental=all;error=true");

if (check.TryGetOptions(out CheckDirectiveOptions? options, out string? error))
{
    Console.WriteLine(options!.WarningsAsErrors); // True
    Console.WriteLine(options.ExperimentalAll);   // True
}
else
{
    Console.WriteLine(error);
}
```

Options are immutable snapshots. They expose `SkippedChecks`, `SkipAll`,
`WarningsAsErrors`, `ExperimentalChecks`, and `ExperimentalAll`. Check names
remain case-sensitive and open-ended; the library does not maintain or execute
a list of BuildKit checks.

The option grammar follows BuildKit: option keys are case-sensitive, and Boolean
values accept `1`, `t`, `T`, `TRUE`, `true`, `True` and their false counterparts
`0`, `f`, `F`, `FALSE`, `false`, `False`. The raw option value remains unchanged.
Repeated options follow BuildKit's configuration parser rather than the
duplicate-*directive* rule. Later named lists replace earlier lists, but `all`
sets a flag that later lists do not clear. Setting `all` also preserves any
previously parsed list. For example, both `skip=all;skip=Foo` and
`skip=Foo;skip=all` produce `SkipAll == true` with `SkippedChecks` containing
only `"Foo"`.
The same rules apply to `experimental`; repeated `error` settings use the last
Boolean value.

BuildKit extracts the portion before the first ASCII space before interpreting
check settings. For example, `skip=Foo, Bar;error=true` retains its entire text,
but the effective options are read from `skip=Foo,`. Tabs are not ASCII spaces.
Use a compact value without spaces to avoid this distinction.

Unknown option keys or malformed values make `TryGetOptions` return `false`,
null options, and a meaningful error. Full-file parsing still preserves the
typed directive. Missing settings receive defaults only after successful
interpretation.

Use `new CheckDirective(new CheckDirectiveOptions(...))` to generate a canonical
value. Structured check names must be nonempty and cannot contain whitespace or
option separators. Use `skipAll` or `experimentalAll` rather than supplying the
reserved name `all`.

BuildKit's three-part option split can retain semicolons inside a name in the
final option. Such parsed snapshots cannot be safely reordered by the canonical
constructor: `new CheckDirective(options)` rejects them with `ArgumentException`.
Preserve the original directive's raw value instead.

## Header placement and errors

Full-file parsing recognizes supported directives only until the first ordinary
comment, unknown directive-shaped comment, blank line, or instruction. A single
newline separating directives is not a blank line. Directive values do not
support line continuations.

Duplicates are checked case-insensitively in the active header. Strict parsing
fails on duplicates and recognized escape values other than a single backslash
or backtick. Recovery returns DFP004 and preserves the offending line as a
`Comment`, keeps the preceding valid escape setting, and stops header scanning.
Misplaced directives are comments and do not affect escape or frontend metadata.

`Dockerfile.Frontend` and `EscapeChar` read the effective current header, including
models constructed or edited programmatically. Each read scans the leading
constructs only until header scanning ends, rather than serializing the entire
Dockerfile. Results are not cached, so token edits take effect on the next read.
These properties retain the valid header state preceding an invalid directive;
they are not validation operations. Use
`Dockerfile.TryParse(file.ToString())` to obtain structural/header diagnostics for
an edited model.

See [Parsing, recovery, and diagnostics](parsing.md) for error results and
original-input source spans.

## Editing and construction

Inherited `DirectiveValue`, `DirectiveValueToken`, and name-token edits remain
available. Untouched surrounding trivia is preserved. Replacing a complete
value intentionally replaces that value's formatting.

Typed getters interpret the current serialized grammar, not cached constructor
arguments. Whitespace edits inside a value token have the same meaning as
reparsing that declaration, without changing its text. If inherited edits rename
a syntax or escape directive, or no longer form a single directive, its typed
getter throws `InvalidOperationException`. Invalid escape values also make
`EscapeChar` throw. For an invalid or renamed check directive, `TryGetOptions`
returns failure with a reason.

The builder supports typed string/options and token-callback methods as well as
the existing generic `ParserDirective` methods.

Configure the builder's `EscapeChar` consistently with an explicit escape
directive. When `EscapeChar` is non-default and automatic insertion is enabled,
the builder inserts an escape directive before the first construct unless that
construct is already a matching escape directive. Automatic insertion is enabled
by default. To add an escape directive explicitly, call `EscapeDirective` first,
as shown below. Do not add another after automatic insertion.

```csharp
DockerfileBuilder builder = new() { EscapeChar = '`', DefaultNewLine = "\n" };
builder.EscapeDirective('`')
    .SyntaxDirective("docker/dockerfile:1.20")
    .CheckDirective(new CheckDirectiveOptions(skipAll: true))
    .FromInstruction("scratch");
```

## Upstream basis

Header recognition follows BuildKit's
[directive parser](https://github.com/moby/buildkit/blob/b7c2f61e845cbf42d9be4e2a675a67b076c1cec9/frontend/dockerfile/parser/directives.go).
Check-option interpretation follows its
[linter configuration parser](https://github.com/moby/buildkit/blob/b7c2f61e845cbf42d9be4e2a675a67b076c1cec9/frontend/dockerfile/linter/linter.go).
These references describe directive grammar, not a maintained version-feature
catalog.
