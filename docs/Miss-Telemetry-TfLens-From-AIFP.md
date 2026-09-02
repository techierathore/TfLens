# Miss telemetry - TfLens contract from AIFP

**Status:** PLAYBOOK PRODUCER IMPLEMENTED; TFLENS CONSUMER TO IMPLEMENT.
**Audience:** TfLens ingestion, API, analytics and miss/rework UI teams.
**Producer:** AI-First Playbook schema-1 `miss`, `miss-fix` and `miss-amend` records.
**Phase efficiency contract:** [`Phase-Efficiency-TfLens-Contract.md`](Phase-Efficiency-TfLens-Contract.md).

## 1. Input

Run from the target repository root:

```bash
node scripts/playbook-telemetry.mjs --misses
```

The command reads committed `verification/telemetry/misses.ndjson`, folds valid amendments,
joins exact fix windows while transient phase events remain available, and emits normalized NDJSON
on stdout. Diagnostics go to stderr. TfLens ingests exporter stdout, not plugin internals.

## 2. Lifecycle records

- `miss` opens a classified defect or gap.
- `miss-fix` records a repair outcome and may be enriched from an exact phase window.
- `miss-amend` fills an allowed null classification without rewriting history.
- Upsert raw source records by immutable source-line identity/hash and preserve stream order.
- Fold valid amendments before reporting; surface orphan and overwrite diagnostics.

Full producer fields and CLI behavior are in [`Telemetry-Guide.md`](Telemetry-Guide.md) section 7.

## 3. Reporting guards

- Model/tier attribution requires `origin_confidence:"linked"`, a complete valid source window and
  a non-null observed model.
- Never put inferred or unknown origins into an "unknown model" performance bucket.
- Headline fix tokens/cost per miss requires `cost_attribution:"sole"`, a complete valid window
  and `data_quality.cost_status:"complete"`.
- Show `shared:<n>` separately as apportioned; exclude `none`.
- Never mix measured `cost_usd` with rate-card `_usd_estimate` values.
- Apply `FIELD_SINCE` before optional-field denominators and show `n of N assessed`.
- Never group miss, amendment, escape, rework, token, time or cost metrics by `actor`.
- Comparative metrics with fewer than three records are `insufficient data`.

## 4. Required UI

- Lifecycle opened, closed, reopened and backlog counts.
- Miss rate by linked origin phase/model.
- Miss class, `why_missed`, design-miss share and escape share.
- Rework incidence/intensity and median/p90 time to close.
- Sole measured repair tokens/cost and separately apportioned repair cost.
- Attribution exclusions, assessment denominators and amendment/orphan diagnostics.

Cross-edition normalization must preserve distinct axes: Playbook `item_id` versus TechieFlow
`req_id`, and Playbook process `found_phase_gate` versus TechieFlow assertion `found_gate`.

## 5. Acceptance tests

1. Re-import does not duplicate lifecycle records.
2. Amendments fold before `why_missed` distributions.
3. Invalid, orphan and overwrite amendments remain visible diagnostics.
4. `sole`, `shared:<n>` and `none` never enter the same headline cost cohort.
5. Measured and estimated dollars never share a series or total.
6. No UI or API exposes actor-grouped quality, rework, effort, token or cost reporting.
