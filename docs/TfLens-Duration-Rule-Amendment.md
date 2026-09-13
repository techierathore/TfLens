# TfLens — the duration rule changed (amendment of 2026-09-10)

*Written by the analyst during `*amend-docs TfLens`. This is the change-set: what the
reference decided, what it means for the documents, and the one line of the instruction
that does not match what the reference actually publishes.*

## What changed upstream

The TechieFlow reference changed, on 2026-09-09, how a run's duration is read. The old
rule was **fill in the gaps**: a record that carried no `duration_s` had one worked out
from its own `started` and `ended`, and a record that already carried one was left alone.
The new rule is **the timestamps win**: the two clocks the record carries are the
measurement, and a stored `duration_s` is only ever a fallback for when they cannot be
read at all.

The reason is in the reference's own comment. A stored duration can contradict the
record's own clocks, and on a stream written before the emitter checked them it often
does. TechieBlog holds fourteen such records — one storing `-166`, and thirteen storing a
plausible round number (3600, 2700, 1800, 900, 600) that bears no relation to their own
timestamps. One `refresh-status` record started at 20:05:00, ended at 17:59:39, and stored
600. Only the record storing the negative was ever detectable by reading the number alone;
the other thirteen looked perfectly reasonable and were wrong.

TfLens does the old thing. `RunDuration.Derive` returns a record that already carries a
duration untouched, which was right against the old reference and is wrong now.

## The rule, in five parts

1. **A run's duration comes from its own `started` and `ended` whenever both parse.**
2. **A stored `duration_s` that disagrees with those timestamps by more than one second is
   overridden**, and the record counts as recomputed. The one-second tolerance is there so
   that ordinary rounding is not reported as a disagreement.
3. **A record whose `ended` precedes its `started` is impossible.** It carries no duration
   at all and is excluded from every duration figure. Nothing is substituted, clamped or
   guessed in its place.
4. **A run that starts and ends in the same second is not impossible.** It is a run that
   records no elapsed time, and it is counted separately. Reporting it as corrupt is a
   defect, not a rounding difference — the reference says so in its own words, having
   briefly made this repository look as though it held thirty corrupt records when it held
   thirty runs that simply recorded no elapsed time.
5. **Where the timestamps cannot be read at all, a positive stored `duration_s` is used**,
   exactly as today. Where `ended` is absent but `started` is present, `ts` stands in for
   `ended` — SCHEMA.md's own rule is that `ended` *is* the moment the record was written,
   which is what `ts` holds.

## The four counts that must be published

A total without its exclusions is just a different wrong number, so the reference publishes
four counts on the `phases` block beside the total:

| Key | What it counts |
|---|---|
| `duration_measured_n` | Records that produced a usable duration |
| `duration_impossible_n` | Records whose `ended` precedes their `started`, discarded |
| `duration_absent_n` | Records with no elapsed time recorded |
| `duration_recomputed_n` | Records where the timestamps overrode a stored figure that disagreed |

The first three partition the live run records exactly — measured plus impossible plus
absent equals `runs_live` on every repository in the estate. `duration_recomputed_n` is not
part of that partition; it is a subset of the measured records, saying how many of them had
a stored figure that was overridden.

## The change-set

| # | Kind | Item | Change |
|---|---|---|---|
| 1 | MODIFY | BRD-179 | The rule is restated. Duration is derived from the record's own timestamps whenever they parse, not only where `duration_s` is absent; a stored duration that disagrees by more than a second is overridden; an impossible record carries no duration and is excluded; a same-second run is counted as recording no elapsed time and never as corrupt. The `ts` stand-in and the read-time-only, never-backfilled character of the derivation are unchanged. |
| 2 | ADD | BRD-189 | Publish `duration_measured_n`. |
| 3 | ADD | BRD-190 | Publish `duration_impossible_n`. |
| 4 | ADD | BRD-191 | Publish `duration_absent_n`. |
| 5 | ADD | BRD-192 | Publish `duration_recomputed_n`. |
| 6 | — | REQ-FN-112 | Set to `Needs re-verify` — the requirement it is derived from changed. |

No requirement is removed and nothing is renumbered. There is no architecture change: no
new module, package or flow, and no screen. The derivation stays where it is, in
`TfLens.Core.Metrics.RunDuration`, and TfLens still writes to no stream.

## One line of the instruction that does not match the reference

The instruction says that in `tools/parity-compare.py`, **"PHASES_TOP_KEYS and the
`duration_s` tuple both need the four new keys."**

`PHASES_TOP_KEYS` does need them, and they have been added there. **The `duration_s` tuple
does not, and adding them there would break the parity gate rather than tighten it.**

The two constants describe two different places in the document:

- `PHASES_TOP_KEYS` lists the keys at the top of the `phases` block — `runs_live`,
  `duration_s_total`, and so on. This is where the reference publishes the four new counts,
  once per repository.
- `PHASES_NESTED_KEYS["duration_s"]` lists the keys **inside each individual phase's**
  `duration_s` object: `total`, `median`, `max`, `n`, `derived_n`. The reference emits
  exactly those five per phase and no more.

If the four counts were added to the nested tuple, the gate would demand them on every
phase row of both documents, find them on neither, and raise an `UNCOVERED` finding for
every phase in every repository. The gate would never reach zero.

What *does* change inside the per-phase `duration_s` block is its **values**, not its keys:
because impossible records are now excluded, `total`, `n`, `median` and `max` all move. That
is precisely what the gate should be comparing, and it does so already on the existing five
keys.

This is recorded rather than quietly worked around. It is not a disagreement with the
rule — all five parts of the rule and all four counts are adopted exactly as decided. It is
one line of the expected change-set that does not match the shape the reference publishes,
and the gate reaching zero findings is the evidence for which reading is right.

**Blocks: no.**
