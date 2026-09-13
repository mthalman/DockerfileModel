# Stage dependencies and image inventory

`Dockerfile.Analyze()` returns a read-only snapshot without changing the parsed
Dockerfile. It analyzes `FROM`, `COPY --from`, and every `RUN --mount` source,
including sources in `ONBUILD` triggers inherited by another stage in the same
Dockerfile.

> Analysis describes the supplied Dockerfile model; it does not validate a
> complete build. Dependencies can include forward references and cycles.
> Check `Diagnostics` before using the graph to plan a build. Analysis does not
> fetch external image metadata or accept named build-context configuration.

## Example

```csharp
Dockerfile dockerfile = Dockerfile.Parse(
    "ARG BASE=alpine\n" +
    "FROM ${BASE} AS build\n" +
    "FROM build AS final\n" +
    "COPY --from=busybox /bin/busybox /bin/busybox\n");

DockerfileAnalysis analysis = dockerfile.Analyze(
    new Dictionary<string, string?> { ["BASE"] = "alpine:3.22" });

AnalyzedStage build = analysis.GetStage("build");
AnalyzedStage first = analysis.GetStage(0);

foreach (DockerfileReference dependency in analysis.Dependencies)
{
    // Source depends on target: final -> build in this example.
    Console.WriteLine(
        $"{dependency.SourceStage!.Index} -> {dependency.TargetStage!.Index}: {dependency.Kind}");
}

foreach (DockerfileReference image in analysis.ExternalImages)
{
    Console.WriteLine($"{image.OriginalText} => {image.ResolvedValue}");
}

foreach (AnalysisDiagnostic diagnostic in analysis.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
}
```

The example reports one base-stage dependency and two external-image occurrences:
`${BASE} => alpine:3.22` and `busybox => busybox`.

## ARG overrides

```csharp
public DockerfileAnalysis Analyze(
    IDictionary<string, string?>? argOverrides = null);
```

Omit `argOverrides`, or pass null, to analyze without overrides. Dictionary keys
are case-sensitive ARG names. Overrides apply to declared ARGs and automatic
platform arguments; they do not supply values for arbitrary undeclared variables.

| Dictionary entry | Meaning |
| --- | --- |
| Key omitted | Use the Dockerfile's declarations and scope rules. |
| `["NAME"] = "value"` | Supply this value instead of the ARG's declared default. |
| `["NAME"] = null` | Mark the argument unset. |
| `["NAME"] = ""` | Supply a set-but-empty value. |

An unset argument has no value. An empty argument has a value whose length is
zero. An unresolved value depends on information the analyzer cannot determine,
such as another variable without an available value. These states are distinct:
an unresolved expression is not treated as an empty string or as an unset value
that can automatically select a fallback.

Automatic platform arguments must be supplied when needed for resolution;
analysis does not infer the host platform. Overrides never enable variable
expansion in COPY or mount source selectors.

## Snapshot contract

| Member | Contents |
| --- | --- |
| `Stages` | Stage metadata in Dockerfile order, with zero-based indices and original `FromInstruction` objects. |
| `References` | Source-selector occurrences, including invalid, unresolved, and deferred references. |
| `Dependencies` | References bound to an internal stage. `SourceStage` depends on `TargetStage`; `Kind` distinguishes `BaseStage`, `CopySource`, and `MountSource`. |
| `ExternalImages` | Effective external-image occurrences. Repeated uses remain separate; names are not normalized or verified against a registry. |
| `UnresolvedReferences` | References classified as `Unresolved`; excludes `Invalid` and `Deferred` references. |
| `Diagnostics` | Structured codes, severity, messages, original instructions/tokens, and relevant stage indices or variable names. |

### Stage lookup

`GetStage(int)` throws `ArgumentOutOfRangeException` for an invalid index.
`GetStage(string)` requires a unique case-insensitive name match: it throws
`KeyNotFoundException` if absent and `InvalidOperationException` if ambiguous.
`FindStages(string)` returns all matching declarations, or an empty list.

### Reference classifications

Every occurrence appears in `References`. Its `Classification` determines
whether it also appears in one of the filtered collections:

| Classification | Meaning | Filtered collection |
| --- | --- | --- |
| `Stage` | Bound to an internal stage, even if the edge has a forward-reference or cycle diagnostic. | `Dependencies` |
| `ExternalImage` | Classified as an external image without checking a registry. | `ExternalImages` |
| `Unresolved` | Evaluation needs unavailable information or cannot be completed by the analyzer. | `UnresolvedReferences` |
| `Invalid` | A reference violates a supported syntax or semantic rule. | None |
| `Deferred` | An ONBUILD declaration awaiting a consuming stage. | None |
| `Scratch` | The reference resolves to `scratch`. | None |
| `BuildContext` | An explicit empty COPY or mount source selector. | None |

For example, `COPY --from=$BASE` and `RUN --mount=from=$BASE,...` produce
`Invalid` references with an `UnsupportedVariableExpansion` diagnostic, even
when `BASE` has a supplied value. They do not appear in `UnresolvedReferences`.
Inspect `References` and `Diagnostics` for all analysis problems, rather than
relying on one filtered collection.

### Model identity and editing

Each reference captures `OriginalText` from its operand token and retains the
original `Instruction` and `OperandToken` as editing handles. `ResolvedValue` is
the evaluated value, or null when evaluation is incomplete or deferred.
These handles remain mutable, but captured names, values, collections, and
relationships do not change. Call `Analyze()` again after editing the model.
Do not mutate the model concurrently with analysis.

