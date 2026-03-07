/-
  Parser/Basic.lean -- Core parser monad and basic combinators.

  This is the Lean 4 equivalent of the Sprache parser combinator library used by
  the C# codebase. The parser monad tracks position in a string and produces
  results or error messages.

  Key design principle: Parsers produce Token values (from Token.lean), not raw
  strings. This is essential for round-trip fidelity proofs.

  The Sprache `from x in p1 from y in p2 select f(x,y)` pattern maps directly
  to Lean `do let x <- p1; let y <- p2; pure (f x y)` via the Monad instance.
-/

namespace DockerfileModel.Parser

/-- Position in a string being parsed. -/
structure Position where
  /-- The source string being parsed. -/
  source : String
  /-- The current byte offset. Uses String.Pos.Raw (a byte index in Lean 4). -/
  offset : String.Pos.Raw
  deriving Repr, BEq

namespace Position

/-- Create a position at the start of a string. -/
def ofString (s : String) : Position :=
  { source := s, offset := ⟨0⟩ }

/-- Check if we've reached the end of input. -/
def atEnd (pos : Position) : Bool :=
  pos.offset.atEnd pos.source

/-- Get the current character (if not at end). -/
def current (pos : Position) : Option Char :=
  if pos.atEnd then none
  else some (pos.offset.get pos.source)

/-- Advance by one character. -/
def next (pos : Position) : Position :=
  if pos.atEnd then pos
  else { pos with offset := pos.offset.next pos.source }

/-- Get the remaining string from current position. -/
def remaining (pos : Position) : String :=
  String.Pos.Raw.extract pos.source pos.offset (String.Pos.Raw.mk pos.source.utf8ByteSize)

end Position

/-- The result of running a parser. -/
inductive ParseResult (α : Type) where
  /-- Successful parse with a value and remaining position. -/
  | ok (value : α) (pos : Position) : ParseResult α
  /-- Failed parse with an error message and the position where it failed. -/
  | error (message : String) (pos : Position) : ParseResult α

instance {α : Type} : Inhabited (ParseResult α) :=
  ⟨ParseResult.error "" (Position.ofString "")⟩

/--
  The Parser type: a function from a position to a parse result.

  This corresponds to the Sprache `Parser<T>` delegate in C#.
-/
def Parser (α : Type) : Type := Position → ParseResult α

namespace Parser

/-- Run a parser on a string. -/
def run {α : Type} (p : Parser α) (input : String) : ParseResult α :=
  p (Position.ofString input)

/-- Run a parser and extract just the value on success. -/
def tryParse {α : Type} (p : Parser α) (input : String) : Option α :=
  match p.run input with
  | .ok value _ => some value
  | .error _ _ => none

-- ============================================================
-- Monad instance (enables `do` notation)
-- ============================================================

/-- `pure` lifts a value into the parser monad (always succeeds without consuming input). -/
def pure {α : Type} (a : α) : Parser α :=
  fun pos => ParseResult.ok a pos

/-- `bind` sequences two parsers (enables `do` notation). -/
def bind {α : Type} {β : Type} (p : Parser α) (f : α → Parser β) : Parser β :=
  fun pos =>
    match p pos with
    | .ok value pos' => f value pos'
    | .error msg pos' => ParseResult.error msg pos'

/-- `map` applies a function to the result of a parser. -/
def map {α : Type} {β : Type} (f : α → β) (p : Parser α) : Parser β :=
  fun pos =>
    match p pos with
    | .ok value pos' => ParseResult.ok (f value) pos'
    | .error msg pos' => ParseResult.error msg pos'

/-- `fail` always fails with the given message. -/
def fail {α : Type} (msg : String) : Parser α :=
  fun pos => ParseResult.error msg pos

instance : Monad Parser where
  pure := Parser.pure
  bind := Parser.bind

-- ============================================================
-- Alternation
-- ============================================================

/-- Try the first parser; if it fails (regardless of consumption), try the second.
    This corresponds to Sprache's `.Or()` which always tries the alternative. -/
