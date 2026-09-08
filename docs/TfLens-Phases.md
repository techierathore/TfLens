# TfLens — Phases

| | |
|---|---|
| App | TfLens |
| Kind | app |
| Size | Large |
| Date | 2026-09-08 |

TfLens was built as one document set and outgrew it — 181 requirements and 192 checklist rows against
a cap of 100. On 2026-09-08 it was split into the three phases it had always been delivered in.

**Nothing was renumbered.** Ids run on across the phases and are never reused, so a range says which
file holds an item, never when it was written. That is why phases 1 and 2 interleave in the low
numbers and everything added since 2026-08-28 sits in phase 3.

**How a requirement was placed.** It sits in the phase of the checklist row that owns it — the phase
tag those rows have carried since day one. Cited by rows in two phases, because a later phase amended
it, it sits in the **earliest**: that is where it was introduced. Cross-cutting quality requirements
found during build and UAT sit in phase 1, because they applied from the first screen.

## Phases

| Phase | Name | Screens | BRD range | Status |
|---|---|---|---|---|
| 1 | Foundation | Login, Register, Forgot password, Reset password, Repos, Profile | BRD-1 to BRD-20, BRD-22 to BRD-29, BRD-77 to BRD-81, BRD-83 to BRD-83, BRD-85 to BRD-88, BRD-90 to BRD-108, BRD-111 to BRD-111, BRD-142 to BRD-142, BRD-144 to BRD-144, BRD-182 to BRD-188 | done |
| 2 | Reports | Coverage / health, Gate outcomes, Harness comparison, Routing & economics, Snapshot export | BRD-21 to BRD-21, BRD-30 to BRD-72, BRD-82 to BRD-82, BRD-84 to BRD-84, BRD-89 to BRD-89, BRD-143 to BRD-143 | done |
| 3 | Depth | Misses & rework, Phase effort, Playbook framework state of the report pages | BRD-73 to BRD-76, BRD-109 to BRD-110, BRD-112 to BRD-141, BRD-145 to BRD-181 | building |

**Phase 1 — Foundation** is everything needed before a figure can be shown: sign-in, the Repos screen,
the sync, the raw archive, the parser and store, the shell, the container. Nothing here renders a
metric; everything here is what the metrics rest on. 72 of 75 requirements verified.

**Phase 2 — Reports** is the engine, the four report pages, the snapshot export, and the parity check
against `tf-metrics.sh` that is the licence to quote a figure. This is where a wrong number first
becomes possible. 39 of 44 verified; 3 await re-verification after the 2026-09-08 amendment.

**Phase 3 — Depth** is everything added after the first report set shipped, and the largest phase:
misses and rework, phase effort from two producers, the Playbook axis, imported telemetry, and the
2026-09-07 fields that say whose gap a miss was. 51 of 73 verified; 12 not yet built.

| Phase | Requirements | Work list | Screens |
|---|---|---|---|
| Phase 1 | [TfLens-BRD.md](./TfLens-BRD.md) | [TfLens-Checklist.md](./TfLens-Checklist.md) | [TfLens-UIDesign.md](./TfLens-UIDesign.md) |
| Phase 2 | [TfLens-P2-BRD.md](./TfLens-P2-BRD.md) | [TfLens-P2-Checklist.md](./TfLens-P2-Checklist.md) | [TfLens-P2-UIDesign.md](./TfLens-P2-UIDesign.md) |
| Phase 3 | [TfLens-P3-BRD.md](./TfLens-P3-BRD.md) | [TfLens-P3-Checklist.md](./TfLens-P3-Checklist.md) | [TfLens-P3-UIDesign.md](./TfLens-P3-UIDesign.md) |

**Not phased, deliberately.** The architecture, coding standards, developer guide and usage guide
describe one running application, not three. Splitting either of the first two would scatter what a
reader needs in one place. **Phase 1 also carries the whole-project context** — summary, objectives,
scope, stakeholders, diagrams, constraints, the parity procedure, definition of done, success metrics
and risks. Phases 2 and 3 point back at it, so there is one copy of each and it cannot drift.
