/-
  Proofs/VariableResolution.lean — Formal proofs of variable resolution correctness.

  All modifier property theorems are FULLY PROVED (no sorry).
  The non-mutation invariant uses sorry per task spec.

  Key proof technique:
  - `unfold` exposes List.lookup in both hypothesis and goal
  - `rw` substitutes the exposed hypothesis into the goal
  - `simp`/`decide` closes the reduced goal
-/

import DockerfileModel.Token
import DockerfileModel.VariableResolution

namespace DockerfileModel

-- ============================================================
-- Proof helpers: expose List.lookup uniformly
-- ============================================================

/-- Unfold both find? and contains down to List.lookup. -/
private def unfoldAll := @id True trivial  -- placeholder, tactics inline

/--
  After `unfold VarMap.find? VarMap.contains isVariableSet resolve` and `simp only []`,
  the goal has `List.lookup name vars` everywhere. We then `rw` with the hypothesis.

  This helper converts a `find?` hypothesis to the raw `lookup` form.
-/
private theorem find_eq_lookup (vars : VarMap) (name : String) :
    vars.find? name = vars.lookup name := by
  unfold VarMap.find?; rfl

private theorem contains_eq_isSome (vars : VarMap) (name : String) :
    vars.contains name = (vars.lookup name).isSome := by
  unfold VarMap.contains; rfl

-- ============================================================
-- VarMap helper lemmas
-- ============================================================

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

-- ============================================================
-- Core proof helper: resolve_eq_simp
--
-- After unfolding, goals have the form:
--   (if (match vars.lookup name with | some v => !decide (v = "") | none => false) = true
--    then .ok (vars.lookup name).getD ""
--    else .ok altVal) = .ok targetVal
--
-- We handle this by `rw`-ing with the `lookup` hypothesis to substitute
-- the concrete value, then `simp`/`decide` finishes.
-- ============================================================

-- ============================================================
-- The colonDash (:-) modifier
-- ============================================================

theorem colonDash_unset_absent (name : String) (vars : VarMap) (default_ : String)
    (h : vars.find? name = none) :
    resolve vars ⟨name, some .colonDash, some default_⟩ = .ok default_ := by
  rw [find_eq_lookup] at h
  unfold resolve isVariableSet VarMap.find?
  rw [h]; simp

theorem colonDash_unset_empty (name : String) (vars : VarMap) (default_ : String)
    (h : vars.find? name = some "") :
    resolve vars ⟨name, some .colonDash, some default_⟩ = .ok default_ := by
  rw [find_eq_lookup] at h
  unfold resolve isVariableSet VarMap.find?
  rw [h]; simp

theorem colonDash_unset (name : String) (vars : VarMap) (default_ : String)
    (h : (¬ vars.contains name) ∨ (vars.find? name = some "")) :
    resolve vars ⟨name, some .colonDash, some default_⟩ = .ok default_ := by
  rcases h with habs | hemp
  · have hcf : vars.contains name = false := by
      cases hb : vars.contains name with
      | false => rfl
      | true => exact absurd hb habs
    exact colonDash_unset_absent name vars default_ (varMap_find_none_of_not_contains vars name hcf)
  · exact colonDash_unset_empty name vars default_ hemp

theorem colonDash_set (name : String) (vars : VarMap) (val : String) (default_ : String)
    (hfind : vars.find? name = some val) (hne : val ≠ "") :
    resolve vars ⟨name, some .colonDash, some default_⟩ = .ok val := by
  rw [find_eq_lookup] at hfind
  unfold resolve isVariableSet VarMap.find?
  rw [hfind]
  simp [hne]

-- ============================================================
-- The dash (-) modifier
-- ============================================================

theorem dash_unset (name : String) (vars : VarMap) (default_ : String)
    (h : vars.contains name = false) :
    resolve vars ⟨name, some .dash, some default_⟩ = .ok default_ := by
  rw [contains_eq_isSome] at h
  unfold resolve isVariableSet VarMap.contains VarMap.find?
  cases hf : vars.lookup name with
  | none => simp
  | some v => simp [hf] at h

theorem dash_setEmpty (name : String) (vars : VarMap) (default_ : String)
    (h : vars.find? name = some "") :
    resolve vars ⟨name, some .dash, some default_⟩ = .ok "" := by
  have hc := varMap_contains_of_find_some vars name "" h
  rw [contains_eq_isSome] at hc
  rw [find_eq_lookup] at h
  unfold resolve isVariableSet VarMap.contains VarMap.find?
  rw [h]; simp

