### 2026-03-08T00:00:00Z: User directive
**By:** Matt Thalman (via Copilot)
**What:** Shell form whitespace splitting (C# uses single StringToken vs Lean splitting by whitespace) is BY DESIGN. BuildKit does not split shell form command text into separate whitespace tokens at the parser layer. The C# behavior is correct. Do not log issues for this behavior. Issue #243 was closed as duplicate of #190 for this reason.
**Why:** User request — captured for team memory
