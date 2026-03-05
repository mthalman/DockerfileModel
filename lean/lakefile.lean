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
