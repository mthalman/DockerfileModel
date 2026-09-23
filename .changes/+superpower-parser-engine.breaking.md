### Migrate the parser engine to Superpower

#### Previous behavior

Parser implementation details and instruction/token `GetParser()` factories
were exposed as public APIs backed by Sprache-shaped parser combinators.

#### New behavior

The grammar now uses Superpower's native `TextParser<T>`, `Result<T>`, and
combinator APIs directly. The Sprache-shaped compatibility parser types have
been removed. Instruction and token `GetParser()` factories are now internal
implementation details and are no longer available to consumers.
`Dockerfile.Parse` and `Dockerfile.TryParse` are the supported parsing entry
points and expose the library-owned parse result and diagnostic types.

#### Type of breaking change

This is a public API breaking change. Consumers that call parser factories or
reference the former parser-engine package types must update their code.

#### Reason for change

The Superpower migration removes the temporary Sprache-shaped compatibility
layer so the parser can use one native combinator model internally. Keeping the
factory surface public would continue to expose implementation details that the
library does not support as stable extension points and would constrain future
parser maintenance.

#### Recommended action

Replace direct parser-factory usage with `Dockerfile.Parse` for throwing
parsing, or `Dockerfile.TryParse` when diagnostics should be returned as data.
Use `DockerfileParseResult.Diagnostics` and `DockerfileDiagnostic` for
structured error reporting.

#### Affected APIs

- Public `GetParser()` factory members previously exposed by instruction and
  token model types.
- Consumer code that referenced the former public Sprache-compatibility parser
  types and their `Result<T>`-style return values.
- Parsing call sites that should now use `Dockerfile.Parse`,
  `Dockerfile.TryParse`, `DockerfileParseResult`, and
  `DockerfileDiagnostic`.