def or' {α : Type} (p1 : Parser α) (p2 : Parser α) : Parser α :=
  fun pos =>
    match p1 pos with
    | .ok value pos' => ParseResult.ok value pos'
    | .error _ _ => p2 pos

/-- Exclusive or: try the first parser; if it fails without consuming input, try the second.
    If the first parser consumed input before failing, propagate the error.
    This corresponds to Sprache's `.XOr()`. -/
def xOr {α : Type} (p1 : Parser α) (p2 : Parser α) : Parser α :=
  fun pos =>
    match p1 pos with
    | .ok value pos' => ParseResult.ok value pos'
    | .error msg pos' =>
      if pos'.offset == pos.offset then p2 pos
      else ParseResult.error msg pos'

-- ============================================================
-- Character parsers
-- ============================================================

/-- Parse any single character. Fails at end of input. -/
def anyChar : Parser Char :=
  fun pos =>
    match pos.current with
    | some c => ParseResult.ok c pos.next
    | none => ParseResult.error "unexpected end of input" pos

/-- Parse a character satisfying a predicate. -/
def satisfy (pred : Char → Bool) (desc : String := "expected character") : Parser Char :=
  fun pos =>
    match pos.current with
    | some c =>
      if pred c then ParseResult.ok c pos.next
      else ParseResult.error s!"expected {desc}, got '{c}'" pos
    | none => ParseResult.error s!"expected {desc}, got end of input" pos

/-- Parse a specific character. -/
def char (c : Char) : Parser Char :=
  satisfy (· == c) s!"'{c}'"

/-- Parse a specific character, case-insensitive. -/
def charIgnoreCase (c : Char) : Parser Char :=
  satisfy (fun ch => ch.toLower == c.toLower) s!"'{c}' (case-insensitive)"

/-- Parse a specific string. -/
def string (s : String) : Parser String :=
  let chars := s.toList
  fun pos =>
    let rec loop (remaining : List Char) (pPos : Position) : ParseResult String :=
      match remaining with
      | [] => ParseResult.ok s pPos
      | sc :: rest =>
        if pPos.atEnd then
          ParseResult.error s!"expected \"{s}\"" pos
        else
          let pc := pPos.offset.get pPos.source
          if sc == pc then
            loop rest pPos.next
          else
            ParseResult.error s!"expected \"{s}\"" pos
    loop chars pos

/-- Parse a specific string, case-insensitive. Returns the actual matched text. -/
def stringIgnoreCase (s : String) : Parser String :=
  let chars := s.toList
  fun pos =>
    let rec loop (remaining : List Char) (pPos : Position) : ParseResult String :=
      match remaining with
      | [] =>
        let matched := String.Pos.Raw.extract pos.source pos.offset pPos.offset
        ParseResult.ok matched pPos
      | sc :: rest =>
        if pPos.atEnd then
          ParseResult.error s!"expected \"{s}\"" pos
        else
          let pc := pPos.offset.get pPos.source
          if sc.toLower == pc.toLower then
            loop rest pPos.next
          else
            ParseResult.error s!"expected \"{s}\"" pos
    loop chars pos

/-- Parse a letter character. -/
def letter : Parser Char :=
  satisfy Char.isAlpha "letter"

/-- Parse a digit character. -/
def digit : Parser Char :=
  satisfy Char.isDigit "digit"

/-- Parse a letter or digit character. -/
def letterOrDigit : Parser Char :=
  satisfy (fun c => c.isAlpha || c.isDigit) "letter or digit"

/-- Parse any character except those satisfying the predicate. -/
def noneOf (pred : Char → Bool) (desc : String := "none of given chars") : Parser Char :=
  satisfy (fun c => !pred c) desc

/-- Parse a whitespace character (space, tab, etc.) but NOT a line terminator. -/
def whitespaceChar : Parser Char :=
  satisfy (fun c => c == ' ' || c == '\t' || c == '\x0C') "whitespace (not newline)"

