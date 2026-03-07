/-
  VariableResolution.lean — Formal model of Dockerfile variable resolution.

  This models the C# VariableRefToken.ResolveVariables method (lines 121-196 of
  Tokens/VariableRefToken.cs) and the ResolutionOptions class.

  The six modifier variants defined by Docker:
    :-   colonDash    — use default when unset OR empty
    :+   colonPlus    — use alt when set AND non-empty
    :?   colonQuestion — error when unset OR empty
    -    dash         — use default only when truly unset
    +    plus         — use alt when set (even if empty)
    ?    question     — error only when truly unset

  Colon vs non-colon distinction:
    Colon variants treat a variable as "unset" if NOT in the map OR if value is ""
    Non-colon variants treat a variable as "unset" only if NOT in the map (empty = set)

  We use `List (String × String)` (association list) rather than HashMap because
  association lists are easier to reason about in Lean proofs — we can use
  `List.lookup` and structural induction without requiring HashMap axioms.

  The resolve function returns `Except String String`:
    .error msg  — for ? and :? modifiers when the variable is unset
    .ok value   — resolved string value in all other cases
-/

import DockerfileModel.Token

namespace DockerfileModel

/-- Variable substitution modifier variants.
    The first six are the standard Docker modifiers. The remaining six are
    extended bash-style pattern operations supported by BuildKit. -/
