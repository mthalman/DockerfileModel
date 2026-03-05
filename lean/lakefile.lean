import Lake
open Lake DSL

package «DockerfileModel» where
  leanOptions := #[
    ⟨`autoImplicit, false⟩
  ]

@[default_target]
lean_lib «DockerfileModel» where
