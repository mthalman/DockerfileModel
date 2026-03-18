/-
  Parser/Combinators.lean -- Higher-level combinators matching ParseHelper.cs patterns.

  These combinators build on the core Parser monad from Basic.lean and provide
  the higher-level patterns used throughout ParseHelper.cs:
    - sepBy / sepBy1 — parse with separators
    - between — parse between delimiters
    - manyTill — parse until terminator
    - token / tokenReturn — match and optionally return
    - Sprache-compatible helpers
-/

import DockerfileModel.Parser.Basic

namespace DockerfileModel.Parser

open Parser

-- ============================================================
-- Separator-based combinators
-- ============================================================

/-- Parse zero or more occurrences of `p` separated by `sep`. -/
partial def sepBy {α : Type} {β : Type} (p : Parser α) (sep : Parser β) : Parser (List α) :=
  fun pos =>
    match p pos with
    | .error _ _ => ParseResult.ok [] pos
    | .ok first pos' =>
      let rec loop (acc : List α) (pos : Position) : ParseResult (List α) :=
        match sep pos with
        | .error _ _ => ParseResult.ok acc.reverse pos
        | .ok _ pos'' =>
          match p pos'' with
          | .error _ _ => ParseResult.ok acc.reverse pos  -- separator consumed but no element: backtrack
          | .ok value pos''' =>
            if pos'''.offset == pos.offset then ParseResult.ok acc.reverse pos
            else loop (value :: acc) pos'''
      loop [first] pos'

/-- Parse one or more occurrences of `p` separated by `sep`. -/
def sepBy1 {α : Type} {β : Type} (p : Parser α) (sep : Parser β) : Parser (List α) := do
  let result ← sepBy p sep
  if result.isEmpty then Parser.fail "expected at least one element"
  else Parser.pure result

-- ============================================================
-- Bracketing combinators
-- ============================================================

/-- Parse `p` between `open` and `close` delimiters. -/
def between {α : Type} {β : Type} {γ : Type} (open_ : Parser α) (close : Parser β) (p : Parser γ) : Parser γ := do
  let _ ← open_
  let result ← p
  let _ ← close
  Parser.pure result

-- ============================================================
-- Terminator-based combinators
-- ============================================================

/-- Parse `p` until `terminator` succeeds. Returns the list of results from `p`.
    The terminator is consumed. -/
partial def manyTill {α : Type} {β : Type} (p : Parser α) (terminator : Parser β) : Parser (List α) :=
  fun pos =>
    let rec loop (acc : List α) (pos : Position) : ParseResult (List α) :=
      match terminator pos with
      | .ok _ pos' => ParseResult.ok acc.reverse pos'
      | .error _ _ =>
        match p pos with
        | .ok value pos' =>
          if pos'.offset == pos.offset then ParseResult.ok acc.reverse pos
          else loop (value :: acc) pos'
        | .error msg pos' => ParseResult.error msg pos'
    loop [] pos

-- ============================================================
-- Once / AtLeastOnce
-- ============================================================

/-- Parse exactly one occurrence and wrap in a list.
    Corresponds to Sprache's `.Once()`. -/
def once {α : Type} (p : Parser α) : Parser (List α) := do
  let value ← p
  Parser.pure [value]

/-- Parse at least one occurrence.
    Corresponds to Sprache's `.AtLeastOnce()`. -/
def atLeastOnce {α : Type} (p : Parser α) : Parser (List α) :=
  many1 p

-- ============================================================
-- Text collection
-- ============================================================

/-- Parse many characters and collect them into a string.
    Corresponds to Sprache's `.Many().Text()`. -/
def manyText' (p : Parser Char) : Parser String := do
  let chars ← many p
  Parser.pure (String.ofList chars)

-- ============================================================
-- AsEnumerable / Flatten for token list patterns
-- ============================================================

/-- Wrap a single value in a list.
    Corresponds to Sprache's `.AsEnumerable()`. -/
def asEnumerable {α : Type} (p : Parser α) : Parser (List α) := do
  let value ← p
  Parser.pure [value]

/-- Flatten a parsed list of lists.
    Corresponds to Sprache's `.Flatten()`. -/
def flattenList {α : Type} (p : Parser (List (List α))) : Parser (List α) := do
  let lists ← p
  Parser.pure lists.flatten

-- ============================================================
-- Except combinators
-- ============================================================

/-- Parse `p` but fail if `exclusion` would also parse at the same position.
    Corresponds to Sprache's `.Except()`. -/
def exceptParser {α : Type} {β : Type} (p : Parser α) (exclusion : Parser β) : Parser α :=
  except p exclusion

/-- Exclude specific characters from a character parser.
    Corresponds to Sprache's `.ExceptChars()`. -/
def exceptChars (p : Parser Char) (chars : List Char) : Parser Char :=
  satisfy (fun c => !chars.contains c && match p (Position.ofString (String.ofList [c])) with
    | .ok c' _ => c == c'
    | .error _ _ => false) "character not in exclusion list"

/-- Simpler version: parse a char satisfying the original predicate AND not in the exclusion list. -/
def exceptCharsFrom (pred : Char → Bool) (chars : List Char) : Parser Char :=
  satisfy (fun c => pred c && !chars.contains c) "character satisfying predicate and not excluded"

-- ============================================================
-- Optional with default
-- ============================================================

/-- Parse optionally, returning a default on failure. -/
def optionD {α : Type} (p : Parser α) (default_ : α) : Parser α :=
  fun pos =>
    match p pos with
    | .ok value pos' => ParseResult.ok value pos'
    | .error _ _ => ParseResult.ok default_ pos

-- ============================================================
-- End-of-input
-- ============================================================

/-- Succeed only at end of input. -/
def eof : Parser Unit :=
  fun pos =>
    if pos.atEnd then ParseResult.ok () pos
    else ParseResult.error "expected end of input" pos

-- ============================================================
-- Guard / where combinator
-- ============================================================

/-- Apply a predicate to the parsed result; fail if the predicate is false.
    Corresponds to Sprache's `where` clause. -/
def guard {α : Type} (p : Parser α) (pred : α → Bool) (msg : String := "guard failed") : Parser α :=
  fun pos =>
    match p pos with
    | .ok value pos' =>
      if pred value then ParseResult.ok value pos'
      else ParseResult.error msg pos
    | .error msg' pos' => ParseResult.error msg' pos'

end DockerfileModel.Parser
