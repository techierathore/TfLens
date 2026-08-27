# Playbook fixture — SYNTHETIC, not a captured run

`events-synthetic.ndjson` is **hand-written**, not collected. Its name says so, and so does this file,
because the distinction is the whole point of REQ-FN-068: no real
`verification/telemetry/events.ndjson` has ever been available to TfLens (see `DECISIONS.md` S-001 for
where it was looked for).

**What it is faithful to.** Every field name, wire spelling, nesting and record kind here is copied from
the Playbook's own emitter, `harness/opencode/plugin/telemetry.ts` in
`techierathore/AI-First-Playbook@main` — camelCase with capitalised acronyms (`sessionID`, `parentID`,
`messageID`), `tokens` as a nested object, `parentID: null` on a main session, `ts` on every record.

**What it is not evidence of.** Value ranges, token magnitudes, how many turns a real phase produces,
what `command` values actually occur, or how the emitter behaves at the edges. Those are unobserved.
No figure computed from this file may be quoted as a measurement of anything.

## What it deliberately contains

| Line | Why it is there |
|------|-----------------|
| `msg-01` twice | The emitter appends a fresh `turn` on **every** `message.updated`, so a streaming message writes several rows and only the last is complete. The pair proves the `messageID` dedupe (D-011) keeps the larger and does not sum both. |
| `ses-sub-01` with `parentID: "ses-main-01"` | The main-vs-subagent split (BRD-75). |
| A line that is not JSON | A malformed line is counted and skipped, never fatal (REQ-FN-032). |
| Two phases, `verify` and `plan-review` | `phase_gate` is **derived**: the file names the phase only on `phase-start`, and the `turn` and `phase-end` rows that follow inherit it. |
| Two models | Tokens-by-model, the Playbook equivalent of the routing view. |

## Expected figures (asserted by `PlaybookReportBuilderTests`)

Tokens follow the Playbook joiner's own split: cache read and write count as input, reasoning counts as
output.

| Figure | Value |
|--------|-------|
| Rows after dedupe | 7 (of 8 valid lines; `msg-01` collapses) |
| `verify` — events / sessions / tokens / cost | 4 / 2 / 505 / 0.045 |
| `plan-review` — events / sessions / tokens / cost | 3 / 1 / 360 / 0.020 |
| Main sessions / tokens | 2 / 725 |
| Sub-agent sessions / tokens | 1 / 140 |
| Sub-agent token share | 16% |
| `anthropic/claude-sonnet-5` total tokens | 725 |
| `anthropic/claude-haiku-5` total tokens | 140 |
| Three questions | `—` on every gate: the stream carries no verdict (S-001) |
