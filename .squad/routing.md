# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture & design | Ripley | System design, token hierarchy changes, API surface decisions |
| Code review | Ripley | Review PRs, check quality, approve/reject implementations |
| Library implementation | Dallas | New instructions, parser changes, builder API, bug fixes |
| Parser work | Dallas | Sprache combinators, ParseHelper.cs, grammar definitions |
| Token & construct modeling | Dallas | New token types, instruction classes, construct hierarchy |
| Testing | Lambert | Write tests, edge cases, round-trip fidelity verification |
| Test infrastructure | Lambert | TestHelper.cs, test patterns, xUnit configuration |
| Documentation | Ash | README, API examples, XML doc comments, NuGet metadata |
| Developer experience | Ash | Usage examples, guides, ScenarioTests-style demos |
| Scope & priorities | Ripley | What to build next, trade-offs, decisions |
| Session logging | Scribe | Automatic — never needs routing |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Ripley |
| `squad:ripley` | Architecture, review, design tasks | Ripley |
| `squad:dallas` | Implementation and parser work | Dallas |
| `squad:lambert` | Testing and quality work | Lambert |
| `squad:ash` | Documentation and DevRel work | Ash |

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts -> coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." -> fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If Dallas is implementing, spawn Lambert to write test cases simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied, route to that member. Ripley handles all `squad` (base label) triage.
