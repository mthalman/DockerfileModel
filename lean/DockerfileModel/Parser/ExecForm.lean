/-
  Parser/ExecForm.lean -- JSON array parser for exec form.

  Parses the exec form used by CMD, ENTRYPOINT, RUN, SHELL, VOLUME, HEALTHCHECK:
    ["cmd", "arg1", "arg2"]

  Requirements:
  - Parse `[` + optional whitespace/line-continuations + JSON double-quoted strings
    + (comma-separated more strings) + `]`
  - Handle JSON escapes inside double-quoted strings:
    `\"`, `\\`, `\/`, `\b`, `\f`, `\n`, `\r`, `\t`, `\uXXXX`
  - Double quotes only (single quotes are NOT valid JSON)
  - Optional whitespace and line continuations between elements
  - Return tokens as a flat list for embedding in instruction token trees

  Token structure produced:
    SymbolToken('[')
    WhitespaceToken?        -- optional leading whitespace
    LiteralToken("cmd")     -- with quoteInfo (double quote)
    WhitespaceToken?
    SymbolToken(',')
    WhitespaceToken?
    LiteralToken("arg1")    -- with quoteInfo (double quote)
    ...
    WhitespaceToken?
    SymbolToken(']')
-/

import DockerfileModel.Token
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.ExecForm

open DockerfileModel
open DockerfileModel.Parser
open Parser

-- ============================================================
-- JSON escape handling
-- ============================================================

/-- Parse a JSON escape sequence inside a double-quoted string.
    Handles: \" \\ \/ \b \f \n \r \t \uXXXX
    Returns the raw escape text (e.g., "\\n") for round-trip fidelity. -/
def jsonEscapeSequence : Parser String := do
  let _ ← char '\\'
  let escaped ← anyChar
  match escaped with
  | '"'  => Parser.pure "\\\""
  | '\\' => Parser.pure "\\\\"
  | '/'  => Parser.pure "\\/"
  | 'b'  => Parser.pure "\\b"
  | 'f'  => Parser.pure "\\f"
  | 'n'  => Parser.pure "\\n"
  | 'r'  => Parser.pure "\\r"
  | 't'  => Parser.pure "\\t"
  | 'u'  => do
    -- Parse exactly 4 hex digits
    let h1 ← satisfy (fun c => c.isDigit || (c.toLower >= 'a' && c.toLower <= 'f')) "hex digit"
    let h2 ← satisfy (fun c => c.isDigit || (c.toLower >= 'a' && c.toLower <= 'f')) "hex digit"
    let h3 ← satisfy (fun c => c.isDigit || (c.toLower >= 'a' && c.toLower <= 'f')) "hex digit"
    let h4 ← satisfy (fun c => c.isDigit || (c.toLower >= 'a' && c.toLower <= 'f')) "hex digit"
    Parser.pure (String.ofList ['\\', 'u', h1, h2, h3, h4])
  | _ => Parser.fail s!"invalid JSON escape: \\{escaped}"

-- ============================================================
-- JSON double-quoted string parser
-- ============================================================

/-- Parse the content of a JSON double-quoted string (between the quotes).
    Handles escape sequences and regular characters.
    Returns the raw content for round-trip fidelity. -/
partial def jsonStringContent : Parser String := do
  let parts ← many (or'
    jsonEscapeSequence
    (do
      let c ← satisfy (fun c => c != '"' && c != '\\' && !isLineTerminator c)
        "JSON string character"
      Parser.pure (String.singleton c)))
  Parser.pure (String.join parts)

/-- Parse a JSON double-quoted string element.
    Returns a LiteralToken with double-quote quoteInfo. -/
def jsonStringElement : Parser Token := do
  let _ ← char '"'
  let content ← jsonStringContent
  let _ ← char '"'
  let children := if content.isEmpty then [] else [Token.mkString content]
  Parser.pure (Token.mkLiteral children (some ⟨'"'⟩))

-- ============================================================
-- Whitespace and line continuation helpers (between array elements)
-- ============================================================

/-- Parse optional whitespace and line continuations that may appear between
    JSON array elements. Returns a list of whitespace and line continuation tokens. -/
def interElementSpace (escapeChar : Char) : Parser (List Token) :=
  optionalWhitespaceOrLineContinuation escapeChar

-- ============================================================
-- JSON array parser
-- ============================================================

/-- Parse a JSON array of double-quoted strings: ["a", "b", "c"]
    Allows whitespace and line continuations between elements.
    Returns a flat list of tokens for embedding in instruction token trees.

    The escapeChar parameter is used for line continuation support
    within the array (e.g., backslash-newline between elements). -/
partial def jsonArrayParser (escapeChar : Char) : Parser (List Token) := do
  let openBracket ← char '['
  let wsAfterOpen ← interElementSpace escapeChar
  -- Try to parse the first string element (may be empty array)
  let firstElement ← optional jsonStringElement
  match firstElement with
  | none =>
    -- Empty array: []
    let wsBeforeClose ← interElementSpace escapeChar
    let closeBracket ← char ']'
    Parser.pure (concatTokens [
      [Token.mkSymbol openBracket],
      wsAfterOpen,
      wsBeforeClose,
      [Token.mkSymbol closeBracket]
    ])
  | some first =>
    -- Parse comma-separated additional elements
    let restElements ← many (do
      let wsBeforeComma ← interElementSpace escapeChar
      let comma ← char ','
      let wsAfterComma ← interElementSpace escapeChar
      let elem ← jsonStringElement
      Parser.pure (concatTokens [
        wsBeforeComma,
        [Token.mkSymbol comma],
        wsAfterComma,
        [elem]
      ]))
    let wsBeforeClose ← interElementSpace escapeChar
    let closeBracket ← char ']'
    Parser.pure (concatTokens [
      [Token.mkSymbol openBracket],
      wsAfterOpen,
      [first],
      List.flatten restElements,
      wsBeforeClose,
      [Token.mkSymbol closeBracket]
    ])

/-- Convenience: parse a JSON array from a string. -/
def parseJsonArray (input : String) (escapeChar : Char := '\\') : Option (List Token) :=
  (jsonArrayParser escapeChar).tryParse input

end DockerfileModel.Parser.ExecForm
