### 2026-03-08T00:00:00Z: User directive
**By:** Matt Thalman (via Copilot)
**What:** ONBUILD recursive parsing is CORRECT. BuildKit's `parseSubCommand` in `line_parsers.go` calls `newNodeFromLine()` which runs the full parser dispatch on the inner instruction, producing a recursively parsed child Node. The C# behavior (recursive parsing into a full Instruction token tree) matches BuildKit. The Lean spec's opaque literal treatment is the bug — Lean should recursively parse the trigger instruction. Do not file issues saying C# should treat ONBUILD trigger text as opaque. Issues #187, #195, #244 were all closed for this reason.
**Why:** User request — verified against BuildKit source (`moby/buildkit/frontend/dockerfile/parser/line_parsers.go`)
