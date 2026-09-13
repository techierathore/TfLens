# TfLens — amendment of 2026-09-11: phase documents to their templates, and decisions 2, 4 and 5

| | |
|---|---|
| App | TfLens |
| Date | 2026-09-11 |
| Command | the document amendment, run unattended (YOLO on), started 2026-09-11T13:17:38Z |
| Requirements | none added, none removed, none renumbered; the highest id is still BRD-201 |

## What changed

| # | Change | Documents | Result |
|---|---|---|---|
| 1 | Phase BRDs to the phase template | `TfLens-P2-BRD.md`, `TfLens-P3-BRD.md` | Feature catalog removed. Whatever it said that no screen row or requirement already said is now in the Screens and flow table. Sections now run Summary, Screens and flow, Requirements, Development status, Where the rest lives. |
| 1 | Phase UI designs to the phase template | `TfLens-P2-UIDesign.md`, `TfLens-P3-UIDesign.md` | The pointer sentence is now a *Where the rest lives* table that links to the phase-1 UI design. The *Library gaps* section is gone, and the feedback file now says it is the one gap log for every phase. |
| 2 | Decision 2, option B | `TfLens-Architecture.md` | The MissReview table sketch and its diagram box now match `database/001-schema.sql`: no surrogate key, an integer `UserId`, a text `Ts`, and the cost columns as built. A dated correction note says what was wrong. The rest of the architecture's template drift is left for later, as you chose. |
| 3 | Decision 4, option A | all 20 mockups, phase-1 UI design | Below 768px the sidebar is off the page, and the menu button at the top left slides it out, 18rem wide with its labels, over a dark backdrop. This is what the app and the UI library draw. The icon strip is now a 768px-and-up state only. |
| 4 | Decision 5, option A | `TfLens-P3-BRD.md` BRD-128 | One sentence added: a list price is labelled as a list price, not as an estimate, so the two list-price figures of BRD-195 and BRD-197 keep their names. |
| 5 | Document check and TF-024 | `TfLens-TechieFlow-Feedback.md` | The strict check shows no section finding on the four phase documents: failures went from 265 to 255, and the ten removed are exactly those findings. TF-024 is closed, and the feedback summary now reads 7 fixed upstream and waiting, 21 closed. |

## Where the removed feature catalogs went

The test was your wording: a fact moved only if no screen row or requirement already said it. I counted the phase-1 BRD as already saying something. Its business objectives, its parity section and BRD-108 were not copied, because a phase BRD does not repeat phase 1.

The table gained the template's two missing columns: **Role**, which holds each feature's personas, and **Fields**, which holds each feature's one-line screen description. Other new facts were added to the end of that screen's *What it answers* cell.

| Feature | Moved into the table | Already said, so not copied |
|---|---|---|
| Metrics engine | Gate outcomes row: a field-for-field port of `analyse()`; reads each repo's streams and removes duplicate commits per repo; one result, laid out like the framework's combined report, kept in memory until the next sync or rebuild; live and backfilled blocks per project type, with no total; a REQ left out for backfilled records restarts its live `attempt` at 1. Routing row: the rounding rules. | BRD-30 to BRD-38 (no pooling, the three-record minimum, dollars never pooled, late gates, the fixture test). The step order is in Architecture §7. |
| Coverage | GREEN criteria; the `update-framework.sh` fix and why; the configurable threshold; how many run durations and attempt numbers were worked out rather than read, shown beside the unknown-fields list; in-place refresh; repo kind and linked short SHA (Fields) | BRD-21, BRD-39 to BRD-44, BRD-127, BRD-137, BRD-181 |
| Gate outcomes | the 2026-09-01 rename and the kept id F-3Q, with a link to phase 1's note; the evidence base for B3; the per-type counts line; the collapsible panel | BRD-45 to BRD-50 |
| Harness comparison | the four-way token split and its two sources; Codex reads "not measured"; one bar chart | BRD-51 to BRD-55, BRD-72; B1 is a phase-1 objective |
| Routing & economics | the rounding rules | BRD-56 to BRD-62; the repricing formula is BRD-58 in words; B3 is a phase-1 objective |
| Snapshot export | `snapshot.md` is sectioned like the pages; what `extras` and `parity` carry; one snapshot per framework (Fields); the comparison is key by key because key order and formatting may differ | BRD-63 to BRD-72, BRD-128, BRD-160; the parity principle is phase-1 §13 |
| Misses & rework | the two "still open" tests and why the framework keeps `wont-fix` live; each *whose gap* answer has its own remedy | BRD-112 to BRD-130 and BRD-164 to BRD-176 cover the three record kinds, folding, attribution, cost split and the `sort`, `what` and `review` fields |
| Phase effort | a phase is a `cmd`; the main thread's own output is the run's output tokens less the part its subagents used; the 1-of-13 observation behind fan-out coverage; which times add up and which never do; incomplete rows stay visible; the export's `effort` section | BRD-145 to BRD-163, BRD-168, BRD-169 |
| Playbook as a framework | a new row body: why Playbook gets the full report set; the two ways data arrives; the Playbook's four process-gate names; diagnostics on standard error; path 2 shrinking to nothing; the 2026-08-26 phase decision; the repo count on the switch | BRD-73 to BRD-76, BRD-108 to BRD-110, BRD-153, BRD-165 |

