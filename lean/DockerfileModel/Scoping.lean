/-
  Scoping.lean — Variable scoping model for multi-stage Dockerfiles.

  This models the C# Dockerfile.ResolveVariables private method (lines 97-160 of
  Dockerfile.cs) and its helper GetGlobalArgs (lines 162-180).

  Scoping rules:
  1. Global ARGs: ARG instructions before any FROM instruction (stagesView.GlobalArgs).
     These are resolved first, before processing any stage.
  2. Per-stage scope: each stage starts with an empty stageArgs dictionary.
     ARG instructions within a stage add to stageArgs as follows:
       a. If the ARG has no default value AND a global arg exists for that name
          → use the global value
       b. If an override exists for that name (from the caller's variableOverrides)
          → use the override value
       c. Otherwise → resolve the default value against stageArgs so far
  3. FROM instructions are resolved against globalArgs (not stageArgs).
  4. Non-ARG instructions are resolved against stageArgs.

  We model this with plain association lists (VarMap) for tractable proofs.
  The resolution functions are pure — they compute a resolved VarMap rather
  than mutating state.
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Dockerfile
import DockerfileModel.VariableResolution

namespace DockerfileModel

-- ============================================================
-- Stage structure
-- ============================================================

/--
  An ARG declaration: a name with an optional default value.

  Corresponds to ArgDeclaration in C# — each ARG instruction can declare
  multiple args, but we model one declaration at a time.
-/
structure ArgDecl where
  name         : String
  defaultValue : Option String
  deriving Repr, BEq

/--
  A single item in a stage body: either an ARG declaration or a non-ARG instruction.

  This captures the C# per-stage loop:
  ```csharp
  if (instruction is ArgInstruction argInstruction) { ... }
  else { resolvedValue = instruction.ResolveVariables(..., stageArgs, ...); }
  ```
  Note: StageItem must be declared before Stage since Stage references it.
-/
inductive StageItem where
  /-- An ARG instruction with zero or more declarations. -/
  | argDecls (decls : List ArgDecl)
  /-- A non-ARG instruction, represented as a VariableRef to resolve. -/
  | nonArg   (ref : VariableRef)
  deriving Repr, BEq

/--
  A stage in a multi-stage Dockerfile.

  In C#, Stage has:
    - FromInstruction : the FROM instruction that opens this stage
    - Items           : the instructions after FROM (excluding the FROM itself)

  We model the FROM image reference as the string being resolved (the image name),
  and the stage body as a list of items that are either ARG declarations or
  other (non-ARG) instruction tokens.
-/
structure Stage where
  /-- The FROM instruction's image reference (what gets resolved against globalArgs). -/
  fromRef : String
  /-- Ordered list of items in this stage body. Each is either an ARG or a non-ARG. -/
  items   : List StageItem
  deriving Repr, BEq

/--
  Global ARGs are the ARG instructions that appear before the first FROM.
-/
abbrev GlobalArgs := List ArgDecl

-- ============================================================
-- Global ARG resolution
-- ============================================================

/--
  Resolve global ARG declarations into a VarMap.

  Models C# Dockerfile.GetGlobalArgs (lines 162-180):

  ```csharp
  foreach (ArgDeclaration arg in stagesView.GlobalArgs.SelectMany(inst => inst.Args)) {
      if (variables.TryGetValue(arg.Name, out string? overridenValue)) {
          globalArgs.Add(arg.Name, overridenValue);
      } else {
          string? resolvedValue = arg.ValueToken?.ResolveVariables(escapeChar, globalArgs, options);
          globalArgs.Add(arg.Name, resolvedValue);
      }
  }
  ```

  We process one declaration at a time, building up the map left-to-right.
  Each arg either takes its override value or uses its plain default string.
  (In our simplified model, we treat default values as plain strings rather
  than recursively resolving variable references within them.)
-/
def resolveGlobalArgs (decls : GlobalArgs) (overrides : VarMap) : VarMap :=
  decls.foldl (fun acc decl =>
    match overrides.find? decl.name with
    | some overrideVal =>
        -- Override exists: use it directly
        acc ++ [(decl.name, overrideVal)]
    | none =>
        -- No override: use the default value (plain string in our model)
        let resolvedVal := decl.defaultValue.getD ""
        acc ++ [(decl.name, resolvedVal)]
  ) []

-- ============================================================
-- Per-stage ARG resolution
-- ============================================================

/--
  Resolve a single ARG declaration within a stage, accumulating into stageArgs.

  Models the inner loop body of C# Dockerfile.ResolveVariables (lines 132-150):

  ```csharp
  // If this is just an arg declaration and a value has been provided from a global arg or arg override
  if (arg.Value is null && globalArgs.TryGetValue(arg.Name, out string? globalArg)) {
      stageArgs.Add(arg.Name, globalArg);
  }
  // If an arg override exists for this arg
  else if (variableOverrides.TryGetValue(arg.Name, out string? overrideArgValue)) {
      stageArgs.Add(arg.Name, overrideArgValue);
  }
  else {
      string? resolvedArgValue = arg.ValueToken?.ResolveVariables(escapeChar, stageArgs, options);
      stageArgs[arg.Name] = resolvedArgValue;
  }
  ```

  Priority order:
  1. No default AND global arg exists → use global value
  2. Override exists → use override value
  3. Otherwise → use default plain string (or empty string if no default)
-/
def resolveArgDecl (decl : ArgDecl) (globalArgs : VarMap) (overrides : VarMap)
    (stageArgs : VarMap) : VarMap :=
  match decl.defaultValue, globalArgs.find? decl.name with
  | none, some globalVal =>
      -- No default AND global arg exists → use global value (case a)
      stageArgs ++ [(decl.name, globalVal)]
  | _, _ =>
      match overrides.find? decl.name with
      | some overrideVal =>
          -- Override exists → use it (case b)
          stageArgs ++ [(decl.name, overrideVal)]
      | none =>
          -- Use plain default string against (case c)
          let resolvedVal := decl.defaultValue.getD ""
          stageArgs ++ [(decl.name, resolvedVal)]

/--
  Resolve all ARG declarations in a stage body, accumulating stageArgs left-to-right.
-/
def resolveArgDecls (decls : List ArgDecl) (globalArgs : VarMap) (overrides : VarMap)
    (stageArgs : VarMap) : VarMap :=
  decls.foldl (fun acc decl => resolveArgDecl decl globalArgs overrides acc) stageArgs

-- ============================================================
-- Stage resolution
-- ============================================================

/--
  The result of resolving a stage: the resolved FROM reference plus a list
  of resolved non-ARG instruction values.
-/
structure StageResult where
  resolvedFrom  : String
  resolvedItems : List (Except String String)
  deriving Repr

/--
  Resolve all items in a stage.

  Models the per-stage loop in C# Dockerfile.ResolveVariables (lines 117-156):
  1. Resolve the FROM instruction against globalArgs.
  2. Iterate stage items:
     - ARG: accumulate into stageArgs (using resolveArgDecls)
     - Non-ARG: resolve against current stageArgs

  Returns the accumulated stageArgs and resolved instruction values.
-/
def resolveStageItems (items : List StageItem) (globalArgs : VarMap) (overrides : VarMap) :
    VarMap × List (Except String String) :=
  items.foldl (fun (acc : VarMap × List (Except String String)) item =>
    let (stageArgs, results) := acc
    match item with
    | .argDecls decls =>
        let newStageArgs := resolveArgDecls decls globalArgs overrides stageArgs
        (newStageArgs, results)
    | .nonArg ref =>
        let resolved := resolve stageArgs ref
        (stageArgs, results ++ [resolved])
  ) ([], [])

/--
  Resolve a complete stage: FROM + body items.

  FROM is resolved against globalArgs.
  Body items are resolved via resolveStageItems.
-/
def resolveStage (stage : Stage) (globalArgs : VarMap) (overrides : VarMap) : StageResult :=
  -- Resolve FROM against globalArgs: it's a plain image reference string
  let resolvedFrom := globalArgs.find? stage.fromRef |>.getD stage.fromRef
  -- Resolve body items
  let (_, resolvedItems) := resolveStageItems stage.items globalArgs overrides
  { resolvedFrom := resolvedFrom, resolvedItems := resolvedItems }

-- ============================================================
-- Full Dockerfile resolution
-- ============================================================

/--
  The result of resolving a complete Dockerfile.
-/
structure DockerfileResolution where
  globalArgs   : VarMap
  stageResults : List StageResult
  deriving Repr

/--
  Resolve a complete Dockerfile with global ARGs and multiple stages.

  Models the top-level C# Dockerfile.ResolveVariables (lines 97-160):
  1. Resolve global ARGs (ARGs before first FROM).
  2. For each stage: resolve FROM against globalArgs, then body against stageArgs.
-/
def resolveDockerfile (globalArgDecls : GlobalArgs) (stages : List Stage)
    (overrides : VarMap) : DockerfileResolution :=
  let globalArgs := resolveGlobalArgs globalArgDecls overrides
  let stageResults := stages.map (fun stage => resolveStage stage globalArgs overrides)
  { globalArgs := globalArgs, stageResults := stageResults }

-- ============================================================
-- Basic scoping lemmas
-- ============================================================

/--
  Resolving with empty global args and empty overrides:
  a stage arg with a default uses that default.
-/
theorem resolveArgDecl_noGlobal_noOverride_usesDefault (name : String) (defVal : String) :
    let decl : ArgDecl := { name := name, defaultValue := some defVal }
    resolveArgDecl decl [] [] [] = [(name, defVal)] := by
  simp [resolveArgDecl, VarMap.find?, List.lookup]

/--
  Resolving a stage arg with no default and no global: uses empty string.
-/
theorem resolveArgDecl_noGlobal_noOverride_noDefault (name : String) :
    let decl : ArgDecl := { name := name, defaultValue := none }
    resolveArgDecl decl [] [] [] = [(name, "")] := by
  simp [resolveArgDecl, VarMap.find?, List.lookup]

/--
  Override takes priority over default value.
-/
theorem resolveArgDecl_override_wins (name : String) (defVal : String) (overrideVal : String) :
    let decl : ArgDecl := { name := name, defaultValue := some defVal }
    let overrides : VarMap := [(name, overrideVal)]
    resolveArgDecl decl [] overrides [] = [(name, overrideVal)] := by
  simp [resolveArgDecl, VarMap.find?, List.lookup]

/--
  Global arg takes priority when there is no default.
-/
theorem resolveArgDecl_global_wins_when_no_default (name : String) (globalVal : String) :
    let decl : ArgDecl := { name := name, defaultValue := none }
    let globalArgs : VarMap := [(name, globalVal)]
    resolveArgDecl decl globalArgs [] [] = [(name, globalVal)] := by
  simp [resolveArgDecl, VarMap.find?, List.lookup]

/--
  An empty Dockerfile (no global ARGs, no stages) resolves to empty.
-/
theorem resolveDockerfile_empty :
    resolveDockerfile [] [] [] = { globalArgs := [], stageResults := [] } := by
  simp [resolveDockerfile, resolveGlobalArgs]

end DockerfileModel
