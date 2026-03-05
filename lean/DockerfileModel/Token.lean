/-
  Token.lean — Formal model of the Valleysoft.DockerfileModel token hierarchy.

  This mirrors the C# hierarchy:
    Token (abstract)
      ├── PrimitiveToken (abstract) — leaf tokens with a string value
      │     ├── StringToken
      │     ├── WhitespaceToken
      │     ├── SymbolToken
      │     └── NewLineToken
      └── AggregateToken (abstract) — composite tokens with children
            ├── KeywordToken
            ├── LiteralToken (IQuotableToken)
            ├── IdentifierToken (IQuotableToken)
            ├── VariableRefToken (overrides GetUnderlyingValue to prepend "$")
            ├── CommentToken
            ├── LineContinuationToken
            ├── KeyValueToken
            └── InstructionToken (wraps a full instruction)

  Key behaviors modeled:
    1. PrimitiveToken.toString returns its string value directly.
    2. AggregateToken.toString concatenates children's toString results.
    3. VariableRefToken.toString prepends "$" before concatenating children.
    4. IQuotableToken wraps toString output in quote characters when present.
-/

namespace DockerfileModel

/-- The kind of a primitive token, mirroring the C# PrimitiveToken subclasses. -/
inductive PrimitiveKind where
  | string    -- StringToken: arbitrary string content
  | whitespace -- WhitespaceToken: whitespace-only content (spaces, tabs)
  | symbol    -- SymbolToken: single-character symbols (e.g., '-', '=', '#', '{', '}')
  | newLine   -- NewLineToken: line ending characters ("\n", "\r\n")
  deriving Repr, BEq, Inhabited

/-- The kind of an aggregate token, mirroring the C# AggregateToken subclasses. -/
inductive AggregateKind where
  | keyword          -- KeywordToken: instruction keywords like "FROM", "RUN"
  | literal          -- LiteralToken: literal values (IQuotableToken)
  | identifier       -- IdentifierToken: identifiers (IQuotableToken)
  | variableRef      -- VariableRefToken: variable references (overrides toString to prepend "$")
  | comment          -- CommentToken: comment content
  | lineContinuation -- LineContinuationToken: escape char + newline
  | keyValue         -- KeyValueToken: key=value pairs (possibly with -- flag prefix)
  | instruction      -- InstructionToken: a full instruction (keyword + args)
  | construct        -- DockerfileConstruct: top-level construct wrapper
  deriving Repr, BEq, Inhabited, DecidableEq

/-- Quote information for tokens implementing IQuotableToken.
    In C#, LiteralToken and IdentifierToken implement IQuotableToken,
    which wraps the GetUnderlyingValue output in quote characters. -/
structure QuoteInfo where
  quoteChar : Char
  deriving Repr, BEq, Inhabited

/--
  The Token inductive type models the C# Token hierarchy.

  A Token is either:
  - `primitive`: a leaf token with a kind and string value
  - `aggregate`: a composite token with a kind, list of children, and optional quote info

  The `aggregate` constructor mirrors C# `AggregateToken`, which stores a `List<Token>`
  of children. The optional `QuoteInfo` models the `IQuotableToken` interface — when
  present, `toString` wraps the underlying value in quote characters.

  Design note: Rather than modeling VariableRefToken's `$` prefix as a separate
  constructor, we use the `AggregateKind.variableRef` kind and handle the prefix
  in the `toString` function. This keeps the inductive type simple while faithfully
  modeling the C# override of `GetUnderlyingValue`.
-/
inductive Token where
  | primitive (kind : PrimitiveKind) (value : String) : Token
  | aggregate (kind : AggregateKind) (children : List Token) (quoteInfo : Option QuoteInfo) : Token
  deriving Repr, BEq, Inhabited

namespace Token

/--
  Convert a token to its string representation, mirroring C# `Token.ToString()`.

  This single recursive function combines both `GetUnderlyingValue` and `ToString`
  from the C# hierarchy:

  For primitive tokens: returns the stored string value directly.
  (Mirrors `PrimitiveToken.GetUnderlyingValue` which returns `Value`.)

  For aggregate tokens:
  1. Concatenates the `toString` results of all children.
  2. If the kind is `variableRef`, prepends "$" to the concatenation.
     (Mirrors `VariableRefToken.GetUnderlyingValue` override.)
  3. If quote info is present, wraps the result in quote characters.
     (Mirrors the `IQuotableToken` check in `Token.ToString`.)
-/
def toString : Token → String
  | .primitive _kind value => value
  | .aggregate kind children quoteInfo =>
    let childConcat := String.join (children.map toString)
    let underlying := match kind with
      | .variableRef => "$" ++ childConcat
      | _ => childConcat
    match quoteInfo with
    | some qi => String.singleton qi.quoteChar ++ underlying ++ String.singleton qi.quoteChar
    | none => underlying

instance : ToString Token where
  toString := Token.toString

/-- Helper: create a StringToken (PrimitiveKind.string). -/
def mkString (s : String) : Token :=
  .primitive .string s

/-- Helper: create a WhitespaceToken. -/
def mkWhitespace (s : String) : Token :=
  .primitive .whitespace s

/-- Helper: create a SymbolToken from a single character. -/
def mkSymbol (c : Char) : Token :=
  .primitive .symbol (String.singleton c)

/-- Helper: create a NewLineToken. -/
def mkNewLine (s : String) : Token :=
  .primitive .newLine s

/-- Helper: create a KeywordToken (aggregate with keyword kind, no quotes). -/
def mkKeyword (children : List Token) : Token :=
  .aggregate .keyword children none

/-- Helper: create a LiteralToken (aggregate with literal kind, optional quotes). -/
def mkLiteral (children : List Token) (quoteInfo : Option QuoteInfo := none) : Token :=
  .aggregate .literal children quoteInfo

/-- Helper: create an IdentifierToken (aggregate with identifier kind, optional quotes). -/
def mkIdentifier (children : List Token) (quoteInfo : Option QuoteInfo := none) : Token :=
  .aggregate .identifier children quoteInfo

/-- Helper: create a VariableRefToken.
    The children do NOT include the "$" — it is prepended by toString automatically. -/
def mkVariableRef (children : List Token) : Token :=
  .aggregate .variableRef children none

/-- Helper: create a CommentToken. -/
def mkComment (children : List Token) : Token :=
  .aggregate .comment children none

/-- Helper: create a LineContinuationToken. -/
def mkLineContinuation (children : List Token) : Token :=
  .aggregate .lineContinuation children none

/-- Helper: create a KeyValueToken. -/
def mkKeyValue (children : List Token) : Token :=
  .aggregate .keyValue children none

/-- Helper: create an InstructionToken. -/
def mkInstruction (children : List Token) : Token :=
  .aggregate .instruction children none

/-- Helper: create a DockerfileConstruct token. -/
def mkConstruct (children : List Token) : Token :=
  .aggregate .construct children none

/-- Extract the children of an aggregate token, or empty list for primitives. -/
def children : Token → List Token
  | .primitive .. => []
  | .aggregate _ cs _ => cs

/-- Check if a token is a primitive. -/
def isPrimitive : Token → Bool
  | .primitive .. => true
  | .aggregate .. => false

/-- Check if a token is an aggregate. -/
def isAggregate : Token → Bool
  | .primitive .. => false
  | .aggregate .. => true

end Token

end DockerfileModel
