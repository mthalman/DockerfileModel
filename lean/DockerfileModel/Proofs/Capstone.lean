/-
  Proofs/Capstone.lean — Phase 5 Capstone: Full Round-Trip + Mutation Isolation.

  This file is the formal capstone of the Valleysoft.DockerfileModel verification
  project. It delivers two major theorems and their supporting infrastructure:

  1. **Full Round-Trip Compositional Theorem**
     If each construct in a Dockerfile independently reproduces its portion of the
     input text, then the full `Dockerfile.toString` reconstructs the original text.
     This is the compositional structure theorem: it composes `dockerfile_toString_concat`
     (from TokenConcat.lean) with per-construct round-trip obligations.

     The per-instruction round-trip obligations remain sorry'd in RoundTrip.lean —
     they require deep parser monad correctness properties. This is the standard
     approach: prove the compositional structure, leave per-parser lemmas as
     named obligations.

  2. **Mutation Isolation Theorem**
     Modifying construct at index i in a Dockerfile does not affect construct at
     index j (i ≠ j). This follows from list ownership: a Dockerfile is a
     `List DockerfileConstruct`, and `List.set` only changes the targeted index.
     The corollary is that `DockerfileConstruct.toString` of the unchanged construct
     is unaffected.

  Proof techniques:
  - `List.getElem_set_ne` for mutation isolation (stdlib, no Mathlib needed)
  - `List.ext_getElem` for extensional equality of mapped lists
  - `foldl_add_shift` helper for `token_concat_length` length arithmetic
  - `string_join_length_eq_foldl` bridges `String.join` to foldl-sum
-/

import DockerfileModel.Token
import DockerfileModel.Dockerfile
import DockerfileModel.Proofs.TokenConcat

namespace DockerfileModel

-- ============================================================
-- Section 1: Supporting arithmetic lemma for length proofs
-- ============================================================

/-- `foldl (· + ·)` with initial accumulator `k + acc` equals
    `k + foldl (· + ·) acc`. This is the key arithmetic lemma
    that lets us split a foldl-sum's accumulator. -/
private theorem foldl_add_shift (ns : List Nat) (k acc : Nat) :
    (ns.foldl (· + ·) (k + acc)) = k + (ns.foldl (· + ·) acc) := by
  induction ns generalizing acc with
  | nil => simp
  | cons n rest ih =>
    simp only [List.foldl]
    rw [show k + acc + n = k + (acc + n) by omega]
    exact ih (acc + n)

/-- The length of `String.join ss` equals the foldl-sum of the lengths of `ss`.
    This bridges `String.join` (which uses foldl internally with a string
    accumulator) to a purely arithmetic foldl-sum over Nat.

    Proof strategy:
    1. Unfold `String.join` to expose the underlying foldl.
    2. Prove a generalized version (parameterized by initial accumulator `acc`).
    3. Instantiate with `acc = ""`, simplify, and close.
-/
private theorem string_join_length_eq_foldl (ss : List String) :
    (String.join ss).length = (ss.map String.length).foldl (· + ·) 0 := by
  unfold String.join
  -- Generalize: prove for any initial string accumulator
  have gen : ∀ (acc : String),
      (ss.foldl (· ++ ·) acc).length = acc.length + (ss.map String.length).foldl (· + ·) 0 := by
    induction ss with
    | nil => intro acc; simp [List.foldl]
    | cons s rest ih =>
      intro acc
      simp only [List.foldl, List.map]
      rw [ih (acc ++ s), String.length_append]
      -- After rw, the goal has `foldl (·+·) (0 + s.length) rest_lengths` on the right.
      -- Use foldl_add_shift to extract s.length from the accumulator position.
      have key : (List.map String.length rest).foldl (· + ·) (0 + s.length) =
                 s.length + (List.map String.length rest).foldl (· + ·) 0 := by
        rw [Nat.zero_add]
        exact foldl_add_shift _ s.length 0
      rw [key]; omega
  -- Instantiate with the empty string accumulator (the real String.join initial value)
  have h := gen ""
  simp at h
  exact h

-- ============================================================
-- Section 2: Fix for token_concat_length (replaces sorry)
-- ============================================================

/-- **Proved version**: Concatenating token toString values preserves total length.
    No characters are lost or added during the token → string mapping.

    This fixes the sorry in RoundTrip.lean by routing through
    `string_join_length_eq_foldl` and `List.map_map` to match the form.

    The key insight: `String.join (tokens.map f)` length equals the foldl-sum
    of `(f t).length` for each token `t`, which is exactly what this states.
