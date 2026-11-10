# TfLens — UI Design — Phase 4: Estimates

| | |
|---|---|
| App | TfLens |
| Kind | app |
| Size | Large |
| Phase | 4 of 4 |
| Date | 2026-11-10 |

This file holds the 1 screen of phase 4; what every phase shares is listed under *Where the rest lives* at the end.

## Screens

### Screen: Estimates (`/estimates`)

**Mockup:** [docs/mockups/estimates.html](./mockups/estimates.html) · Playbook state: [docs/mockups/estimates-playbook.html](./mockups/estimates-playbook.html) · **Role(s):** Owner, Author · **BRD:** BRD-211..BRD-219 · **REQ:** REQ-UI-078..REQ-UI-080 · **Phase 4**

**Layout (one line):** shell, with **Estimates** (icon `scale`) after Phase effort in the sidebar; header with the BRD-210 repo picker; a standing separation note; one phase card per repository, phase and estimating model; collapsed By step and By row under each card.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Repo picker | the BRD-210 `Select` `repo-picker` | session repo choice | cleared by a framework change |
| Separation note | `Alert Variant=Info` `est-separation-note` | estimate and actual are two columns, never one number; UAT not measured | always |
| Phase card | `Card` `est-card`; description `est-card-head` (model · when · rows); `Badge Variant=Warning` `est-retro` | one `EstimatePhase` | retro badge only on a hindsight batch |
| Parts table | `DataTable` `est-parts`: Part · Estimate mid (low–high) · Actual · Runs · Rows; rows `est-part-build`, `-qa`, `-handoff`, `-total`, `-uat`; tokens above, time beneath | `EstimatePart` | `no precedent`; `insufficient data (n=…)`; `Badge` `est-apportioned`; Rows `78/80`; Total badge "no oracle"; UAT `not measured` |
| No-row runs | `TypographyMuted` `est-rowless` | per step, "not shared out" | always, zeros included |
| By step | `Collapsible` `est-by-step` → `DataTable` + Range `Badge` `est-range` | `EstimateStep` | collapsed |
| By row | `Collapsible` `est-by-row` → paged `DataTable` (25), Row · Build · Machine QA · After handoff, basis beneath each estimate | `EstimateRow` | collapsed; no per-row total |
| Repos without | `TypographyMuted` `est-repos-without` | names them | All repos only |
| Empty | `Empty` `est-empty`, "No estimate yet" | — | no estimates; always in Playbook state |

| Field | Type | Required | Validation |
|---|---|---|---|
| Repository | select | no | All repos or a connected repo of the selected framework |

**Dialogs opened here:** none.

**States:** empty: `est-empty` and no figure or `0` anywhere · loading: `Skeleton` in the shape of one card · error: `Alert Variant=Destructive`, no figures · Playbook: the empty card, "The Playbook writes no estimate stream."

**Notes:** nothing on this page can produce a ratio, difference or percentage of estimate to actual (BRD-213). Total = build + machine QA; after handoff never inside. Figures render through `FigureText`, formatted as the oracle prints them (`2.5M`, `6h41m`).

## Where the rest lives

| What | Where |
|---|---|
| UI library, theme and the design system | [phase 1 UI design](./TfLens-UIDesign.md) |
| The click-through flow across every phase | [phase 1 UI design](./TfLens-UIDesign.md) |
| Library gaps — controls TrBlazeUI lacks, logged once for every phase | [TfLens-TrBlazeUI-Feedback.md](./TfLens-TrBlazeUI-Feedback.md) |
| This phase's requirements | [TfLens-P4-BRD.md](./TfLens-P4-BRD.md) |
| This phase's work list | [TfLens-P4-Checklist.md](./TfLens-P4-Checklist.md) |
| Every phase and its screens | [TfLens-Phases.md](./TfLens-Phases.md) |
