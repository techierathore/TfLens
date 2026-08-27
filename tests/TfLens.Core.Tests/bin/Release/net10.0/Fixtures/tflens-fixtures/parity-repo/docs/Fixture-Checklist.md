# Fixture Checklist

Not a real checklist. `tf-metrics.sh` derives a repository's `app` name from the single
`docs/*-Checklist.md` it finds, so this file exists solely to make the reference report
`"app": "Fixture"` deterministically instead of falling back to the directory name.

The streams under `docs/metrics/` are the parity fixture set (REQ-FN-054, REQ-FN-061). They are
hand-authored to exercise the cases that are easy to get wrong:

| Case | Where |
|---|---|
| Live and backfilled gate records that must never pool | `gates.jsonl` — `REQ-A-004` / `REQ-A-008` are backfilled |
| Backfill taint excluding a REQ from the live first-pass rate | `REQ-A-004` has both a live and a backfilled record |
| `project_type_inferred` segmenting as `unclassified`, never `app` | `REQ-U-001`, `REQ-U-002` |
| A second `project_type` that must not pool with the first | the `library` records |
| `insufficient data (n=…)` below min n = 3 | the `library` and `unclassified` segments |
| `escaped` as its own row, and an unattributed failure | `REQ-A-005`, `REQ-A-007` |
| Late-added gate `perf` reported as `ran` beside `caught` | `REQ-A-006` |
| All three harnesses plus `harness: null` records | `runs.jsonl`, `gates.jsonl`, `sessions.jsonl` |
| Measured `cost_usd` on OpenCode only | `runs.jsonl` rows 4 and 5 |
| Routing drift (`routed: false`) and a multi-model run | `runs.jsonl` row 2 |
| `tokens_scope: none` excluded from repricing and counted | `runs.jsonl` row 6 |
| An observed model with no rate-card entry | `gpt-5-codex` |
| Duplicate commit `sha` collapsed on read | `commits.jsonl` rows 1 and 3 |

The parent folder is named `tflens-fixtures` so that invoking the reference as
`tf-metrics.sh --rollup tflens-fixtures/parity-repo` from `tests/TfLens.Core.Tests/Fixtures`
makes the reference's repo identifier read as `owner/name`, matching how TfLens names a
repository. That removes a cosmetic difference from the parity output without hiding anything.