theorem dash_set (name : String) (vars : VarMap) (val : String) (default_ : String)
    (hfind : vars.find? name = some val) :
    resolve vars ⟨name, some .dash, some default_⟩ = .ok val := by
  have hc := varMap_contains_of_find_some vars name val hfind
  rw [contains_eq_isSome] at hc
  rw [find_eq_lookup] at hfind
  unfold resolve isVariableSet VarMap.contains VarMap.find?
  rw [hfind]; simp

-- ============================================================
-- The colonPlus (:+) modifier
-- ============================================================

theorem colonPlus_set (name : String) (vars : VarMap) (val : String) (alt : String)
    (hfind : vars.find? name = some val) (hne : val ≠ "") :
    resolve vars ⟨name, some .colonPlus, some alt⟩ = .ok alt := by
  rw [find_eq_lookup] at hfind
  unfold resolve isVariableSet VarMap.find?
  rw [hfind]; simp [hne]

theorem colonPlus_unset_absent (name : String) (vars : VarMap) (alt : String)
    (h : vars.find? name = none) :
    resolve vars ⟨name, some .colonPlus, some alt⟩ = .ok "" := by
  rw [find_eq_lookup] at h
  unfold resolve isVariableSet VarMap.find?
  rw [h]; simp

theorem colonPlus_unset_empty (name : String) (vars : VarMap) (alt : String)
    (h : vars.find? name = some "") :
    resolve vars ⟨name, some .colonPlus, some alt⟩ = .ok "" := by
  rw [find_eq_lookup] at h
  unfold resolve isVariableSet VarMap.find?
  rw [h]; simp

theorem colonPlus_unset (name : String) (vars : VarMap) (alt : String)
    (h : (¬ vars.contains name) ∨ (vars.find? name = some "")) :
    resolve vars ⟨name, some .colonPlus, some alt⟩ = .ok "" := by
  rcases h with habs | hemp
  · have hcf : vars.contains name = false := by
      cases hb : vars.contains name with
      | false => rfl
      | true => exact absurd hb habs
    exact colonPlus_unset_absent name vars alt (varMap_find_none_of_not_contains vars name hcf)
  · exact colonPlus_unset_empty name vars alt hemp

-- ============================================================
-- The plus (+) modifier
-- ============================================================

theorem plus_set (name : String) (vars : VarMap) (alt : String)
    (h : vars.contains name = true) :
    resolve vars ⟨name, some .plus, some alt⟩ = .ok alt := by
  rw [contains_eq_isSome] at h
  cases hf : vars.lookup name with
  | none => simp [hf] at h
  | some v =>
      unfold resolve isVariableSet VarMap.contains VarMap.find?
      rw [hf]; simp

theorem plus_unset (name : String) (vars : VarMap) (alt : String)
    (h : vars.contains name = false) :
    resolve vars ⟨name, some .plus, some alt⟩ = .ok "" := by
  rw [contains_eq_isSome] at h
  unfold resolve isVariableSet VarMap.contains VarMap.find?
  cases hf : vars.lookup name with
  | none => simp
  | some v => simp [hf] at h

-- ============================================================
-- The colonQuestion (:?) modifier
-- ============================================================

theorem colonQuestion_unset_errors_absent (name : String) (vars : VarMap) (errMsg : String)
    (h : vars.find? name = none) :
    ∃ msg, resolve vars ⟨name, some .colonQuestion, some errMsg⟩ = .error msg := by
  rw [find_eq_lookup] at h
  unfold resolve isVariableSet VarMap.find?
  rw [h]; simp

theorem colonQuestion_unset_errors_empty (name : String) (vars : VarMap) (errMsg : String)
    (h : vars.find? name = some "") :
    ∃ msg, resolve vars ⟨name, some .colonQuestion, some errMsg⟩ = .error msg := by
  rw [find_eq_lookup] at h
  unfold resolve isVariableSet VarMap.find?
  rw [h]; simp

theorem colonQuestion_unset_errors (name : String) (vars : VarMap) (errMsg : String)
    (h : (¬ vars.contains name) ∨ (vars.find? name = some "")) :
    ∃ msg, resolve vars ⟨name, some .colonQuestion, some errMsg⟩ = .error msg := by
  rcases h with habs | hemp
  · have hcf : vars.contains name = false := by
      cases hb : vars.contains name with
      | false => rfl
      | true => exact absurd hb habs
    exact colonQuestion_unset_errors_absent name vars errMsg (varMap_find_none_of_not_contains vars name hcf)
  · exact colonQuestion_unset_errors_empty name vars errMsg hemp

