/-
  Json.lean — Canonical JSON serialization for Token trees.

  Produces compact, deterministic JSON matching the canonical format used by the
  C# differential test harness. Both sides must produce byte-identical output.

  Primitive tokens:
    {"type":"primitive","kind":"<kind>","value":"<escaped>"}

  Aggregate tokens:
    {"type":"aggregate","kind":"<kind>","quoteChar":<null|"char">,"children":[...]}

  Rules: no trailing commas, no extra whitespace, JSON-standard string escaping.
-/

import DockerfileModel.Token

namespace DockerfileModel

/-- Convert a PrimitiveKind to its canonical JSON kind string. -/
def PrimitiveKind.toJsonName : PrimitiveKind → String
  | .string     => "string"
  | .whitespace => "whitespace"
  | .symbol     => "symbol"
  | .newLine    => "newLine"

/-- Convert an AggregateKind to its canonical JSON kind string. -/
def AggregateKind.toJsonName : AggregateKind → String
  | .keyword          => "keyword"
  | .literal          => "literal"
  | .identifier       => "identifier"
  | .variableRef      => "variableRef"
  | .comment          => "comment"
  | .lineContinuation => "lineContinuation"
  | .keyValue         => "keyValue"
  | .instruction      => "instruction"
  | .construct        => "construct"
  | .heredoc          => "heredoc"

namespace Json

private def toHex4 (n : Nat) : List Char :=
  let hexDigit (d : Nat) : Char :=
    if d < 10 then Char.ofNat (48 + d)  -- '0' + d
    else Char.ofNat (97 + d - 10)       -- 'a' + (d - 10)
  [hexDigit ((n / 4096) % 16),
   hexDigit ((n / 256) % 16),
   hexDigit ((n / 16) % 16),
   hexDigit (n % 16)]

/-- Escape a string for JSON output.
    Handles: \\ \" \n \r \t and control chars < 0x20 as \uXXXX. -/
def jsonEscapeString (s : String) : String :=
  let rec loop (cs : List Char) (acc : List Char) : List Char :=
    match cs with
    | [] => acc.reverse
    | c :: rest =>
      if c == '\\' then loop rest ('\\' :: '\\' :: acc)
      else if c == '"' then loop rest ('"' :: '\\' :: acc)
      else if c == '\n' then loop rest ('n' :: '\\' :: acc)
      else if c == '\r' then loop rest ('r' :: '\\' :: acc)
      else if c == '\t' then loop rest ('t' :: '\\' :: acc)
      else if c.val < 0x20 then
        -- \uXXXX encoding for control characters
        let hexChars := toHex4 c.val.toNat
        loop rest (hexChars.reverse ++ ('u' :: '\\' :: acc))
      else loop rest (c :: acc)
  String.ofList (loop s.toList [])

/-- Serialize a Token to canonical compact JSON. -/
def Token.toJson : Token → String
  | .primitive kind value =>
    "{\"type\":\"primitive\",\"kind\":\"" ++ kind.toJsonName ++
    "\",\"value\":\"" ++ jsonEscapeString value ++ "\"}"
  | .aggregate kind children quoteInfo =>
    let childrenJson := "[" ++ String.intercalate "," (children.map Token.toJson) ++ "]"
    let quoteCharJson := match quoteInfo with
      | some qi => "\"" ++ jsonEscapeString (String.singleton qi.quoteChar) ++ "\""
      | none => "null"
    "{\"type\":\"aggregate\",\"kind\":\"" ++ kind.toJsonName ++
    "\",\"quoteChar\":" ++ quoteCharJson ++
    ",\"children\":" ++ childrenJson ++ "}"

end Json

end DockerfileModel