**One row was mislabelled and is fixed.** Phase 3's third screen row was named *Playbook framework state of the report pages*, but it described the Playbook view of Phase effort. That text and its requirements (BRD-156 to BRD-163) moved to the Phase effort row, beside a *Playbook state* mockup link. The third row now holds the Playbook-state content that only the catalog carried, and it matches its entry in the phase-3 UI design.

**The two diagrams per feature and the numbered workflows were not carried.** Every step in them is a requirement already, and the order of work is in the architecture.

## Also changed

- **Phase-1 BRD:** the ten "moved to phase n" feature stubs pointed at a §4 that no longer exists. They now point at the screen row in §2 and the requirements in §3.
- **Phase 3 checklist:** REQ-FN-080 carries BRD-128, so it moved from Verified to *Needs re-verify* with a remark saying what changed. REQ-UI-059 carries BRD-128 too, but it is a marked duplicate and stays N/A. The twelve Misses and Phase effort rows held by decision 4 were already at *Needs re-verify* and are untouched.
- **Misses logged and closed against this run:** MISS-TfLens-20260911-20 (the architecture sketch), -21 (BRD-128 against BRD-195 and BRD-197) and -22 (the phone-width icon strip). Each was two documents saying different things, so each should have been right the first time. None of the 16 older open document misses is about these changes, so none was closed.
- **Decision Request:** decisions 2, 4 and 5 are folded in, so it moved unchanged to `docs/OldDocs/TfLens-Decision-Request-2026-09-11.md`.
- **TF-029, filed, fixed upstream and closed the same day.** A miss logged inside this amendment wrote its run record over the amendment's own time, so the amendment's record was refused; it was repaired with the framework's correction record. The framework fixed the logger within the hour. The follow-up below re-checked it: the logger wrote no record of its own, and the amendment's record was accepted with no correction.

## Added after your question: the Price providers screen

BRD-200 adds a screen, **Price providers** at `/prices`, and the app has had it since 2026-09-11, second in the sidebar under Repos, with both of its rows verified. But no document drew it. The amendment that added BRD-200 skipped the step that writes a screen row and a mockup, and my first pass today rebuilt the phase 3 screens table without noticing. The document check had flagged that REQ-UI-072 had no mockup link, but it carried that as an old finding that did not block.

Now fixed:

- **A mockup**, `docs/mockups/prices.html`, drawn with the shared frame and the app's real provider list: four provider cards, each with its source link, an endpoint-or-typed badge, the last-checked date, Refresh (OpenRouter only) and Remove, and its rates in a paged table. Below them sit the Add a provider and Add or change a rate forms. It was checked at 1280px and 390px, and nothing runs off the screen.
- **The sidebar item** "Price providers" in all 17 mockups with the app frame, where the app has it.
- **A screen row** in the phase 3 BRD, an **entry** in the phase 3 UI design, the phase map's screen list, and phase 1's navigation line and click-through diagram.
- **BRD-5** amended in place: nine sidebar items, Price providers second.
- **Checklist:** REQ-UI-072 now links its mockup and, with REQ-UI-006 (the sidebar order), moved from Verified to *Needs re-verify*, because each can now be compared against a design it never had.

This is logged as MISS-TfLens-20260911-23, found by you and closed by this run. The check that should have stopped it is the framework's, so it is filed upstream:

| Problem | What it affects | Does it block or break anything |
|---|---|---|
| TF-030 — the document check never sees a screen that is named only inside a requirement, and its one related finding does not block | A screen added by a later amendment can be built with no design, as Price providers was | No. The screen can be drawn afterwards, as it was here |

To get it fixed, paste this in the framework's own window:

```
Fix TF-030 from docs/TfLens-TechieFlow-Feedback.md in the TfLens repo.
```

**Where the build differs from the new design.** The next verify will show two differences. The app lists every rate in a plain table, 434 rows for OpenRouter, where the design pages them in the library's data table. The app's rate form uses a plain browser select where the design uses the library's select. Both are the build's to change, not the design's.

## Found, not changed

| What | Why it was left |
|---|---|
| Architecture §4 (and a guarantee list further down) names a `ReviewCost(int Corrections, Figure ProducedCost, Figure CorrectionCost)` result type; no type of that name exists in the code | Not part of the table sketch you asked me to correct; it belongs to the later architecture pass. |
| 204 findings from the normal document check across the project (phase-1 BRD, architecture, phase-1 UI design, the mockups' inert buttons, older feedback files) | The template drift you chose to leave for later. None was introduced here. The phase-1 UI design's *Design system* section grew by about 50 words for the phone-width note, and it was already over its limit. |
| Between 768px and 900px the mockups still draw the icon strip, where the app shows the full sidebar until it is collapsed | Decision 4 was about phone width only, and screens are graded at 390px and 1280px. |

## Next

Fifteen rows are graded again: the twelve Misses and Phase effort rows and the sidebar order (REQ-UI-006) against the updated mockups, Price providers (REQ-UI-072) against its new mockup, and REQ-FN-080 against the amended BRD-128:

```
*verify TfLens REQ-UI-006,REQ-UI-035,REQ-UI-036,REQ-UI-037,REQ-UI-038,REQ-UI-045,REQ-UI-046,REQ-UI-047,REQ-UI-048,REQ-UI-049,REQ-UI-052,REQ-UI-053,REQ-UI-054,REQ-UI-072,REQ-FN-080
```
