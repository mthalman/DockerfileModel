import Lake
open Lake DSL

package «DockerfileModel» where
  leanOptions := #[
    ⟨`autoImplicit, false⟩
  ]

@[default_target]
lean_lib «DockerfileModel» where

lean_exe «DockerfileModelTests» where
  root := `DockerfileModel.Tests.SlimCheck
  supportInterpreter := true

lean_exe «DockerfileModelDiffTest» where
  root := `DockerfileModel.Main
  supportInterpreter := true