-/
theorem token_concat_length_proved (tokens : List Token) :
    (String.join (tokens.map Token.toString)).length =
    (tokens.map (fun t => (Token.toString t).length)).foldl (· + ·) 0 := by
  have h := string_join_length_eq_foldl (tokens.map Token.toString)
  simp [List.map_map] at h ⊢
  exact h

-- ============================================================
-- Section 3: Full Round-Trip Compositional Theorem
-- ============================================================

/-- **Core round-trip obligation for a single construct**.
    States that construct `c` faithfully reproduces the substring `seg`.
    This is the per-construct obligation that callers must discharge.

    In the complete proof (when per-parser correctness is established),
    each instruction parser would produce a proof of this form. -/
def ConstructRoundTrip (c : DockerfileConstruct) (seg : String) : Prop :=
  DockerfileConstruct.toString c = seg

/-- **construct_roundTrip factor lemma**.
    If every construct at each index independently reproduces its segment,
    then the list of constructs maps toString to exactly the list of segments.

    This is the inductive step that lets us lift per-construct proofs
    to a full list equality. -/
theorem constructs_map_toString_eq_segments
    (items : List DockerfileConstruct)
    (segments : List String)
    (h_len : items.length = segments.length)
    (h_each : ∀ (i : Nat) (hi : i < items.length),
        ConstructRoundTrip (items[i]'hi) (segments[i]'(h_len ▸ hi))) :
    items.map DockerfileConstruct.toString = segments := by
  apply List.ext_getElem
  · simp [h_len]
  · intro i hi1 hi2
    simp [List.getElem_map]
    exact h_each i (by simp at hi1; exact hi1)

/-- **Full Round-Trip Compositional Theorem** (the capstone).

    If:
    - `items` is a list of Dockerfile constructs
    - `segments` is a corresponding list of strings (one per construct)
    - The segments are in 1-1 correspondence with the items (same length)
    - Each item independently round-trips its segment (ConstructRoundTrip)
    - The segments concatenate to `text`

    Then: `Dockerfile.toString { items := items } = text`.

    **Why this is the right theorem:**
    `dockerfile_toString_concat` (proved in TokenConcat.lean) gives us that
    `Dockerfile.toString df = String.join (df.items.map DockerfileConstruct.toString)`.
    This theorem composes that with the per-construct obligations to get the
    full text.

    **What remains to discharge:**
    The `h_each` hypothesis — that each construct independently round-trips.
    In Phase 2, `fromInstruction_roundTrip` and `argInstruction_roundTrip`
    in RoundTrip.lean are sorry'd because per-parser correctness (showing
    every consumed character ends up in exactly one token) is a deep
    metatheoretic result. This theorem is correct modulo those obligations.
-/
theorem dockerfile_roundTrip_compositional
    (items : List DockerfileConstruct)
    (segments : List String)
    (h_len : items.length = segments.length)
    (h_each : ∀ (i : Nat) (hi : i < items.length),
        ConstructRoundTrip (items[i]'hi) (segments[i]'(h_len ▸ hi)))
    (text : String)
    (h_text : String.join segments = text) :
    Dockerfile.toString { items := items } = text := by
  -- Step 1: Use dockerfile_toString_concat to unfold Dockerfile.toString
  rw [← h_text, dockerfile_toString_concat]
  -- Step 2: Show that mapping toString over items gives segments
  -- (using the per-construct round-trip hypotheses)
  congr 1
  exact constructs_map_toString_eq_segments items segments h_len h_each

/-- **Corollary**: When a Dockerfile parses completely, its toString reconstructs
    the original text, PROVIDED each construct round-trips.

    This is stated as a direct consequence of `dockerfile_roundTrip_compositional`,
    using the same per-construct obligation structure. It shows the theorem
    is "ready" — the compositional structure is complete; only the per-parser
    lemmas (in RoundTrip.lean) remain as proof obligations. -/
theorem dockerfile_fullRoundTrip_modulo_perConstruct
    (text : String)
    (items : List DockerfileConstruct)
    (segments : List String)
    (h_len : items.length = segments.length)
    (h_each : ∀ (i : Nat) (hi : i < items.length),
        ConstructRoundTrip (items[i]'hi) (segments[i]'(h_len ▸ hi)))
    (h_text : String.join segments = text) :
    Dockerfile.toString { items := items } = text :=
  dockerfile_roundTrip_compositional items segments h_len h_each text h_text

-- ============================================================
-- Section 4: Mutation Isolation Theorem
-- ============================================================

