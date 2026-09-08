# TfLens — Misses

| | |
|---|---|
| App | TfLens |
| Count | 86 logged: 37 open, 49 fixed, 0 will not fix |
| Source | `docs/metrics/misses.jsonl`, one row per miss record. Rewritten by `tf-misses-md.sh` on every new record. Never edit it: a wrong row is corrected by a new record. |
| Updated | 2026-09-07 |

**Whose gap** answers the four questions of the miss protocol: **the app's spec** did not say it, so the checklist line is fixed; **the framework never said it**, so one requirement line and a check are added; **the check was too weak** (a review, or a script that did not fire), so the check is fixed; **said and ignored**, so the rule becomes a hook or is deleted. **not sorted** means the record predates the sort or nobody has answered yet; `bash .tfcore/utils/tf-emit.sh --amend <miss> sort <spec|unsaid|weak-check|ignored>` completes it.

## Open (37)

| Miss | Found | Whose gap | What went wrong |
|---|---|---|---|
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

## Fixed (49)

| Miss | Found | Closed | Whose gap | What went wrong |
|---|---|---|---|---|
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