inductive Modifier where
  -- Standard Docker modifiers (colon vs non-colon):
  | colonDash      -- :-   use default when unset or empty
  | colonPlus      -- :+   use alt when set and non-empty
  | colonQuestion  -- :?   error when unset or empty
  | dash           -- -    use default when truly unset
  | plus           -- +    use alt when set (even if empty)
  | question       -- ?    error when truly unset
  -- Extended bash-style pattern modifiers:
  | hashPattern          -- ${var#pattern}  — remove shortest prefix match
  | doubleHashPattern    -- ${var##pattern} — remove longest prefix match
  | percentPattern       -- ${var%pattern}  — remove shortest suffix match
  | doublePercentPattern -- ${var%%pattern} — remove longest suffix match
  | slashPattern         -- ${var/pattern/replacement} — replace first match
  | doubleSlashPattern   -- ${var//pattern/replacement} — replace all matches
  deriving Repr, BEq, Inhabited, DecidableEq

/--
  A parsed variable reference, capturing the three components that drive resolution.

  Corresponds to what VariableRefToken exposes:
    - VariableName    → name
    - Modifier        → modifier (None for bare $VAR or ${VAR})
    - ModifierValue   → modifierValue (None when no modifier present)
-/
structure VariableRef where
  name          : String
  modifier      : Option Modifier
  modifierValue : Option String
  deriving Repr, BEq

/--
  Options controlling resolution behavior.

  Mirrors C# ResolutionOptions:
    - updateInline      → when true, the token tree is modified in place
    - removeEscapeChars → when true, escape character sequences are stripped
-/
structure ResolutionOptions where
  updateInline      : Bool := false
  removeEscapeChars : Bool := false
  deriving Repr, BEq, Inhabited

/--
  Variable map: an association list of (name, value) pairs.

  Using `List (String × String)` because:
  1. No dependency on Std.HashMap required.
  2. `List.lookup` is easy to unfold in proofs.
  3. Structural induction matches how we reason about environments.

  `lookup key vars` returns `some value` if the key exists, `none` if not.
  This corresponds to C# `variables.TryGetValue(name, out value)`.
-/
abbrev VarMap := List (String × String)

/-- Look up a variable in the map (wraps List.lookup). -/
def VarMap.find? (vars : VarMap) (name : String) : Option String :=
  vars.lookup name

/-- Check whether a variable name is present in the map (regardless of its value). -/
def VarMap.contains (vars : VarMap) (name : String) : Bool :=
  (vars.lookup name).isSome

/--
  Determine whether a variable is "set" for resolution purposes.

  This captures the colon vs non-colon distinction from C# lines 141-148:

  For colon modifiers: set iff key is present AND the value is non-empty.
  For non-colon modifiers: set iff key is present (empty string counts as set).
-/
def isVariableSet (vars : VarMap) (name : String) (mod : Modifier) : Bool :=
  match mod with
  | .colonDash | .colonPlus | .colonQuestion =>
      -- Colon variants: must exist AND be non-empty
      match vars.find? name with
      | some v => v ≠ ""
      | none   => false
  | .dash | .plus | .question =>
      -- Non-colon variants: must simply exist
      vars.contains name
  | .hashPattern | .doubleHashPattern | .percentPattern | .doublePercentPattern
  | .slashPattern | .doubleSlashPattern =>
      -- Pattern modifiers: variable presence
      vars.contains name

/-- Remove a literal prefix from a string. Returns the string unchanged if the
    prefix doesn't match. For simple literal patterns only (no glob support). -/
def removePrefix (s : String) (pattern : String) : String :=
  if s.startsWith pattern then
    String.ofList (s.toList.drop pattern.toList.length)
  else s

/-- Remove a literal suffix from a string. Returns the string unchanged if the
    suffix doesn't match. For simple literal patterns only (no glob support). -/
def removeSuffix (s : String) (pattern : String) : String :=
  if s.endsWith pattern then
    String.ofList (s.toList.take (s.toList.length - pattern.toList.length))
  else s

/-- Replace the first occurrence of a literal pattern in a string.
    Returns the string unchanged if the pattern is not found. -/
def replaceFirst (s : String) (pattern : String) (replacement : String) : String :=
  if pattern.isEmpty then s
  else
    let sList := s.toList
    let patList := pattern.toList
    let patLen := patList.length
    let rec scan (before : List Char) (remaining : List Char) : String :=
      match remaining with
      | [] => s  -- pattern not found
      | c :: rest =>
        let candidate := remaining.take patLen
        if candidate == patList then
          String.ofList (before.reverse ++ replacement.toList ++ remaining.drop patLen)
        else
          scan (c :: before) rest
    scan [] sList

/-- Replace all occurrences of a literal pattern in a string.
    Returns the string unchanged if the pattern is not found. -/
def replaceAll (s : String) (pattern : String) (replacement : String) : String :=
  if pattern.isEmpty then s
  else s.replace pattern replacement

/--
  Resolve a variable reference against a variable map.

  Returns `Except String String`:
    - `.error msg` for ? and :? modifiers when the variable is unset
    - `.ok value`  for all other cases

  This models C# VariableRefToken.ResolveVariables (lines 138-178).
  FormatValue (escape character stripping) is modeled separately in `formatValue`.
-/
def resolve (vars : VarMap) (ref : VariableRef) : Except String String :=
  match ref.modifier with
  | none =>
      -- No modifier: bare $VAR or ${VAR} — just look up the value
      .ok (vars.find? ref.name |>.getD "")
  | some mod =>
      let set := isVariableSet vars ref.name mod
      let currentVal := vars.find? ref.name |>.getD ""
      let altVal := ref.modifierValue.getD ""
      match mod with
      | .colonDash | .dash =>
          -- Use default when unset; use variable value when set
          if set then .ok currentVal
          else .ok altVal
      | .colonPlus | .plus =>
          -- Use alt when set; use empty when unset
          if set then .ok altVal
          else .ok ""
      | .colonQuestion | .question =>
          -- Error when unset; return variable value when set
          if set then .ok currentVal
          else .error s!"Variable '{ref.name}' is not set. Error detail: '{altVal}'."
      | .hashPattern =>
          -- ${var#pattern} — remove shortest prefix match (simplified: literal only)
          .ok (removePrefix currentVal altVal)
      | .doubleHashPattern =>
          -- ${var##pattern} — remove longest prefix match (simplified: same as shortest for literals)
          .ok (removePrefix currentVal altVal)
      | .percentPattern =>
          -- ${var%pattern} — remove shortest suffix match (simplified: literal only)
          .ok (removeSuffix currentVal altVal)
      | .doublePercentPattern =>
          -- ${var%%pattern} — remove longest suffix match (simplified: same as shortest for literals)
          .ok (removeSuffix currentVal altVal)
      | .slashPattern =>
          -- ${var/pattern/replacement} — replace first match
          -- modifierValue holds "pattern", ref has a secondary value for replacement
          -- For now, modifierValue is the pattern, and we use "" as replacement
          -- (the parser will need to handle the second value)
          let parts := altVal.splitOn "/"
          let pat := parts.head!
          let repl := if parts.length > 1 then (parts.drop 1 |> String.intercalate "/") else ""
          .ok (replaceFirst currentVal pat repl)
      | .doubleSlashPattern =>
          -- ${var//pattern/replacement} — replace all matches
          let parts := altVal.splitOn "/"
          let pat := parts.head!
          let repl := if parts.length > 1 then (parts.drop 1 |> String.intercalate "/") else ""
          .ok (replaceAll currentVal pat repl)

/--
  Helper: process escape characters in a character list.
  - Double escape char → emit one escape char, advance by 2
  - Single escape before other char → drop the escape char
  - Trailing escape char → drop it
  - Non-escape char → emit it

  This models the inner loop of C# ResolutionOptions.FormatValue.

  We use an index variable `n` (the length of the remaining list) to give
  Lean an explicit well-founded recursion measure that's obvious to check.
-/
private def processEscapes (escapeChar : Char) : List Char → List Char
  | [] => []
  | [c] =>
      -- Single character: emit it unless it is a lone escape char (trailing escape)
      if c == escapeChar then [] else [c]
  | c :: next :: rest =>
      if c == escapeChar then
        if next == escapeChar then
          -- Double escape: emit one escape char, skip both c and next
          escapeChar :: processEscapes escapeChar rest
        else
          -- Single escape before other char: drop the escape char (c),
          -- emit next, and continue processing rest
          next :: processEscapes escapeChar rest
      else
        -- Non-escape char: emit it and process the remainder (next :: rest)
        c :: processEscapes escapeChar (next :: rest)
  termination_by cs => cs.length
  decreasing_by
    all_goals (simp only [List.length_cons]; omega)

/--
  Format a resolved value by stripping escape characters.

  Models C# ResolutionOptions.FormatValue:
  When removeEscapeChars is false, returns the value unchanged.
  When true, processes escape sequences:
  - double escape char → single escape char
  - single escape before other char → dropped
-/
def formatValue (escapeChar : Char) (opts : ResolutionOptions) (value : String) : String :=
  if !opts.removeEscapeChars then value
  else String.ofList (processEscapes escapeChar value.toList)

/--
  Full resolution pipeline: resolve then format.

  This combines `resolve` and `formatValue` to match the complete C# pipeline
  from VariableRefToken.ResolveVariables lines 181-182:
  ```csharp
  value = options.FormatValue(escapeChar, value ?? String.Empty);
  ```
-/
def resolveAndFormat (vars : VarMap) (ref : VariableRef)
    (escapeChar : Char) (opts : ResolutionOptions) : Except String String :=
  match resolve vars ref with
  | .ok v    => .ok (formatValue escapeChar opts v)
  | .error e => .error e

-- ============================================================
-- Helper lemmas about VarMap
-- ============================================================

/-- If a key is not in the map, find? returns none. -/
theorem varMap_find_not_contains (vars : VarMap) (name : String)
    (h : vars.contains name = false) :
    vars.find? name = none := by
  unfold VarMap.find? VarMap.contains at *
  cases hfind : vars.lookup name with
  | none => rfl
  | some v => simp [hfind] at h

/-- If find? returns some value, then contains is true. -/
theorem varMap_contains_of_find (vars : VarMap) (name : String) (val : String)
    (h : vars.find? name = some val) :
    vars.contains name = true := by
  unfold VarMap.find? VarMap.contains at *
  simp [h]

end DockerfileModel
