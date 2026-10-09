# Native AOT and trimming

The `net10.0` target is annotated for trimming and Native AOT compatibility.
The `netstandard2.0` target is not annotated; use the `net10.0` asset when
publishing a Native AOT application.

Parsing and ordinary use of the built-in model types require no additional
preservation configuration. Structural edits accept library-defined
`Dockerfile` and `Token` runtime types. Consumer-defined subclasses remain
available for ordinary reads and serialization, but structural edits reject
them whether or not they override serialization or quote behavior. This
boundary avoids inspecting runtime method metadata and preserves the edit
validator's guarantee that consumer serialization code is never invoked during
validation.

The `Valleysoft.DockerfileModel.AotSmoke` project publishes and runs a Native
AOT executable that exercises parsing, structural edits with library-defined
types, and rejection of consumer-defined subclasses.
