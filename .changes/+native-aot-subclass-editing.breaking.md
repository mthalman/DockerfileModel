### Restrict structural edits to library-defined model types

Structural-edit validation no longer reflects over consumer-defined runtime
types. To support that change without allowing unsafe serialization behavior,
structural edits are limited to model types defined by the library.

#### Previous behavior

Structural editing allowed consumer-defined `Dockerfile` and `Token`
subclasses when their effective serializer and quote behavior met the supported
implementation rules. Checking those runtime overrides required reflection over
consumer method metadata and generated trimming warnings.

#### New behavior

Structural edits reject consumer-defined `Dockerfile` and `Token` subclasses,
including subclasses that inherit all serialization and quote behavior.
Consumer-defined subclasses remain available for ordinary reads and
serialization.

#### Type of breaking change

This is a behavioral compatibility change. Existing code that structurally
edits a consumer-defined `Dockerfile` or `Token` subclass must use
library-defined model types instead.

#### Reason for change

Rejecting consumer-defined subclasses removes runtime reflection from edit
validation, avoids requiring preservation of arbitrary consumer metadata, and
keeps validation from invoking consumer serialization or quote code. This
allows the library's `net10.0` target to advertise Native AOT compatibility
without suppressing trimming warnings.

#### Recommended action

Use library-defined `Dockerfile` and `Token` types for models that will be
structurally edited. Consumer-defined subclasses can still be parsed, read, and
serialized, but cannot participate in structural writes.

#### Affected APIs

- `Dockerfile.Items` and document structural edits.
- `EditableList<T>` and structural edits on token-backed model collections.