If builder-flag decoding changes mount CSV boundaries so that a selector cannot
be mapped to its original token, analysis reports `UnsupportedEvaluation` instead
of guessing a stage or image. In that case, `OperandToken` identifies the whole
original `Mount`. Unresolved references are not safe automatic edit targets.

## Resolution and diagnostics

Analysis follows instruction-specific BuildKit rules:

* Global ARG declarations and supplied overrides can resolve `FROM`. Stage
  environments are used for mount-type evaluation, not for expanding subsequent
  FROM operands.
* `COPY --from` and mount `from` selectors do not support ARG/ENV expansion.
  Variable-based selectors receive diagnostics even if an override is supplied.
  Use an ARG-expanded `FROM ... AS alias` and refer to that alias instead.
* `FROM` binds to previously declared stages. BuildKit lowercases aliases but
  does not lowercase the `FROM` operand for this lookup. COPY and mount named
  lookups are case-insensitive. Public `GetStage` lookup is separate from these
  instruction-binding rules.
* COPY supports numeric stage indices; RUN mount sources do not. An unmatched
  name may be an external image, so absence from the stage list alone is not an
  unknown-stage error.
* Duplicate names produce warnings. FROM uses the latest previous matching
  declaration; COPY and mount name resolution use the last matching declaration.
* Forward references and cycles retain their bound edges with diagnostics.
  Analysis examines all stages, including stages a particular build may skip.
* `scratch` and explicit empty selectors that use the build context have separate
  classifications and are not external images. A mount without a `from` entry
  does not create an image-reference occurrence. For repeated mount keys,
  analysis uses the effective last entry and retains its original operand token.
  An earlier invalid variable-based selector still receives a diagnostic, even
  when a later entry replaces it.

Semantic failures are reported without aborting analysis. Consumers should
inspect diagnostics before treating the graph as a valid build plan.

### Stage ARG redeclarations

Unless an override is supplied, a bare stage `ARG NAME` imports a global value
before falling back to an existing stage value. A global `ARG NAME` with no value
to import leaves an existing stage value unchanged. A global value of `""` still
takes precedence. A global expression that cannot be resolved remains unresolved
rather than falling back to an existing stage value.

## ONBUILD references

Copying or mounting from stages, images, or named contexts inside ONBUILD requires
[Dockerfile syntax 1.11 or newer](https://docs.docker.com/reference/dockerfile/#copy-or-mount-from-stage-image-or-context).
Analysis does not enforce frontend versions.

An `ONBUILD COPY` or `ONBUILD RUN` declaration is recorded with phase
`DeferredOnBuild`; it does not create an active dependency for its declaring
stage. When another stage uses that stage as its base, analysis adds an
`InheritedOnBuild` occurrence for the child. The occurrence retains the original
declaration and operand while `SourceStage` identifies the child. Consumed
triggers do not automatically execute again in grandchildren.

```dockerfile
# syntax=docker/dockerfile:1.11
FROM alpine AS parent
ONBUILD COPY --from=tools /tool /tool
FROM busybox AS tools
FROM parent AS child
```

The COPY declaration creates no dependency from `parent` to `tools`. When `child`
inherits `parent`, analysis adds a COPY dependency from `child` to `tools`, in
addition to the base-stage dependency from `child` to `parent`.

For the two COPY reference occurrences in this example:

| Property | Deferred declaration | Inherited occurrence |
| --- | --- | --- |
| `Phase` | `DeferredOnBuild` | `InheritedOnBuild` |
| `Classification` | `Deferred` | `Stage` |
| `DeclaringStage` | `parent` | `parent` |
| `SourceStage` | null | `child` |
| `TargetStage` | null | `tools` |
| `ResolvedValue` | null | `"tools"` |

Both occurrences retain the same original nested `CopyInstruction` in
`Instruction`, its source token in `OperandToken`, and the wrapping declaration
in `OnBuildInstruction`. In general, an inherited occurrence has a non-null
`TargetStage` only when its reference binds to an internal stage; an inherited
external-image, invalid, or unresolved reference has no target stage.

Ordinary, non-ONBUILD references have phase `Immediate`, the same stage in
`DeclaringStage` and `SourceStage`, and a null `OnBuildInstruction`.

BuildKit reparses inherited trigger text with the default backslash escape for
builder flags, even when the Dockerfile declares a different escape character.
Analysis applies that flag-decoding rule while retaining the original instruction
and token handles. Variable-expression evaluation still uses the Dockerfile's
escape character.

If reparsing changes flag boundaries or reveals flags that have no corresponding
tokens in the original model, analysis reports `UnsupportedEvaluation` rather
than guessing a dependency. The inherited reference is `Unresolved`, and its
`OperandToken` identifies the whole original nested instruction. The deferred
occurrence retains that same instruction as its operand without a diagnostic.

## Static-analysis limits

Analysis does not fetch external image metadata, discover ONBUILD triggers
stored in external images, or accept named build-context configuration. An
external image classification assumes no caller-supplied named context overrides
that name. It does not inspect arbitrary shell commands, ADD URLs, or heredoc
bodies for image-like strings.

This API is not a complete BuildKit validator or execution simulator. It checks
reference syntax and source relationships, not registry existence, image
contents, every mount option, or target-dependent execution. Numeric stage
indices refer to declared FROM stages, not BuildKit's incidental internal
dispatch states. Unsupported expression evaluation remains explicit rather than
falling back to a guessed image name.