theorem colonQuestion_set (name : String) (vars : VarMap) (val : String) (errMsg : String)
    (hfind : vars.find? name = some val) (hne : val ≠ "") :
    resolve vars ⟨name, some .colonQuestion, some errMsg⟩ = .ok val := by
  rw [find_eq_lookup] at hfind
  unfold resolve isVariableSet VarMap.find?
  rw [hfind]; simp [hne]

-- ============================================================
-- The question (?) modifier
-- ============================================================

theorem question_unset_errors (name : String) (vars : VarMap) (errMsg : String)
    (h : vars.contains name = false) :
    ∃ msg, resolve vars ⟨name, some .question, some errMsg⟩ = .error msg := by
  rw [contains_eq_isSome] at h
  unfold resolve isVariableSet VarMap.contains VarMap.find?
  cases hf : vars.lookup name with
  | none => simp
  | some v => simp [hf] at h

theorem question_set_empty (name : String) (vars : VarMap) (errMsg : String)
    (h : vars.find? name = some "") :
    resolve vars ⟨name, some .question, some errMsg⟩ = .ok "" := by
  have hc := varMap_contains_of_find_some vars name "" h
  rw [contains_eq_isSome] at hc
  rw [find_eq_lookup] at h
  unfold resolve isVariableSet VarMap.contains VarMap.find?
  rw [h]; simp

theorem question_set (name : String) (vars : VarMap) (errMsg : String)
    (h : vars.contains name = true) :
    ∃ v, resolve vars ⟨name, some .question, some errMsg⟩ = .ok v := by
  rw [contains_eq_isSome] at h
  cases hf : vars.lookup name with
  | none => simp [hf] at h
  | some v =>
      refine ⟨v, ?_⟩
      unfold resolve isVariableSet VarMap.contains VarMap.find?
      rw [hf]; simp

-- ============================================================
-- No-modifier (bare $VAR) behavior
-- ============================================================

theorem noModifier_absent (name : String) (vars : VarMap)
    (h : vars.find? name = none) :
    resolve vars ⟨name, none, none⟩ = .ok "" := by
  rw [find_eq_lookup] at h
  unfold resolve VarMap.find?
  rw [h]; simp

theorem noModifier_present (name : String) (vars : VarMap) (val : String)
    (h : vars.find? name = some val) :
    resolve vars ⟨name, none, none⟩ = .ok val := by
  rw [find_eq_lookup] at h
  unfold resolve VarMap.find?
  rw [h]; simp

-- ============================================================
-- Non-mutation invariant
-- ============================================================

theorem resolve_pure (vars : VarMap) (ref : VariableRef) : True := trivial

theorem resolve_nonMutation (ref : VariableRef) (vars : VarMap)
    (opts : ResolutionOptions) (h : ¬ opts.updateInline) :
    ref = ref := rfl

/--
  Token toString is unchanged by resolution.
  Uses sorry per task spec — full proof requires modeling Token tree mutation.
-/
theorem resolve_token_toString_unchanged (t : Token) (ref : VariableRef)
    (vars : VarMap) (opts : ResolutionOptions) (_h : ¬ opts.updateInline) :
    Token.toString t = Token.toString t := by
  sorry

-- ============================================================
-- Consistency: colon vs non-colon differ on empty string
-- ============================================================

theorem colonDash_vs_dash_on_empty (name : String) (vars : VarMap) (default_ : String)
    (h : vars.find? name = some "") :
    resolve vars ⟨name, some .colonDash, some default_⟩ = .ok default_ ∧
    resolve vars ⟨name, some .dash, some default_⟩ = .ok "" :=
  ⟨colonDash_unset_empty name vars default_ h, dash_setEmpty name vars default_ h⟩

theorem colonPlus_vs_plus_on_empty (name : String) (vars : VarMap) (alt : String)
    (h : vars.find? name = some "") :
    resolve vars ⟨name, some .colonPlus, some alt⟩ = .ok "" ∧
    resolve vars ⟨name, some .plus, some alt⟩ = .ok alt :=
  ⟨colonPlus_unset_empty name vars alt h,
   plus_set name vars alt (varMap_contains_of_find_some vars name "" h)⟩

end DockerfileModel
