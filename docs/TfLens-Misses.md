# TfLens — Misses

| | |
|---|---|
| App | TfLens |
| Count | 138 logged: 77 open, 61 fixed, 0 will not fix |
| Source | `docs/metrics/misses.jsonl`, one row per miss record. Rewritten by `tf-misses-md.sh` on every new record. Never edit it: a wrong row is corrected by a new record. |
| Updated | 2026-09-15 |

**Whose gap** answers the four questions of the miss protocol: **the app's spec** did not say it, so the checklist line is fixed; **the framework never said it**, so one requirement line and a check are added; **the check was too weak** (a review, or a script that did not fire), so the check is fixed; **said and ignored**, so the rule becomes a hook or is deleted. **not sorted** means the record predates the sort or nobody has answered yet; `bash .tfcore/utils/tf-emit.sh --amend <miss> sort <spec|unsaid|weak-check|ignored>` completes it.

## Open (77)

| Miss | Found | Whose gap | What went wrong |
|---|---|---|---|
| MISS-TfLens-20260913-01 (REQ-UI-006) | 2026-09-13 by owner | the check was too weak | The mockups were drawn without the sidebar rail, even though the UI design document names SidebarRail as part of the shell and that shell is standard across all my applications. Every report screen then failed the mockup comparison, and the failure was reported against the app rather than the drawin |
| MISS-TfLens-20260911-40 (REQ-UI-029) | 2026-09-11 by agent-review | the app's spec | The routing page printed the counterfactual delta as $-305.56 because the specification never said what to show when a mix of models costs more than the all-at-one-model estimate. |
| MISS-TfLens-20260911-39 (REQ-UI-072) | 2026-09-11 by agent-review | the check was too weak | The sidebar showed the repo count badge on Price providers as well as Repos, which the mockup does not draw. |
| MISS-TfLens-20260911-38 (REQ-UI-007) | 2026-09-11 by agent-review | the check was too weak | On a phone the header wraps onto three lines, breadcrumb, framework switch and sync row, where the mockup keeps it to one line and hides the breadcrumb and the synced badge. |
| MISS-TfLens-20260911-37 (REQ-UI-006) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-36 (REQ-UI-072) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-35 (REQ-UI-054) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-34 (REQ-UI-053) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-33 (REQ-UI-052) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-32 (REQ-UI-049) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-31 (REQ-UI-048) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-30 (REQ-UI-047) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-29 (REQ-UI-046) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-28 (REQ-UI-045) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-27 (REQ-UI-038) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-26 (REQ-UI-037) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-25 (REQ-UI-036) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-24 (REQ-UI-035) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-19 (REQ-FN-140) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-18 (REQ-FN-112) | 2026-09-11 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260911-17 (REQ-FN-103) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-16 (REQ-FN-089) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-15 (REQ-UI-054) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-14 (REQ-UI-053) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-13 (REQ-UI-052) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-12 (REQ-UI-049) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-11 (REQ-UI-048) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-10 (REQ-UI-047) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-09 (REQ-UI-046) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-08 (REQ-UI-045) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-07 (REQ-UI-038) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-06 (REQ-UI-037) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-05 (REQ-UI-036) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-04 (REQ-UI-035) | 2026-09-11 by gate | not sorted | no sentence recorded (regression, src) |
| MISS-TfLens-20260911-03 | 2026-09-11 by owner | said and ignored | The report said verify was pending but gave the owner no prompt to run, though the status gate prints the next command in both harness forms for exactly that purpose |
| MISS-TfLens-20260911-02 | 2026-09-11 by owner | the framework never said it | The report told the owner two framework defects were filed but never said which application features depend on them, what breaks, or whether to fix the framework first or work in parallel |
| MISS-TfLens-20260911-01 | 2026-09-11 by owner | said and ignored | The build report was written in jargon the owner had to decode, after the owner had already asked twice for plain English with examples |
| MISS-TfLens-20260910-01 (REQ-FN-112) | 2026-09-10 by gate | the framework never said it | TfLens counts a run-void record as a run and never drops the run it voids, so TechieFlow reports 81 live runs where the reference reports 75 |
| MISS-TfLens-20260909-02 (REQ-UI-039) | 2026-09-09 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260909-01 (REQ-UI-034) | 2026-09-09 by gate | not sorted | no sentence recorded (partial-implementation, src) |
| MISS-TfLens-20260902-10 | 2026-09-02 by owner | not sorted | no sentence recorded (unspecified-gap, config, why: other) |
| MISS-TfLens-20260902-09 | 2026-09-02 by owner | not sorted | no sentence recorded (wrong-behaviour, checklist, why: other) |
| MISS-TfLens-20260902-08 | 2026-09-02 by owner | not sorted | no sentence recorded (partial-implementation, checklist, why: instruction-ignored) |
| MISS-TfLens-20260902-07 (REQ-FN-063) | 2026-09-02 by gate | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260902-04 (REQ-FN-063) | 2026-09-02 by gate | not sorted | no sentence recorded (unspecified-gap, config, why: missing-checklist-item) |
| MISS-TfLens-20260902-01 | 2026-09-02 by owner | not sorted | no sentence recorded (unspecified-gap, config, why: missing-checklist-item) |
| MISS-TfLens-20260901-12 (REQ-FN-105) | 2026-09-01 by agent-review | not sorted | no sentence recorded (unspecified-gap, brd, why: ambiguous-acceptance) |
| MISS-TfLens-20260901-11 (REQ-FN-103) | 2026-09-01 by agent-review | not sorted | no sentence recorded (unspecified-gap, brd, why: dependency-not-declared) |
| MISS-TfLens-20260901-10 (REQ-FN-102) | 2026-09-01 by agent-review | not sorted | no sentence recorded (unspecified-gap, brd, why: ambiguous-acceptance) |
| MISS-TfLens-20260901-09 (REQ-FN-094) | 2026-09-01 by agent-review | not sorted | no sentence recorded (unspecified-gap, brd, why: missing-checklist-item) |
| MISS-TfLens-20260901-08 (REQ-FN-092) | 2026-09-01 by agent-review | not sorted | no sentence recorded (spec-contradiction, brd, why: ambiguous-acceptance) |
| MISS-TfLens-20260901-07 (REQ-FN-090) | 2026-09-01 by agent-review | not sorted | no sentence recorded (spec-contradiction, brd, why: ambiguous-acceptance) |
| MISS-TfLens-20260901-06 (REQ-FN-093) | 2026-09-01 by agent-review | not sorted | no sentence recorded (spec-contradiction, architecture, why: ambiguous-acceptance) |
| MISS-TfLens-20260901-02 | 2026-09-01 by owner | not sorted | no sentence recorded (wrong-behaviour, tests, why: instruction-ignored) |
| MISS-TfLens-20260830-07 | 2026-08-30 by agent-review | not sorted | no sentence recorded (unspecified-gap, architecture, why: missing-checklist-item) |
| MISS-TfLens-20260830-05 (REQ-NFR-020) | 2026-08-30 by owner | not sorted | no sentence recorded (partial-implementation, tests, why: insufficient-verify-method) |
| MISS-TfLens-20260830-04 (REQ-NFR-011) | 2026-08-30 by owner | not sorted | no sentence recorded (partial-implementation, config, why: insufficient-verify-method) |
| MISS-TfLens-20260830-03 (REQ-NFR-020) | 2026-08-30 by agent-review | not sorted | no sentence recorded (unspecified-gap, brd, why: insufficient-verify-method) |
| MISS-TfLens-20260830-02 (REQ-NFR-019) | 2026-08-30 by agent-review | not sorted | no sentence recorded (wrong-behaviour, src, why: insufficient-verify-method) |
| MISS-TfLens-20260830-01 (REQ-UI-027) | 2026-08-30 by agent-review | not sorted | no sentence recorded (unspecified-gap, checklist, why: missing-checklist-item) |
| MISS-TfLens-20260829-29 | 2026-08-29 by agent-review | not sorted | no sentence recorded (unspecified-gap, brd, why: ambiguous-acceptance) |
| MISS-TfLens-20260829-25 (REQ-NFR-020) | 2026-08-29 by gate | not sorted | no sentence recorded (missed-requirement, src) |
| MISS-TfLens-20260829-24 (REQ-NFR-011) | 2026-08-29 by owner | not sorted | no sentence recorded (standards-violation, config, why: insufficient-verify-method) |
| MISS-TfLens-20260829-23 (REQ-NFR-011) | 2026-08-29 by owner | not sorted | no sentence recorded (wrong-behaviour, config, why: missing-checklist-item) |
| MISS-TfLens-20260829-21 (REQ-UI-011) | 2026-08-29 by owner | not sorted | no sentence recorded (spec-contradiction, devguide, why: ambiguous-acceptance) |
| MISS-TfLens-20260829-20 | 2026-08-29 by owner | not sorted | no sentence recorded (unspecified-gap, checklist, why: insufficient-verify-method) |
| MISS-TfLens-20260829-17 (REQ-UI-032) | 2026-08-29 by owner | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-15 (REQ-UI-018) | 2026-08-29 by owner | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-13 (REQ-UI-011) | 2026-08-29 by owner | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-11 (REQ-UI-009) | 2026-08-29 by owner | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-10 (REQ-UI-006) | 2026-08-29 by owner | not sorted | no sentence recorded (wrong-behaviour, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-09 (REQ-UI-005) | 2026-08-29 by owner | not sorted | no sentence recorded (wrong-behaviour, src, why: missing-checklist-item) |
| MISS-TfLens-20260829-08 (REQ-UI-004) | 2026-08-29 by owner | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-06 (REQ-UI-002) | 2026-08-29 by owner | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-01 (REQ-NFR-019) | 2026-08-29 by gate | not sorted | no sentence recorded (unspecified-gap, architecture, why: missing-checklist-item) |
| MISS-TfLens-20260828-25 (REQ-UI-014) | 2026-08-28 by gate | not sorted | no sentence recorded (standards-violation, tests, why: insufficient-verify-method) |
| MISS-TfLens-20260828-21 | 2026-08-28 by owner | not sorted | no sentence recorded (standards-violation, config, why: missing-checklist-item) |

## Fixed (61)

| Miss | Found | Closed | Whose gap | What went wrong |
|---|---|---|---|---|
| MISS-TfLens-20260915-07 | 2026-09-15 by owner | 2026-09-15 by log-miss | the check was too weak | inside *fix-issues the inline verify took the fix's start so its run record swallowed the fix's, and tf-fix-close read one ledger so rows of an earlier scoped verify got Needs re-verify (TfLens TF-052) |
| MISS-TfLens-20260915-06 | 2026-09-15 by owner | 2026-09-15 by log-miss | the check was too weak | tf-triage.py resolved only the appPhase checklist and had no --phase, so with appPhase 3 a Phase 1 row the Phase 3 verify found failing could not be demoted (TfLens TF-051) |
| MISS-TfLens-20260915-05 (REQ-FN-040) | 2026-09-15 by owner | 2026-09-15 by log-miss | said and ignored | After I was reminded, the Deployment Checklist still told the owner in its Who does what table that only four repo-level secrets were needed and never named the GitHub Packages access token for TrBlazeUI. |
| MISS-TfLens-20260915-04 (REQ-UI-072) | 2026-09-15 by owner | 2026-09-15 by fix-issues | the check was too weak | On /prices, an OpenRouter model with only one of its two prices published is saved with the missing price as zero after Refresh (PriceProviders.ReadOpenRouter). |
| MISS-TfLens-20260915-03 (REQ-UI-050) | 2026-09-15 by owner | 2026-09-15 by fix-issues | the check was too weak | On /effort in Playbook view, picking a harness in the Harness filter does not change the table; the row filter never reads the harness choice (PlaybookEffortSurface.razor Matches). |
| MISS-TfLens-20260915-02 (REQ-FN-040) | 2026-09-15 by owner | 2026-09-15 by log-miss | said and ignored | I switched the deploy pipeline to the private GitHub package feed but did not add the steps to create the packages token and save it as a GitHub secret to the Deployment Checklist, so the next deploy would have failed. |
| MISS-TfLens-20260915-01 (REQ-FN-040) | 2026-09-15 by production | 2026-09-15 by log-miss | the check was too weak | The Docker image never included Blazor's own script, so the live site loads its pages but nothing on them responds; blazor.web.js returns 404 in production. |
| MISS-TfLens-20260914-01 (REQ-UI-035) | 2026-09-14 by owner | 2026-09-14 by log-miss | the framework never said it | In the verify, the first mockup comparison ran at the same time as the Misses tests; those tests switch the demo user's saved framework and window size, so the comparison caught a page mid-change and reported 11 false findings at 390 wide. It was re-run on its own and only clean results went into th |
| MISS-TfLens-20260911-23 (REQ-UI-072) | 2026-09-11 by owner | 2026-09-11 by log-miss | the check was too weak | BRD-200 added a Price providers screen, but no screen row, UI design entry or mockup was written, so the page was built and verified with no design to compare it against. |
| MISS-TfLens-20260911-22 (REQ-UI-035) | 2026-09-11 by owner | 2026-09-11 by log-miss | the app's spec | The approved mockups drew a narrow strip of menu icons on a phone, where the UI library the app uses hides the sidebar and slides it out from the menu button instead. |
| MISS-TfLens-20260911-21 (REQ-FN-080) | 2026-09-11 by owner | 2026-09-11 by log-miss | the app's spec | BRD-128 said every rate-card figure's key must end in _usd_estimate, while BRD-195 and BRD-197, added later, named the list-price figures list_usd and cost_list_usd_per_miss. |
| MISS-TfLens-20260911-20 (REQ-FN-074) | 2026-09-11 by owner | 2026-09-11 by log-miss | the app's spec | The architecture's sketch of the MissReview table gave it a surrogate key, a text user id and a date-typed timestamp, unlike every sibling table and unlike the code built from it. |
| MISS-TfLens-20260902-06 (REQ-FN-079) | 2026-09-02 by gate | 2026-09-02 by fix-issues | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260902-05 (REQ-FN-052) | 2026-09-02 by gate | 2026-09-02 by fix-issues | not sorted | no sentence recorded (unspecified-gap, src, why: missing-checklist-item) |
| MISS-TfLens-20260902-03 (REQ-UI-038) | 2026-09-02 by gate | 2026-09-02 by fix-issues | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260902-02 (REQ-UI-048) | 2026-09-02 by gate | 2026-09-02 by fix-issues | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260901-05 (REQ-UI-025) | 2026-09-01 by owner | 2026-09-01 by fix-issues | not sorted | no sentence recorded (spec-contradiction, src, why: ambiguous-acceptance) |
| MISS-TfLens-20260901-04 (REQ-UI-023) | 2026-09-01 by owner | 2026-09-01 by fix-issues | not sorted | no sentence recorded (wrong-behaviour, src, why: insufficient-verify-method) |
| MISS-TfLens-20260901-03 (REQ-UI-018) | 2026-09-01 by gate | 2026-09-02 by fix-issues | not sorted | no sentence recorded (wrong-behaviour, src, why: missing-checklist-item) |
| MISS-TfLens-20260901-01 (REQ-UI-023) | 2026-09-01 by owner | 2026-09-01 by log-miss | not sorted | no sentence recorded (standards-violation, checklist, why: instruction-ignored) |
| MISS-TfLens-20260830-10 (REQ-NFR-015) | 2026-08-30 by owner | 2026-09-01 by fix-issues | not sorted | no sentence recorded (regression, tests, why: insufficient-verify-method) |
| MISS-TfLens-20260830-09 (REQ-UI-022) | 2026-08-30 by gate | 2026-09-01 by fix-issues | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260830-08 (REQ-UI-032) | 2026-08-30 by gate | 2026-09-01 by fix-issues | not sorted | no sentence recorded (wrong-behaviour, src, why: insufficient-verify-method) |
| MISS-TfLens-20260830-06 (REQ-UI-024) | 2026-08-30 by owner | 2026-09-01 by fix-issues | not sorted | no sentence recorded (wrong-behaviour, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-28 (REQ-UI-037) | 2026-08-29 by agent-review | 2026-08-30 by build-phase | not sorted | no sentence recorded (partial-implementation, code, why: insufficient-verify-method) |
| MISS-TfLens-20260829-27 (REQ-UI-027) | 2026-08-29 by agent-review | 2026-08-30 by build-phase | not sorted | no sentence recorded (partial-implementation, code, why: insufficient-verify-method) |
| MISS-TfLens-20260829-26 (REQ-FN-058) | 2026-08-29 by gate | 2026-08-29 by build-phase | not sorted | no sentence recorded (wrong-behaviour, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-22 (REQ-UI-011) | 2026-08-29 by owner | 2026-08-30 by build-phase | not sorted | no sentence recorded (regression, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-19 (REQ-UI-036) | 2026-08-29 by owner | 2026-08-30 by build-phase | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-18 (REQ-UI-033) | 2026-08-29 by owner | 2026-09-01 by fix-issues | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-16 (REQ-UI-023) | 2026-08-29 by owner | 2026-08-30 by build-phase | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-14 (REQ-UI-014) | 2026-08-29 by owner | 2026-08-30 by build-phase | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-12 (REQ-UI-010) | 2026-08-29 by owner | 2026-08-30 by build-phase | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-07 (REQ-UI-003) | 2026-08-29 by owner | 2026-08-30 by build-phase | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-05 (REQ-UI-001) | 2026-08-29 by owner | 2026-08-30 by build-phase | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-04 (REQ-NFR-016) | 2026-08-29 by owner | 2026-08-29 by fix-issues | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260829-03 (REQ-FN-063) | 2026-08-29 by gate | 2026-08-29 by fix-issues | not sorted | no sentence recorded (wrong-behaviour, src, why: ambiguous-acceptance) |
| MISS-TfLens-20260829-02 (REQ-FN-063) | 2026-08-29 by gate | 2026-08-29 by fix-issues | not sorted | no sentence recorded (wrong-behaviour, src, why: insufficient-verify-method) |
| MISS-TfLens-20260828-24 (REQ-UI-044) | 2026-08-28 by owner | 2026-08-28 by fix-issues | not sorted | no sentence recorded (unspecified-gap, uidesign, why: missing-checklist-item) |
| MISS-TfLens-20260828-23 (REQ-NFR-018) | 2026-08-28 by self-smoke | 2026-08-28 by fix-issues | not sorted | no sentence recorded (standards-violation, tests, why: missing-checklist-item) |
| MISS-TfLens-20260828-22 | 2026-08-28 by owner | 2026-08-28 by fix-issues | not sorted | no sentence recorded (unspecified-gap, devguide, why: missing-checklist-item) |
| MISS-TfLens-20260828-20 | 2026-08-28 by owner | 2026-08-28 by fix-issues | not sorted | no sentence recorded (unspecified-gap, brd, why: missing-checklist-item) |
| MISS-TfLens-20260828-19 (REQ-NFR-011) | 2026-08-28 by owner | 2026-08-28 by fix-issues | not sorted | no sentence recorded (partial-implementation, config, why: ambiguous-acceptance) |
| MISS-TfLens-20260828-18 (REQ-UI-011) | 2026-08-28 by owner | 2026-08-28 by fix-issues | not sorted | no sentence recorded (partial-implementation, src, why: insufficient-verify-method) |
| MISS-TfLens-20260828-17 (REQ-UI-001) | 2026-08-28 by owner | 2026-08-28 by fix-issues | not sorted | no sentence recorded (wrong-behaviour, src, why: insufficient-verify-method) |
| MISS-TfLens-20260828-16 | 2026-08-28 by agent-review | 2026-08-28 by handoff-phase | not sorted | no sentence recorded (wrong-behaviour, src, why: other) |
| MISS-TfLens-20260828-15 | 2026-08-28 by agent-review | 2026-08-28 by handoff-phase | not sorted | no sentence recorded (standards-violation, other, why: instruction-ignored) |
| MISS-TfLens-20260828-14 (REQ-UI-006) | 2026-08-28 by agent-review | 2026-08-28 by handoff-phase | not sorted | no sentence recorded (wrong-behaviour, checklist, why: missing-checklist-item) |
| MISS-TfLens-20260828-13 (REQ-FN-042) | 2026-08-28 by gate | 2026-08-28 by fix-issues | not sorted | no sentence recorded (wrong-behaviour, other, why: dependency-not-declared) |
| MISS-TfLens-20260828-12 (REQ-UI-042) | 2026-08-28 by gate | 2026-08-28 by build-phase | not sorted | no sentence recorded (wrong-behaviour, tests, why: ambiguous-acceptance) |
| MISS-TfLens-20260828-11 | 2026-08-28 by agent-review | 2026-08-28 by build-phase | not sorted | no sentence recorded (wrong-behaviour, tests, why: insufficient-verify-method) |
| MISS-TfLens-20260828-10 (REQ-FN-073) | 2026-08-28 by agent-review | 2026-08-28 by build-phase | not sorted | no sentence recorded (wrong-behaviour, architecture) |
| MISS-TfLens-20260828-09 (REQ-UI-037) | 2026-08-28 by agent-review | 2026-08-28 by log-miss | not sorted | no sentence recorded (wrong-behaviour, src, why: insufficient-verify-method) |
| MISS-TfLens-20260828-08 (REQ-FN-077) | 2026-08-28 by agent-review | 2026-08-28 by build-phase | not sorted | no sentence recorded (partial-implementation, src, why: ambiguous-acceptance) |
| MISS-TfLens-20260828-07 (REQ-UI-012) | 2026-08-28 by agent-review | 2026-08-28 by build-phase | not sorted | no sentence recorded (wrong-behaviour, src, why: missing-checklist-item) |
| MISS-TfLens-20260828-06 (REQ-FN-085) | 2026-08-28 by agent-review | 2026-08-28 by build-phase | not sorted | no sentence recorded (partial-implementation, src, why: missing-checklist-item) |
| MISS-TfLens-20260828-05 (REQ-FN-084) | 2026-08-28 by agent-review | 2026-08-28 by build-phase | not sorted | no sentence recorded (partial-implementation, src, why: ambiguous-acceptance) |
| MISS-TfLens-20260828-04 (REQ-FN-087) | 2026-08-28 by agent-review | 2026-08-28 by build-phase | not sorted | no sentence recorded (wrong-behaviour, src, why: instruction-ignored) |
| MISS-TfLens-20260828-03 | 2026-08-28 by agent-review | 2026-08-28 by handoff-phase | not sorted | no sentence recorded (unspecified-gap, config, why: insufficient-verify-method) |
| MISS-TfLens-20260828-02 | 2026-08-28 by owner | 2026-08-28 by fix-issues | not sorted | no sentence recorded (unspecified-gap, tests, why: dependency-not-declared) |
| MISS-TfLens-20260828-01 (REQ-NFR-011) | 2026-08-28 by owner | 2026-08-28 by build-phase | not sorted | no sentence recorded (wrong-behaviour, devguide, why: missing-checklist-item) |