/-- **Helper**: After `List.set i newItem`, index `j` (j ≠ i) is still in bounds.

    This is a trivial consequence of `List.length_set` but needed for the
    bound argument in the mutation theorems. -/
theorem set_preserves_length_bound (items : List DockerfileConstruct) (i j : Nat)
    (hj : j < items.length) (newItem : DockerfileConstruct) :
    j < (items.set i newItem).length := by
  simp [List.length_set]
  exact hj

/-- **Mutation Isolation Theorem** (the central safety property).

    Modifying construct at index `i` in a Dockerfile leaves all other constructs
    (at index `j`, where `j ≠ i`) completely unchanged.

    **Proof:** Pure list theory via `List.getElem_set_ne` from the Lean 4 stdlib.
    No Mathlib needed. The proof is a one-liner because list ownership is
    exactly the right abstraction: `List.set` only touches the targeted cell.

    **Significance:** This is the formal guarantee that the C# library's
    structural separation between instructions is sound — editing one instruction's
    tokens cannot accidentally corrupt another instruction's tokens. -/
theorem mutation_isolation (items : List DockerfileConstruct) (i j : Nat)
    (_hi : i < items.length) (hj : j < items.length) (hij : i ≠ j)
    (newItem : DockerfileConstruct) :
    (items.set i newItem)[j]'(set_preserves_length_bound items i j hj newItem) =
    items[j]'hj := by
  exact List.getElem_set_ne hij _

/-- **Mutation Isolation Corollary**: toString of construct j is unaffected.

    If we replace construct at index `i` with a new construct,
    the string representation of construct at index `j ≠ i` is unchanged.

    **Why this matters:** This is the key property that makes Dockerfile
    editing compositional — you can replace instruction i's token tree
    without affecting the string representation of any other instruction.
    The token model's isolation property follows directly from list structure. -/
theorem mutation_preserves_toString (items : List DockerfileConstruct) (i j : Nat)
    (hi : i < items.length) (hj : j < items.length) (hij : i ≠ j)
    (newItem : DockerfileConstruct) :
    let hj' := set_preserves_length_bound items i j hj newItem
    DockerfileConstruct.toString ((items.set i newItem)[j]'hj') =
    DockerfileConstruct.toString (items[j]'hj) := by
  simp only
  congr 1
  exact mutation_isolation items i j hi hj hij newItem

-- Note: `_hi` in `mutation_isolation` matches Lean's underscore convention for
-- unused-but-documented parameters. The bound `i < items.length` is not needed
-- by `List.getElem_set_ne` but is part of the logical signature for clarity.

/-- **Mutation isolation at the Dockerfile level**.

    When we replace one construct in a Dockerfile (by updating `items` at index `i`),
    the string representation of ANY other position is unaffected.

    This lifts `mutation_preserves_toString` to work directly on
    `Dockerfile.toString`-level reasoning: the full Dockerfile text changes
    only in the segment corresponding to construct `i`. -/
theorem mutation_isolation_dockerfile (df : Dockerfile) (i j : Nat)
    (hi : i < df.items.length) (hj : j < df.items.length) (hij : i ≠ j)
    (newItem : DockerfileConstruct) :
    let newItems := df.items.set i newItem
    let hj' := set_preserves_length_bound df.items i j hj newItem
    DockerfileConstruct.toString (newItems[j]'hj') =
    DockerfileConstruct.toString (df.items[j]'hj) :=
  mutation_preserves_toString df.items i j hi hj hij newItem

-- ============================================================
-- Section 5: Interplay — Mutation Isolation + Round-Trip
-- ============================================================

/-- **Combined theorem**: After a mutation at index i, the round-trip of construct j
    (j ≠ i) is still intact.

    If construct j originally round-tripped segment s (i.e., its toString = s),
    then after mutating construct i, construct j still round-trips s.

    This is the key compositional property: the round-trip theorem is
    "mutation-stable" for unchanged constructs. -/
theorem mutation_preserves_roundTrip (items : List DockerfileConstruct)
    (i j : Nat) (hi : i < items.length) (hj : j < items.length) (hij : i ≠ j)
    (newItem : DockerfileConstruct) (s : String)
    (h_rt : ConstructRoundTrip (items[j]'hj) s) :
    ConstructRoundTrip
      ((items.set i newItem)[j]'(set_preserves_length_bound items i j hj newItem)) s := by
  unfold ConstructRoundTrip at *
  rw [mutation_isolation items i j hi hj hij newItem]
  exact h_rt

end DockerfileModel
