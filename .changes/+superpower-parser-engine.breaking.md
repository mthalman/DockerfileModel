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

#### Recommended action

Replace direct parser-factory usage with `Dockerfile.Parse` for throwing
parsing, or `Dockerfile.TryParse` when diagnostics should be returned as data.
Use `DockerfileParseResult.Diagnostics` and `DockerfileDiagnostic` for
structured error reporting.