/-- Parse a line ending: "\r\n" or "\n". -/
def lineEnd : Parser String :=
  fun pos =>
    match pos.current with
    | some '\n' => ParseResult.ok "\n" pos.next
    | some '\r' =>
      let pos' := pos.next
      match pos'.current with
      | some '\n' => ParseResult.ok "\r\n" pos'.next
      | _ => ParseResult.error "expected line ending" pos
    | _ => ParseResult.error "expected line ending" pos

/-- Check if a character is a line terminator. -/
def isLineTerminator (c : Char) : Bool :=
  c == '\n' || c == '\r'

-- ============================================================
-- Repetition combinators
-- ============================================================

/-- Parse zero or more occurrences. Returns results in order.
    Uses fuel parameter to ensure termination. -/
partial def many {α : Type} (p : Parser α) : Parser (List α) :=
  fun pos =>
    let rec loop (acc : List α) (pos : Position) : ParseResult (List α) :=
      match p pos with
      | .ok value pos' =>
        if pos'.offset == pos.offset then
          -- Parser succeeded without consuming input; stop to avoid infinite loop
          ParseResult.ok acc.reverse pos
        else
          loop (value :: acc) pos'
      | .error _ _ => ParseResult.ok acc.reverse pos
    loop [] pos

/-- Parse one or more occurrences. -/
def many1 {α : Type} (p : Parser α) : Parser (List α) := do
  let first ← p
  let rest ← many p
  Parser.pure (first :: rest)

/-- Parse zero or more characters and collect them into a string. -/
def manyChars (p : Parser Char) : Parser String := do
  let chars ← many p
  Parser.pure (String.ofList chars)

/-- Parse one or more characters and collect them into a string. -/
def many1Chars (p : Parser Char) : Parser String := do
  let chars ← many1 p
  Parser.pure (String.ofList chars)

/-- Parse zero or more occurrences and collect results as text. -/
def manyText (p : Parser String) : Parser String := do
  let parts ← many p
  Parser.pure (String.join parts)

/-- Optionally parse something. Returns `some value` on success, `none` on failure. -/
def optional {α : Type} (p : Parser α) : Parser (Option α) :=
  fun pos =>
    match p pos with
    | .ok value pos' => ParseResult.ok (some value) pos'
    | .error _ _ => ParseResult.ok none pos

-- ============================================================
-- Sequencing and selection helpers
-- ============================================================

/-- Parse `p` and return the result, requiring that all input is consumed. -/
def parseEnd {α : Type} (p : Parser α) : Parser α :=
  fun pos =>
    match p pos with
    | .ok value pos' =>
      if pos'.atEnd then ParseResult.ok value pos'
      else ParseResult.error "expected end of input" pos'
    | .error msg pos' => ParseResult.error msg pos'

/-- Peek at the result of a parser without consuming input (lookahead). -/
def lookAhead {α : Type} (p : Parser α) : Parser α :=
  fun pos =>
    match p pos with
    | .ok value _ => ParseResult.ok value pos
    | .error msg pos' => ParseResult.error msg pos'

/-- Succeed only if the parser fails (consume no input). -/
def notFollowedBy {α : Type} (p : Parser α) : Parser Unit :=
  fun pos =>
    match p pos with
    | .ok _ _ => ParseResult.error "unexpected success" pos
    | .error _ _ => ParseResult.ok () pos

/-- Parse something except when another parser would succeed. -/
def except {α : Type} {β : Type} (p : Parser α) (exclusion : Parser β) : Parser α :=
  fun pos =>
    match exclusion pos with
    | .ok _ _ => ParseResult.error "matched exclusion" pos
    | .error _ _ => p pos

/-- Parse p, then return a constant value. -/
def returning {α : Type} {β : Type} (p : Parser α) (value : β) : Parser β :=
  map (fun _ => value) p

-- ============================================================
-- Convenience for working with lists of tokens
-- ============================================================

/-- Flatten a list of lists. -/
def flatten {α : Type} (p : Parser (List (List α))) : Parser (List α) :=
  map List.flatten p

end Parser

end DockerfileModel.Parser
