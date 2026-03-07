# Skill: Lean 4 Association List (VarMap) Proofs

**Category:** Lean 4 / Formal Verification
**Discovered:** 2026-03-05 (Phase 4 Variable Resolution)
**Author:** Dallas

## Problem

Proving properties about functions that look up values in a dictionary/map in Lean 4.

## Solution: Use Association Lists, Not HashMap

Define the map as:
```lean
abbrev VarMap := List (String × String)

def VarMap.find? (vars : VarMap) (name : String) : Option String :=
  vars.lookup name

def VarMap.contains (vars : VarMap) (name : String) : Bool :=
  (vars.lookup name).isSome
```

### Why Association Lists?
- `List.lookup` unfolds completely (no axioms needed)
- `simp` knows how to work with `List.lookup` on concrete values
- Structural induction is straightforward
- No Std library dependency

## Key Proof Pattern: `unfold` + `rw` NOT `simp`

**Problem:** `simp [VarMap.find?, h]` often fails because:
- `VarMap.find?` unfolds to `vars.lookup name` (dot-method notation)
- Goal may contain `List.lookup name vars` (function notation)
- These are definitionally equal but `simp` may not unify them transparently

**Solution:** Use bridge lemmas + explicit `rw`:

```lean
-- Bridge lemma (add once per file):
private theorem find_eq_lookup (vars : VarMap) (name : String) :
    vars.find? name = vars.lookup name := by
  unfold VarMap.find?; rfl

private theorem contains_eq_isSome (vars : VarMap) (name : String) :
    vars.contains name = (vars.lookup name).isSome := by
  unfold VarMap.contains; rfl

-- Theorem proof pattern:
theorem my_theorem (vars : VarMap) (name val : String) (h : vars.find? name = some val) :
    someFunction vars name = targetResult := by
  rw [find_eq_lookup] at h          -- convert h to List.lookup form
  unfold someFunction VarMap.find?  -- expose List.lookup in goal
  rw [h]                            -- substitute the concrete lookup result
  simp                              -- finish (now goal is concrete)
```

## Termination for List-Processing Helpers

When writing a recursive function over `List Char` that consumes 2 chars at once:

```lean
-- WRONG: termination checker can't see rest.length < (c :: next :: rest).length
private def processEscapes (esc : Char) : List Char → List Char
  | [] => []
  | c :: rest => processEscapes esc rest  -- fails

-- RIGHT: three-constructor pattern makes decrease visible
private def processEscapes (esc : Char) : List Char → List Char
  | [] => []
  | [c] => if c == esc then [] else [c]
  | c :: next :: rest =>
      if c == esc then
        if next == esc then esc :: processEscapes esc rest      -- rest.length < rest.length + 2
        else next :: processEscapes esc rest                    -- rest.length < rest.length + 2
      else c :: processEscapes esc (next :: rest)               -- rest.length + 1 < rest.length + 2
  termination_by cs => cs.length
  decreasing_by all_goals (simp only [List.length_cons]; omega)
```

## Handling `¬(vars.contains name)` in Proofs

After `rcases h with habs | hemp`, if `habs : ¬vars.contains name`, convert to `Bool = false`:

```lean
have hcf : vars.contains name = false := by
  cases hb : vars.contains name with
  | false => rfl
  | true => exact absurd hb habs
```

Then use `varMap_find_none_of_not_contains vars name hcf`.

## Template: VarMap Helper Lemmas

Copy these lemmas at the top of any proof file using VarMap:

```lean
theorem varMap_contains_of_find_some (vars : VarMap) (name : String) (val : String)
    (h : vars.find? name = some val) :
    vars.contains name = true := by
  rw [contains_eq_isSome, find_eq_lookup] at *
  rw [h]; simp

theorem varMap_find_none_of_not_contains (vars : VarMap) (name : String)
    (h : vars.contains name = false) :
    vars.find? name = none := by
  rw [contains_eq_isSome] at h
  unfold VarMap.find?
  cases hf : vars.lookup name with
  | none => rfl
  | some v => simp [hf] at h
```
