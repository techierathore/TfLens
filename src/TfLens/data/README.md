# TfLens data folder

Runtime state only. Nothing here is committed (`/data/` is gitignored) and nothing here is
derived data that TfLens depends on — every figure is recomputed from the stream tables at
request time (REQ-FN-046).

| Path | What it is | Who edits it |
|---|---|---|
| `raw/<userId>/` | The verbatim JSONL archive, written before parsing. A rebuild replays it. | TfLens only |
| `reports/<userId>/<date>/<framework>/` | `snapshot.md` + `tflens.json` for one export. | TfLens only |
| `prices.json` | **The rate card. YOU edit this.** | The operator |
| `parity-last.json` | The record of the last passing parity run. Written by `tools/parity-compare.py --record`. | The parity procedure |

## `prices.json` — a rate card, not a bill

`prices.json` is the one editable input in the product (ADR-009). It lists, per model, the USD
rate per 1,000,000 input / output / cache-read / cache-write tokens.

**Everything TfLens computes from it is an estimate — tokens × rate card, not measured spend.**
That wording (SCHEMA.md §4, BRD-59) appears beside every figure derived from this file, on the
Routing & economics page, in `snapshot.md`, and in `tflens.json`, where each such value's key
ends in `_usd_estimate`. Nobody was billed these amounts.

The only *measured* dollars in TfLens are `cost_usd` on OpenCode records, which the harness
itself reports. They never come from this file and are never totalled with anything from it.

Edit the rates to match what you actually pay. Delete a model to drop it from the estimate —
TfLens will then list it as an unpriced observed model rather than quietly pricing it at zero.
Add a model by copying a block and changing its key; the key is the model id as the telemetry
observed it (a `provider/model` id also matches a bare `model` line).
