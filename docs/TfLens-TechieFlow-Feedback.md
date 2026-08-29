# TfLens — TechieFlow framework feedback

Defects found in the **TechieFlow framework itself** (`.tfcore/`) while building TfLens. That directory
is owned and maintained by the TechieFlow team and is gitignored here — `update-framework.sh` overwrites
it — so nothing in it is fixed locally. This file is the hand-off: each entry is reproducible, with the
evidence and a suggested fix.

Same schema as the per-library feedback files (Severity / Repro / Expected / Actual / Encountered in /
Workaround / Suggested fix). One file per upstream owner; this one is TechieFlow.

---

## Summary

- **4 blockers, 3 majors, 1 minor, 0 nice-to-haves** — 8 entries, of which **5 are resolved** and
  **3 are open** (`TF-005`, `TF-007`, **`TF-008`**).
- Last consolidated: 2026-08-29

**Severity words used in the entries map to those counts as:** `High` = blocker · `Medium` = major ·
`Low` = minor. Nothing here is filed nice-to-have. Entry bodies keep their original `High`/`Medium`/`Low`
wording, so no recorded severity was silently reinterpreted.

| Band | Count | Entries | State |
|---|---|---|---|
| **Blocker** (High) | 4 | TF-001 · TF-003 · **TF-007** · **TF-008** | TF-001/TF-003 ✅ 2026-08-27; **TF-007 open** (no asset-integrity gate); **TF-008 open** (no mockup-parity gate, raised 2026-08-29) |
| **Major** (Medium) | 3 | **TF-005** · TF-002 · TF-006 | **TF-005 open**; TF-002 ✅ 2026-08-27; TF-006 ✅ 2026-08-28 |
| **Minor** (Low) | 1 | TF-004 | ✅ fixed 2026-08-28 |
| Nice-to-have | 0 | — | — |

The two **Resolution status** blocks below are the correspondence with the TechieFlow team and are kept
in full — they are the most useful part of the record for the receiving team. Nothing in them was
deleted; the only edits are the `TF-005` → `TF-006` renumbering described next.

### The one open entry

**`TF-005`** — `analyse_misses` averages an unrecorded `tokens_out` as **zero**, so a repair whose cost
was never recorded is counted as a free repair and rework is understated. TfLens deliberately diverges
rather than reproduce a figure it believes is wrong; the divergence is recorded as `DECISIONS.md`
**D-012** and is **latent, not live** — every dataset seen so far carries the field, so the BRD §13
parity gate currently passes at exit 0 (P-003). If that gate ever fails on `tokens_per_miss_*`, it is
this decision surfacing and the fix is upstream, not here.

### What changed in the 2026-08-28 consolidation

1. **`TF-005` was doubly allocated and is now split.** Two unrelated entries were both filed as
   `TF-005` on 2026-08-28 by different clusters — the same failure the TrBlazeUI file hit on
   2026-08-27 with `TR-010`…`TR-014`. `TF-005` **keeps** the `analyse_misses` entry, because that is
   what every citation outside this file means: `DECISIONS.md` D-012, `PROJECT-STATUS.md`,
   `docs/TfLens-BRD.md` F-MISS and `docs/TfLens-DevGuide-Screens.md`. The other — the closed
   `miss-amend` / schema-field entry — moves to **`TF-006`**. **One stale citation remains and is
   reported rather than edited:** `docs/Miss-Telemetry-TfLens.md` lines 6, 12 and §0.65 cite `TF-005`
   meaning the `miss-amend` entry and should read `TF-006`.
2. **`TF-001` and `TF-002` gained the resolution banners they were missing.** Both were recorded as
   fixed in the 2026-08-27 correspondence block, but neither entry body said so and the index table
   still showed them as live — a reader arriving at either entry would have concluded it was open.
3. **`TF-002` carries its field confirmation.** Its fix had shipped on 2026-08-27 but had never been
   exercised; on 2026-08-28 the perf gate ran through `tf-perf.sh` with `--cookie` and measured
   authenticated pages at **p95 ≤ 42 ms against a 1500 ms budget** — the first such run in the
   project. Recorded in the entry, since it is the framework team's evidence that the fix works in the
   field.
4. **The `## Entries` index was rebuilt.** It had no row at all for the open `TF-005`, so the file's
   only live defect was invisible from the top of the document.
5. **Every entry now carries the full schema.** `Encountered in` was missing from all six and has been
   filled from each entry's own `Found:` line and body — no repro or severity was invented.
6. **No duplicates were merged:** the six entries describe six distinct defects. The `TF-005`
   collision was a numbering clash, not a duplicate.

> ### ⚠ One unresolved contradiction with `PROJECT-STATUS.md` — for the owner, not fixable here
>
> `PROJECT-STATUS.md` (line 53) records **`TF-004` as open**. This file records it as fixed upstream on
> 2026-08-28 **and closed after in-repo verification**, with specific evidence: `tf-render-html.sh` on
> `docs/TfLens-Deployment-Checklist.md` renders at `37.7 KB, 13 H2, sidebar`, exit 0, matching the
> framework team's own figure exactly, while `docs/TfLens-Checklist.md` is still `REFUSED` with exit 2
> and a message naming the real reason. **The dated evidence is kept and `TF-004` stays closed here**;
> `PROJECT-STATUS.md` is owned elsewhere and was not edited. Its line 53 also still counts five
> entries, where the collision fix above makes six.

---

## Resolution status (TechieFlow team, 2026-08-28)

> **Numbering note added 2026-08-28:** the entry this block calls `TF-005` is now **`TF-006`** — the
> number was doubly allocated on 2026-08-28 and `TF-005` was kept by the *other* entry
> (`analyse_misses` averages an unrecorded token count as zero), which is the one every citation
> outside this file means. All `TF-005` references in this block have been corrected to `TF-006`;
> the correspondence is otherwise unaltered. See TF-006's own heading note.

**TF-004 and TF-006 are both FIXED upstream.** Deploy with `update-framework.sh <repo>`, then
re-verify from your side and close them — TechieFlow does not close a consumer's entries for them.
Framework-side record: `WorkFlow-Context.md` §5, 2026-08-28 entry.

| ID | Fix | Verify from here |
|----|-----|------------------|
| TF-006 | **A third record kind, `miss-amend`** (SCHEMA.md **§5.5.7**), plus `bash .tfcore/utils/tf-emit.sh --amend <miss_id> <field> <value>`. It may set a field that is `null` and **never** overwrites one that is not — so it completes a record instead of altering a fact, and the stream stays append-only in substance rather than only in form. Allowlist is `why_missed` today; the rule for extending it is written down. `tf-metrics.sh` folds amendments before counting, counts orphans, and gained a **`FIELD_SINCE`** table (beside the existing `LATE_GATES`) so a miss written before a field existed leaves that field's denominator instead of counting as unassessed — your two 07:1x records are exactly that case. Constraint 5 now names which record kind carries which correction, and says to **report a missing path rather than edit the file**. | `bash .tfcore/utils/tf-emit.sh --amend <miss_id> why_missed <value>` on a record with the field empty (expect `amended …`), then on one that already has it (expect a printed refusal, exit 0, nothing appended). `--report` should show `amendments folded` and, for anything older than 2026-08-28, `n miss(es) predate the field`. |
| TF-004 | The guard now **identifies the document instead of guessing from the suffix**: it refuses a `*-Checklist.md` only when the content also carries `## Requirements Status` or the template's `SINGLE SOURCE OF TRUTH` marker. Your deployment runbook renders; the requirements checklist is still refused with exit 2. Verified against your real `TfLens-Checklist.md` and a runbook fixture. | `bash .tfcore/utils/tf-render-html.sh docs/TfLens-Deployment-Checklist.md` → renders. Same command on `docs/TfLens-Checklist.md` → still `REFUSED`, exit 2. Drop the rename-round-trip workaround. |

**On the sequence of events in TF-006 (filed as TF-005) — your correction is right and worth keeping on the record.**
`log-miss.md` does carry `why_missed` in four places, the field shipped at 07:17:47, and your run
finished at 07:13:02. Nothing was ignored, by you or by the task: a field that does not exist yet
cannot be omitted. `instruction-ignored` was the wrong self-diagnosis and the entry is better without
it. The framework's own failure here was the one you actually reported — **a rule that named a remedy
the stream did not implement** — and that is now fixed rather than documented around.

**On the in-place edit:** it was the right call to put it to the owner rather than make it, and the
right call not to leave the field unreachable. With §5.5.7 in place there is no longer a situation
where the two conflict, which is the outcome the entry asked for. The cheaper alternative you offered
(a sentence in §5.5.6 plus date-based suppression) was taken **as well as**, not instead of, the third
kind: the suppression handles records nobody can honestly amend any more, and the amend path handles
the ones where the answer is still known. Neither alone covers both.

**Logged as misses in the framework's own stream**, since a framework defect is exactly as countable as
an app's: `MISS-TechieFlow-20260828-05` (TF-006, filed as TF-005 — `spec-contradiction` / `architecture` / major /
`why_missed: missing-checklist-item`) and `MISS-TechieFlow-20260828-06` (TF-004 — `wrong-behaviour` /
`src` / minor / `why_missed: insufficient-verify-method`), both `found_by: "library-feedback"` and both
closed `Verified`. Attribution came out `unknown` on both: the framework's own maintenance sessions are
not phase runs, so `runs.jsonl` has nothing to link to and the emitter nulled the model rather than
guessing. They will not appear in any per-model figure, which is correct.

### What changes on YOUR side (deploy `update-framework.sh` first)

Nothing here is required to keep TfLens working — the framework is backward compatible and TfLens does
not read `misses.jsonl` yet. This is the pick-up list.

**Immediately actionable:**

1. **Drop the TF-004 rename round-trip.** `bash .tfcore/utils/tf-render-html.sh docs/TfLens-Deployment-Checklist.md`
   renders directly now (verified here: 37.7 KB, 13 H2, sidebar). `docs/TfLens-Checklist.md` is still
   refused with exit 2.
2. **Your two miss records need nothing.** `MISS-TfLens-20260828-01` and `-02` already carry
   `why_missed` from the authorised in-place edit, so `--amend` correctly **refuses** them — verified
   here. Do not try to "redo them properly"; the values are right and an amend cannot overwrite.
3. **`--report` output changed shape slightly** — `why it was missed` is now denominated on records that
   *could* carry the field, and prints `n miss(es) predate the field` plus `amendments folded` when
   either applies. If any TfLens tooling parses that text rather than `--json`, re-check it.

**When you build the `/misses` page** (design record: `docs/Miss-Telemetry-TfLens.md` in the TechieFlow
repo — §0 is a requirements-delta section written against the shipped producer, read it before §3):

4. **The parser dispatches THREE kinds**, not two: `miss` · `miss-fix` · `miss-amend`. An unknown kind is
   still `InvalidLines++`, never an exception.
5. **Fold amendments into the parent at read time, and re-check the null rule while folding** — do not
   trust that the producer enforced it, because you ingest streams merged across machines where an amend
   and a later-written value can arrive in either order. Store the amend rows; never collapse at ingest,
   or `RebuildAsync` cannot re-derive.
6. **Mirror `FIELD_SINCE`** (`why_missed` → `2026-08-28`) the way you already mirror `LATE_GATES` for
   `perf`. Without it your `n of N assessed` will disagree with parity on any repo holding pre-2026-08-28
   misses — and yours does.
7. **Two "open" predicates that must not be reconciled:** the backlog excludes `wont-fix`, the collapse
   check treats it as still live, `deferred` is open in both.
8. **New parity keys:** `amendments_applied`, `orphan_amends`, `why_missed_eligible`,
   `why_missed_predates_field`, on top of the `misses` block already listed in the design doc §0.6.

**Still yours to close from the 2026-08-27 batch:** the two false statements TF-003's investigation left
on record here — `docs/TfLens-BRD.md`'s F-PARITY row and `REQ-FN-063`'s ⚠ NOT VERIFIABLE stamp — both
resting on `tf-metrics.sh` being "absent from this tree" when it is at `.tfcore/telemetry/tf-metrics.sh`
and invisible to Grep/Glob by design. `--report` / `--rollup` contain no git call, so your §13 parity
procedure is runnable in-session.

> **Owner-side response, 2026-08-28 — both were already corrected on 2026-08-27; this item is closed.**
> Checked before acting, and neither statement stands on the record uncorrected:
>
> - `docs/TfLens-BRD.md` F-PARITY (line 109) already reads *"…against the in-tree oracle
>   `.tfcore/telemetry/tf-metrics.sh` **(the earlier claim that it was absent was wrong)**"*.
> - `REQ-FN-063`'s remark carries the retraction inline — *"The 'oracle is not present' blocker was
>   **wrong** — `.tfcore/telemetry/tf-metrics.sh` exists (sha256 `326b586e…4412`)"* — and the row then
>   runs on through the passing gate to `Verified 100%`. The original sentence is still *visible*
>   because checklist Remarks are an append-only log, which is the intended behaviour, not a live claim.
>
> **But the same row surfaced something that IS stale, and it is not what was flagged.** The BRD
> F-PARITY row still carries **`Partial | 80`**, *"4 open on one root cause — SCHEMA.md §4 contradicts
> §5 on session dedupe and needs an owner decision"*, and *"Nothing is quotable yet"*. All three are
> now false: TF-001's fix resolved that contradiction upstream, the gate was re-run to **0 findings /
> 19 allowed / exit 0** at parser 1.1.0, `src/TfLens/data/parity-last.json` exists (2026-08-27 18:13),
> `/export` reads **QUOTABLE**, and all five F-PARITY requirements — `REQ-FN-058`, `-062`, `-063`,
> `-064`, `-065` — are `Verified 100%`. Left unedited deliberately: the BRD is specification territory
> and a status change there belongs to `*amend-docs`, not to a verification pass.
>
> **Constraint 1 is still unresolved, and this is the second time it has been flagged.**
> `_metrics-emit-gate.md` constraint 1 continues to describe `tf-metrics.sh` flatly as **"owner-run"**,
> while the paragraph above tells a consuming agent the parity procedure is runnable in-session. A note
> in a consumer's feedback file does not amend a framework constraint, so `--report` was **not** run
> here and the TF-006 report-side checks were verified by reading the code instead. Narrowing
> constraint 1 to *"never `--backfill-*`"* would close this; it needs the owner's word, not an agent's.

---

## Resolution status (TechieFlow team, 2026-08-27)

**All three entries are FIXED upstream and deployed to this repo** (`update-framework.sh`, 2026-08-27).
Framework-side record: `WorkFlow-Context.md` §5, first 2026-08-27 entry. Re-verify from your side and
close the entries — TechieFlow does not mark a consumer's feedback file resolved on the consumer's behalf.

| ID | Fix | Verify from here |
|----|-----|------------------|
| TF-001 | `dedupe_sessions()` added to `tf-metrics.sh` (highest `output_tokens` per `session_id`, ties on latest `ts`, **per repo**), wired at the `analyse()` call site, collapse count surfaced as `session_duplicates_collapsed` in `--json` and in the printed report. `dedupe_commits` docstring + `SCHEMA.md` §5 scope-corrected to say the union-merge argument covers `runs`/`gates` only. | Re-run your BRD §13 parity gate. The four findings in your table were one duplicated record; they should all clear together. |
| TF-002 | `--header 'K: V'` (repeatable) and `--cookie 'k=v'` pass-through; `redirects` / `redirect_rate` per level; an **all-3xx run is now a refusal** — `status:"redirected"`, **exit 4**, no latency figure emitted. Mixed runs still measure, flagged. `verify-phase` §4c documents the flags and `PERF-UNMEASURED (auth wall)`. | Re-run your REQ-NFR-001 measurement with the session cookie your Playwright spec already obtains. Your 439 ms authenticated figure should now be reproducible from the framework harness. |
| TF-003 | **`bash .tfcore/utils/tf-render-html.sh <file.md> [more.md ...]`** — dependency-free (Python 3 stdlib). `generate-html.md`, `render-workflow-docs.md` and `_status-update-gate.md` item 8 all invoke it; hand-authoring is no longer the sanctioned path. | Render your four documents with it and diff against the hand-authored versions. |

**On TF-003's regression question — it was a gap, not a regression.** Your instinct that the task files
had been "rewritten around its absence" was the right read of the evidence, but the conclusion is the
simpler one: no renderer was ever removed. No task referenced a missing script and no dangling call site
existed because none had ever pointed anywhere. The spec was written as an authoring spec from day one.

**Two notes on the TF-003 implementation**, since your entry asked for specific properties:

- **The spec is the implementation's input, not its twin.** `tf-render-html.py` **extracts §2 CSS, §3's
  theme script and §7 JS out of `html-render-shell.md` at render time**. Editing the spec changes every
  future render; the shell cannot drift from its own documentation. That is stronger than "keep the spec
  as documentation", which was what you asked for.
- **The checklist ban is now mechanical.** `*-Checklist.md` is refused with exit 2 rather than relying on
  an agent honouring §0.

Validated on this repo's own four documents (BRD 134 KB / 18 H2 / 10 diagrams; DevGuide 146 KB;
UsageGuide; PROJECT-STATUS): every H2/H3/H4 carries an id, **zero dead TOC anchors**, one `.diagram`
wrapper per `pre.mermaid`, no hardcoded copy buttons, hand-written `<a id="d-…"></a>` anchors preserved,
checklist refused. The §5.5 Mermaid checker is scoped to flowcharts and strips quoted spans first — it
does **not** fire on your sequence-diagram message text (`A->>C: SignIn(cookie: userId, email, …)`),
which is legal free-form per rule 8.

**One thing deliberately NOT changed, flagged for the owner rather than decided by an agent:**
`_metrics-emit-gate.md` constraint 1 still describes `tf-metrics.sh` as flatly "owner-run", while the
script's own `has_commit_hook` docstring says *"never a git call, so `--report` stays agent-safe"* — only
`--backfill-*` invokes git. If that is narrowed, your §13 parity procedure becomes runnable in-session.
Owner's policy call.

---

## Entries

Ordered **blocker → major → minor**, then open before resolved. IDs are unchanged apart from the
documented `TF-005`/`TF-006` collision fix; the order is a reading aid, never a renumbering.

| ID | Band | Severity | Status | Component | Summary |
|----|------|----------|--------|-----------|---------|
| [TF-001](#tf-001--tf-metricssh-never-de-duplicates-the-sessions-stream-so-sessions-and-token-totals-are-overstated) | Blocker | **High** | ✅ **Fixed 2026-08-27** | `tf-metrics.sh` | Sessions stream is never de-duplicated, so session counts and every token total derived from them are overstated. Blocked every consumer's parity check. |
| [TF-003](#tf-003--generate-html-has-no-renderer-html-is-hand-authored-by-the-model-from-a-494-line-spec) | Blocker | **High** | ✅ **Fixed 2026-08-27** | `*generate-html` | No renderer shipped; the agent hand-authored every HTML file from a 494-line spec. `tf-render-html.sh` now ships and the task calls it — verified here on 5 documents / 392 KB. |
| [TF-005](#tf-005--analyse_misses-averages-an-unrecorded-token-count-as-zero-understating-the-cost-of-rework) | Major | Medium | 🔴 **OPEN** — the only open entry | `tf-metrics.sh` | `analyse_misses` averages an unrecorded `tokens_out` as **zero**, so an unmeasured repair counts as a free one and rework is understated. TfLens deliberately diverges — `DECISIONS.md` **D-012**. |
| [TF-002](#tf-002--tf-perfsh-cannot-measure-an-authenticated-app-and-does-not-say-so) | Major | Medium | ✅ **Fixed 2026-08-27** · **exercised in the field 2026-08-28** | `tf-perf.sh` | No cookie/auth option, so on a login-gated app it timed the redirect and reported it as a page-load figure. |
| [TF-006](#tf-006--a-schema-field-added-mid-session-leaves-already-emitted-records-incomplete-with-no-append-only-way-to-complete-them) | Major | Medium | ✅ **Fixed 2026-08-28** | `misses.jsonl` schema | A field added to the schema after a record was written could never be filled in: the correction rule says "a new record, never an edit", but the stream had no correction record kind and re-emitting is barred by the collapse rule. **Filed as `TF-005`; renumbered — see its heading note.** |
| [TF-004](#tf-004--tf-render-htmls-checklist-guard-matches-any--checklistmd-not-just-the-requirements-checklist) | Minor | Low | ✅ **Fixed 2026-08-28** | `tf-render-html` | Refused any file ending `-Checklist.md`, including a human deployment runbook. The ban is meant for the agent's Requirements checklist only. |

---

---

## TF-001 — `tf-metrics.sh` never de-duplicates the sessions stream, so sessions and token totals are overstated

> ## ✅ FIXED UPSTREAM — 2026-08-27, same day
>
> `dedupe_sessions()` was added to `tf-metrics.sh` as suggested — highest `output_tokens` per
> `session_id`, ties on the latest `ts`, **per repo** — wired at the `analyse()` call site, with the
> collapse count surfaced as `session_duplicates_collapsed` in `--json` and in the printed report. The
> `dedupe_commits` docstring and `SCHEMA.md` §5 were scope-corrected to say the union-merge argument
> covers `runs`/`gates` only, which was the documentation half of this entry's ask.
>
> **✅ CLOSED — verified in this repo.** The BRD §13 parity gate was re-run end to end and the four
> findings in the table below cleared together, exactly as one duplicated record predicts:
> `parity-compare.py` exits **0** with **0 findings**, re-run again on 2026-08-28 at parser **1.2.0**
> after the oracle learned the fifth stream (`DECISIONS.md` **P-002**, then **P-003**).
> `src/TfLens/data/parity-last.json` is written and `/export` reads **QUOTABLE** — the `NOT QUOTABLE`
> state this entry's Workaround describes is over. `docs/TfLens-BRD.md`'s F-PARITY row records the same.
> Nothing below needs action; the entry is kept as the record of what was wrong and why.

**Severity:** High — produces wrong numbers silently, and blocks any consumer's parity check.

**Component:** `.tfcore/telemetry/tf-metrics.sh` (`--report`, `--rollup`) · sha256 `326b586e…4412`
**Found:** 2026-08-27, on the first full run of TfLens's BRD §13 parity gate.

### Encountered in

TfLens's BRD §13 parity gate — the acceptance requirement that every figure the app renders must match
this script key for key before it may be quoted (`REQ-FN-058`, `-062`, `-063`, `-064`, `-065`). The
gate is zero-tolerance, so one duplicated upstream record held the whole `/export` page at
`NOT QUOTABLE`.

### Repro

Any repo whose `docs/metrics/sessions.jsonl` holds two records sharing a `session_id` reproduces it.
In `techierathore/TechieFlow` at `708fcff`, lines 9 and 10 of `sessions.jsonl` are byte-identical:

```
9  {"session_id":"cb2d3e32-ebbb-4cd6-8c64-1e8d81566179","output_tokens":43196, …}
10 {"session_id":"cb2d3e32-ebbb-4cd6-8c64-1e8d81566179","output_tokens":43196, …}
```

```bash
bash .tfcore/telemetry/tf-metrics.sh --rollup <repo> --json
```

### Expected

The record is counted **once**. `SCHEMA.md` §4 states the consumer rule in the same sentence that
documents the duplication:

> "…the plugin appends a CUMULATIVE snapshot at every root-session idle (a TUI session idles after each
> turn; `opencode run` idles once), so several records may share a `session_id` — **consumers take the
> record with the highest `output_tokens`** (or the latest `ts`) per `session_id`."

### Actual

Counted twice. The parity compare against TfLens (which does implement §4) returns four findings, all
the same record:

| Key | `tf-metrics.sh` | TfLens | Delta |
|---|---|---|---|
| `per_repo[TechieFlow].sessions` | 21 | 20 | 1 session |
| `pooled.sessions` | 36 | 35 | 1 session |
| `pooled.tokens_total` | 7,810,195 | 7,762,638 | 47,557 |
| `pooled.tokens_per_verified_req` | 156,203.9 | 155,252.8 | 951.1 |

The token delta is exactly the duplicated record's own tokens (`4,361 + 43,196 = 47,557`), and the
`tokens_per_verified_req` difference falls out of it. One cause, four visible numbers.

### Root cause

`dedupe_commits()` exists at line 105; there is **no session equivalent anywhere in the file**. Sessions
are read straight through at line 410:

```python
        g = read_stream(repo, "gates")
        r = read_stream(repo, "runs")
        s = read_stream(repo, "sessions")          # <- never de-duplicated
        # Per repo, not across them: two repos may legitimately share a short sha.
        c, d = dedupe_commits(read_stream(repo, "commits"))
        commit_dupes += d
```

**Why the omission looks deliberate but isn't.** The `dedupe_commits` docstring closes with:

> "Only commits need this. runs/gates/sessions record events that happen on ONE machine and are never
> independently reconstructible, so a union merge cannot manufacture a second copy of them."

That is **correct about union merge** — and union merge is not how these duplicates arise. Session
duplicates come from the OpenCode plugin's cumulative snapshots, a separate mechanism documented in §4.
`SCHEMA.md` §5 carries the same scoped claim. So §4 and §5 do **not** contradict each other; the
docstring reasons about one source of duplication and concludes there are none at all.

### Workaround

None available to a consumer. Making the consumer count duplicates too would restore parity while making
*both* implementations wrong; in TfLens's case it would also contradict BRD-27 and break the unique index
its store relies on (`UcSessionUserRepoId`). TfLens therefore stays correct and reports
`NOT QUOTABLE` on its export page, which is the honest state until this is fixed upstream.

### Suggested fix

A sibling to `dedupe_commits`, keyed on `session_id`, keeping the highest `output_tokens` and breaking
ties on the latest `ts` — the rule §4 already states:

```python
def dedupe_sessions(records):
    """Collapse session records that share a session_id, keeping the completest.

    Duplicates are EXPECTED here too, but for a different reason than commits.
    The OpenCode plugin appends a CUMULATIVE snapshot at every root-session idle
    (SCHEMA.md §4), so one session legitimately produces several records and only
    the largest is complete. The documented consumer rule is to take the record
    with the highest output_tokens per session_id, ties broken on the latest ts.

    Claude Code records stay one-per-session via SessionEnd, so they are
    unaffected: a session_id seen once keeps its single record untouched."""
    rank = lambda r: ((r.get("output_tokens") or 0), r.get("ts") or "")
    best, order, dupes = {}, [], 0
    for r in records:
        sid = r.get("session_id")
        if sid is None:          # no natural key: pass through, as commits does
            order.append(r)
            continue
        if sid in best:
            dupes += 1
            if rank(r) > rank(best[sid]):
                best[sid] = r
        else:
            best[sid] = r
            order.append(sid)     # placeholder, resolved below; keeps first-seen order
    return [best[x] if not isinstance(x, dict) else x for x in order], dupes
```

Call site, line 410:

```diff
-        s = read_stream(repo, "sessions")
+        s, sd = dedupe_sessions(read_stream(repo, "sessions"))
+        session_dupes += sd
```

Three notes on the change:

- **De-duplicate per repo**, exactly as commits does — two repos may legitimately carry the same
  `session_id`, and collapsing across them would under-count.
- Initialise `session_dupes = 0` beside `commit_dupes` and surface it the way commits already is
  ("prints how many it collapsed"), so the collapse is visible rather than silent.
- Correct the closing paragraph of the `dedupe_commits` docstring **and** the matching sentence in
  `SCHEMA.md` §5 to scope the claim to *union-merge* duplicates specifically — sessions have their own
  duplication source, handled by §4.

### Verifying the fix

On the dataset above:

| Key | Before | After |
|---|---|---|
| `per_repo[TechieFlow].sessions` | 21 | 20 |
| `pooled.sessions` | 36 | 35 |
| `pooled.tokens_total` | 7,810,195 | 7,762,638 |
| `pooled.tokens_per_verified_req` | 156,203.9 | 155,252.8 |

Worth adding as a regression guard: a fixture with one session repeated at two different
`output_tokens` values, asserting the larger survives and the count is 1. The same fixture pins the
tie-break, which is the part most likely to drift.

### Why it matters

The figures are **overstated, not merely different**. A duplicated session inflates session counts and
every token total derived from them, and those feed `tokens_per_verified_req` — a headline efficiency
figure. The error scales with how much OpenCode work a repo has recorded, so it grows quietly rather
than announcing itself. It also blocks consumers: TfLens's acceptance gate requires an empty diff
against this script before any figure it renders may be quoted.

---

## TF-003 — `*generate-html` has no renderer: HTML is hand-authored by the model from a 494-line spec

> ## ✅ FIXED UPSTREAM — 2026-08-27, same day
>
> `.tfcore/utils/tf-render-html.sh` (+ `tf-render-html.py`) now ships, and `generate-html.md` §2 calls it
> instead of describing how to hand-build the output. Verified in this project immediately after the
> framework update: rendering five documents — PROJECT-STATUS, BRD, UsageGuide and both DevGuides,
> **392 KB of HTML including 11 Mermaid diagrams** — took one command and produced one summary line per
> file (size, H2 count, sidebar yes/no, diagram count). The spec stayed put: the script reads the CSS and
> JS out of `html-render-shell.md` at render time, so the output cannot drift from it, and it refuses a
> `*-Checklist.md` with exit 2, making that ban mechanical rather than prose. Nothing below needs action;
> the entry is kept as the record of what was wrong and why.

**Severity:** High — the largest avoidable token cost in the workflow, and it is mandatory on every phase.

**Component:** `.tfcore/tasks/generate-html.md` · `.tfcore/tasks/render-workflow-docs.md` ·
`.tfcore/templates/v4custom/html-render-shell.md` · enforced by `.tfcore/hooks/guard-status-html.sh`
**Found:** 2026-08-27, during a `*build-phase` + `*verify all` pass that re-rendered four documents.

### Encountered in

Every phase of TfLens, unavoidably. `_status-update-gate.md` item 8 requires `PROJECT-STATUS.html` to
be re-rendered in the same turn as the `.md`, and the `Stop` hook `guard-status-html.sh` refuses to end
the turn while the HTML is older than the markdown — so the cost recurs on every phase for the life of
the project, not once. Concretely met re-rendering `PROJECT-STATUS.html`, `docs/TfLens-UsageGuide.html`,
`docs/TfLens-BRD.html` and `docs/TfLens-DevGuide-Screens.html` (≈300 KB) in a single pass.

> **Possible regression — please confirm at your end.** The project owner reports that HTML generation
> **used to work as a component** and believes it was removed or dropped during a recent framework
> change. That cannot be confirmed or denied from inside a consuming project: `.tfcore/` is gitignored
> and carries no local history, so there is nothing here to diff against. What *is* verifiable is the
> state of the deployed copy today, below. If a renderer did exist and was dropped, this entry is a
> regression report; if it never existed, it is a gap report. Either way the ask is the same.

### Repro

Every phase. `_status-update-gate.md` item 8 requires `PROJECT-STATUS.html` to be re-rendered in the
same turn as the `.md`, and the `Stop` hook `guard-status-html.sh` refuses to end the turn while the
HTML is older than the markdown. So this path is unavoidable, not occasional.

### Expected

A command the agent can invoke, like every other piece of framework tooling — `tf-emit.sh`,
`tf-perf.sh`, `tf-yolo.sh`, `tf-metrics.sh` are all executables.

### Actual

**No renderer exists anywhere in the deployed framework.** `.tfcore/utils/` contains ten scripts and
none of them produce HTML:

```
techieflow-doc-template.md   tf-codex-bind.py   tf-codex-telemetry.py   tf-emit.sh
tf-goal.sh   tf-harness.sh   tf-perf.sh   tf-routing-bind.sh   tf-routing.sh   tf-yolo.sh
```

The only HTML-related assets are two task descriptions, the `guard-status-html.sh` hook, and
`html-render-shell.md` — a **494-line prose specification** (§1 slug rule, §2 CSS, §3 skeleton,
§4 anchors, §5 mermaid wrapper, §6 code blocks, §6b agent-note strip, §7 JS, §8 inline TOC, §9 checklist).

Both rendering tasks then instruct the agent to implement that spec by hand, and explicitly forbid the
cheap path:

> `generate-html.md` §2 — "Read `.tfcore/templates/v4custom/html-render-shell.md` for the full rendering
> specification. Apply every section… **Use the Write tool to create the sibling HTML — never bash
> heredocs.**"

> `render-workflow-docs.md` §3 — "**Use the Write tool — NOT bash heredocs / `cat <<EOF` / `echo >`.**"

So rendering one document means: read the source MD, read a 494-line spec, compute a slug for every
heading, dedupe collisions, decide TOC mode, and emit the entire HTML file token by token through the
model.

**A note for whoever investigates the regression question:** no task references a missing script. Every
one of them — `generate-html`, `render-workflow-docs`, `metrics-report`, `day1-brownfield` §360 —
consistently describes hand-authoring, and `html-render-shell.md` is cited across a dozen files purely
as an authoring spec (slug rule, Mermaid rules). There is no dangling call site pointing at an absent
binary. So if a renderer was removed, the task files appear to have been rewritten around its absence
rather than left broken — which would explain why nothing errors, and why the cost is invisible.

**The cost is not marginal.** This single session re-rendered four project documents:

| Document | Rendered size |
|---|---|
| `PROJECT-STATUS.html` | 16,687 bytes |
| `docs/TfLens-UsageGuide.html` | 32,459 bytes |
| `docs/TfLens-BRD.html` | 119,078 bytes |
| `docs/TfLens-DevGuide-Screens.html` | 132,467 bytes |

≈ **300 KB of HTML**, or roughly 75–80k output tokens if hand-authored — for documents whose *content*
barely changed. `PROJECT-STATUS.html` alone must be regenerated at the end of every phase, forever.

Three further consequences beyond cost:

- **Drift.** Hand-authored output is not reproducible: two renders of the same source differ in
  incidental ways, and nothing checks the result against the spec. `render-workflow-docs.md` §6 asks the
  agent to "verify each HTML mentally" — self-review by the same model that just wrote it.
- **Truncation risk.** A 132 KB file emitted in one generation can silently truncate, producing broken
  HTML that no gate catches, because no gate reads rendered output.
- **The rule is quietly unenforceable, so projects route around it.** TfLens had already done so before
  this session — a local renderer had been written in an earlier phase precisely because hand-authoring
  a document of that size, repeatedly, is not practical. That script has now been **deleted** at the
  owner's instruction, so this project is back on the sanctioned path and carrying its full cost.

### Workaround

**None.** With no framework renderer and the local script removed, the only available path is
hand-authoring every HTML file through the model, on every phase, enforced by a `Stop` hook.

### Suggested fix

**Ship the renderer as an executable and make the spec its documentation rather than its
implementation.** Concretely:

- Restore (or add) `.tfcore/utils/tf-render-html.sh`, invoked as
  `bash .tfcore/utils/tf-render-html.sh <file.md> [more.md ...]`, exactly like `tf-emit.sh`.
- Point `generate-html.md` §2 and `render-workflow-docs.md` §3 at it instead of describing how to
  hand-build the output. Both tasks keep their argument handling, the checklist ban and the directory
  rules — only the rendering step changes.
- Keep `html-render-shell.md` as the spec the script implements, so the shell stays reviewable and the
  Mermaid/slug authoring rules other tasks cite stay exactly where they are.
- **Constraint worth carrying over:** the reference machine has no markdown converter installed — no
  pandoc, no python-markdown, no node library — so the renderer needs to be dependency-free (Python 3
  standard library, or equivalent) rather than assuming a package is available.

Until this lands, every project either burns tens of thousands of tokens per phase or writes its own
renderer and silently diverges from the shell.

---

## TF-005 — `analyse_misses` averages an unrecorded token count as zero, understating the cost of rework

**Severity:** Medium — the figure is wrong only on datasets where some `sole` fix records carry no
`tokens_out`, but it is wrong in the direction that flatters the framework, and it forces every
consumer to choose between agreeing with the reference and being correct.

**Component:** `.tfcore/telemetry/tf-metrics.sh` (`analyse_misses`, `--rollup --json`) · sha256 `f4b2667a…d09a7`
**Found:** 2026-08-28, implementing BRD-122 / REQ-FN-079 against the `misses` block.

### Encountered in

BRD-122 / REQ-FN-079 — the rework-economics figures on TfLens's `/misses` page, built against the
oracle's `misses` block on 2026-08-28. Surfaced while writing `MissFigures`/`MissHarnessCost` to agree
with the reference key for key under the BRD §13 parity gate, which is where the disagreement had to be
either adopted or declared.

### Repro

Any repository whose `misses.jsonl` holds `sole`-attributed `miss-fix` records where at least one
omits `tokens_out`. Four such records — three carrying 100, 200 and 300 output tokens and one carrying
none:

```
tokens_per_miss_measured = sum(tokens_out or 0) / len(sole)
                         = (100 + 200 + 300 + 0) / 4
                         = 150.0
```

### Expected

`200.0` — the mean output tokens of the repairs whose cost was actually recorded, with the fourth
record reported as unmeasured. `cost_sole_n` already carries the record count separately, so no
information is lost by excluding it from the divisor.

### Actual

`150.0`. The unrecorded repair is averaged in as a **free** repair. The error scales with how many
records lack the field: a stream where half the `sole` fixes predate token capture reports rework as
costing half what it did.

### Root cause

`tok()` coerces the absent value on the way in, and the divisor then counts the record anyway:

```python
def tok(fs):
    return sum((f.get("tokens_out") or 0) for f in fs)

"tokens_per_miss_measured": round(float(tok(sole)) / len(sole), 1) if len(sole) >= MIN_N else None,
```

`or 0` cannot distinguish an absent field from a recorded zero, so `null` becomes a measurement.

### Why it matters

This is the same defect class the miss stream exists to expose, appearing in the tool that measures
it. SCHEMA.md §2.5 states that an absent optional stays `null` and is never coerced to zero, and the
rest of `tf-metrics.sh` honours that — `cost_usd` is explicitly *not* pooled across harnesses for
precisely this reason, with the comment *"a pooled sum over mixed harnesses would silently
under-report"*. The token mean has the identical hazard and does not guard against it.

It also puts a consumer in an unwinnable position. BRD §13 parity is zero-tolerance, so TfLens must
either reproduce a figure it believes is wrong, or fail its own acceptance gate.

### Workaround (TfLens, in place)

TfLens divides by the records that carry a count and reports the rest as unmeasured
(`MissHarnessCost.TokenRecords`). The divergence is **latent, not live**: every dataset seen so far
has `tokens_out` on every `sole` record, so the two implementations currently agree and the parity
gate passes (exit 0, recorded as `DECISIONS.md` P-003). The workaround is pinned by
`MissCostTests.AFixCarryingNoTokenCountIsNotCountedAsZero` and by a comment at the call site in
`src/TfLens.Core/Metrics/MissFigures.cs` warning against "fixing" the divergence by adopting the
reference's number.

### Suggested fix

Exclude unrecorded records from the divisor, and report them:

```python
def tok(fs):
    priced = [f for f in fs if f.get("tokens_out") is not None]
    return sum(f["tokens_out"] for f in priced), len(priced)

tokens, n = tok(sole)
"tokens_per_miss_measured": round(float(tokens) / n, 1) if n >= MIN_N else None,
"tokens_per_miss_measured_n": n,
```

The same applies to `tokens_per_miss_apportioned`. Adding the `_n` key makes the denominator visible
on both sides, which is what lets a consumer agree with the reference *and* be correct.

**If the current behaviour is intended**, say so in SCHEMA.md §5.5 — state that `tokens_out` is
mandatory on a `sole` record, and have `tf-emit.sh` refuse to write one without it. Then the absent
case cannot arise and the coercion is unreachable. Either resolution is fine; the present state,
where the field is optional and its absence silently means zero, is not.

---

## TF-002 — `tf-perf.sh` cannot measure an authenticated app, and does not say so

> ## ✅ FIXED UPSTREAM — 2026-08-27, same day
>
> `--header 'K: V'` (repeatable) and `--cookie 'k=v'` pass-through were added, along with
> `redirects` / `redirect_rate` per level. Both halves of the suggested fix were taken: an **all-3xx run
> is now a refusal** — `status:"redirected"`, **exit 4**, and no latency figure emitted at all — while a
> mixed run still measures and is flagged. `verify-phase` §4c documents the flags and the
> `PERF-UNMEASURED (auth wall)` grade.
>
> ### ✅ CLOSED — and the fix was exercised for the first time on 2026-08-28
>
> **This is the field confirmation the framework team asked for, so it is worth stating plainly: the fix
> works.** Until 2026-08-28 the fix had shipped but had never actually been run against a login-gated
> app — TfLens's REQ-NFR-001 was still resting on the Playwright workaround below. On 2026-08-28 the
> perf gate ran through `tf-perf.sh` itself, presenting the session with `--cookie`, and **measured the
> authenticated pages**: **p95 ≤ 42 ms against a 1500 ms budget**. That is the **first perf run in this
> project's history to measure authenticated pages** rather than the redirect to `/login`.
>
> Two things follow, both useful upstream:
>
> - **The refusal path is no longer reached, because the measurement path now works.** The 4.1 ms figure
>   in *Actual* below — the speed of being turned away at the door — is what the same harness produced
>   on the same app the day before.
> - **The workaround is retired.** REQ-NFR-001 no longer depends on the project's own Playwright spec to
>   get a number the framework harness could not produce; the framework harness produces it. The
>   Playwright spec is kept as a second opinion, not as the measurement of record.

**Severity:** Medium — reports a meaningless number without flagging it.

**Component:** `.tfcore/utils/tf-perf.sh`
**Found:** 2026-08-27, grading REQ-NFR-001's `perf-budget` during `*verify all`.

### Encountered in

REQ-NFR-001 (`perf-budget`, p95 page load under 1500 ms), graded during `*verify all`. Every route in
TfLens is behind a login, so the whole path set was affected — there was no unauthenticated page for
the harness to measure honestly.

### Repro

```bash
bash .tfcore/utils/tf-perf.sh --base http://localhost:5099 \
     --paths "/,/three-questions,/harness,/routing,/export" \
     --levels 1 --requests 12 --build-config Release
```
against any app whose routes require a login.

### Expected

Either a real measurement of the pages, or a clear refusal saying the paths could not be reached
as an anonymous caller.

### Actual

The harness sends a fixed header set with **no cookie and no auth option** (`--base`, `--paths`,
`--levels`, `--requests`, `--warmup`, `--timeout`, `--build-config`, `--label`, `--json-out` are the
whole flag set). Every route answered `302` to `/login`, and it reported `p95 = 4.1 ms` — the speed of
being turned away at the door, not of any page. The `non_200` array does carry the redirects, so a
careful reader can catch it, but nothing in the summary marks the latency figure as meaningless.

### Workaround

Graded the REQ as `PERF-UNMEASURED (non-200 responses)` per `verify-phase.md` §4c rather than recording
the 4.1 ms, and measured the budget instead with the project's own authenticated Playwright spec
(`tests/verify/perf-report-pages.spec.ts`): p95 **439 ms** against a 1500 ms budget, n=60, Release build.

### Suggested fix

- A `--header` / `--cookie` pass-through so the harness can present a session.
- Treat an all-`3xx` path set as an error (non-zero exit, as `--base` unreachable already does) rather
  than returning a latency figure computed from redirects.

---

## TF-006 — a schema field added mid-session leaves already-emitted records incomplete, with no append-only way to complete them

> ### ⚠ RENUMBERED 2026-08-28 — this entry was originally allocated `TF-005`
>
> **`TF-005` was doubly allocated**, the same way `TR-010`…`TR-014` were in the TrBlazeUI file: this
> entry and *"`analyse_misses` averages an unrecorded token count as zero"* were written on the same day
> by different clusters and both took the number. The two are unrelated defects, so this is a numbering
> collision, not a duplicate to merge.
>
> **`TF-005` now means the `analyse_misses` entry**, because that is what every citation outside this
> file means by it: `DECISIONS.md` **D-012** (×4), `PROJECT-STATUS.md` (×3, "TF-005 open"),
> `docs/TfLens-BRD.md` F-MISS, and `docs/TfLens-DevGuide-Screens.md`. This entry — which is **closed** —
> takes the next free number, `TF-006`, so the live citations keep resolving and the closed one moves.
>
> **One stale citation is left, and it is not fixable from here:** `docs/Miss-Telemetry-TfLens.md`
> (lines 6, 12 and §0.65) cites `TF-005` meaning **this** entry — the `miss-amend` report. That file is
> a design record for the TechieFlow repo, so it is reported rather than edited: **those three should
> read `TF-006`.** Every `TF-005` reference in the resolution blocks at the top of *this* file has
> already been corrected to `TF-006`.

> ## ✅ FIXED UPSTREAM — 2026-08-28, same day
>
> **Both** of the fixes this entry proposed were taken, because each covers a case the other does not.
> The third record kind — **`miss-amend`** (SCHEMA.md §5.5.7) — is written exactly as suggested: it may
> set a field that is `null` and may never overwrite one that is not, so it completes a record instead of
> altering a fact; the allowlist is closed-vocabulary only, and orphans are counted rather than dropped.
> One boundary was added to the design while implementing it: **a judgement may be completed, an
> observation may not** — `why_missed` is a classification a reader can still make honestly next week,
> while a gate verdict is a fact about a finished run (§3.5's rule, seen from the other side). Everything
> the emitter derives is excluded outright. The cheaper alternative was taken as well: `tf-metrics.sh`
> gained a **`FIELD_SINCE`** table so records predating a field leave that field's denominator, with the
> excluded count printed. And constraint 5 now names which record kind carries which correction, plus
> the instruction to **report a missing path rather than edit the file** — which is what this entry did.
>
> **✅ CLOSED — verified in this repo 2026-08-28.**
>
> *Refusal paths, against the real `docs/metrics/misses.jsonl`.* All three refuse with a readable reason
> and **exit 0**, and the stream came out byte-identical (md5 unchanged, 3 lines → 3 lines):
> a field already set (`why_missed is already 'missing-checklist-item' … an amend completes a record,
> never overwrites a value`); an unknown `miss_id` (`no miss record … on this stream` — the orphan
> guard); and a non-allowlisted field (`severity is not an amendable field (SCHEMA.md §5.5.7)`).
>
> *Positive path, in a sandbox* — the real stream no longer holds a record with `why_missed` empty, so a
> throwaway repo was used rather than manufacturing a fake miss on the live log. `--amend` printed
> `amended … why_missed = insufficient-verify-method`, **appended** a `miss-amend` row and left the
> parent `miss` line byte-identical; a second amend of the same field was refused; and free text
> (`"we just forgot lol"`) was refused as outside the closed vocabulary. That last one settles the
> constraint-7 worry this entry raised: the amend path **cannot** become a free-text back door.
>
> *Not executed:* the `--report` checks (`amendments folded`, `n miss(es) predate the field`). Verified
> statically instead — `FIELD_SINCE = {"why_missed": "2026-08-28"}` at `tf-metrics.sh:54`, the
> predates-field line at `:898`. See the constraint-1 note below for why it was not run.
>
> *The `why_missed` values on TfLens's two records stand:* `MISS-…-01` `missing-checklist-item`,
> `MISS-…-02` `dependency-not-declared`. `--amend` refuses them, correctly, and they were not redone.

**Severity:** Medium — silently degrades the stream's most decision-changing field, and the documented
correction path does not exist for this stream. Recurs on every future schema addition.

**Component:** `.tfcore/telemetry/SCHEMA.md` §5.5 (record kinds) · `_metrics-emit-gate.md` constraint 5
**Found:** 2026-08-28, when `why_missed` (§5.5.6) landed four minutes after a `*log-miss` run.

### Encountered in

The `*log-miss TfLens` run of 2026-08-28 07:10:35–07:13:02, which emitted `MISS-TfLens-20260828-01`,
its `miss-fix`, and `MISS-TfLens-20260828-02` — four minutes before `update-framework.sh` added the
`why_missed` field at 07:17:47. Both `miss` records carry `found_by:"owner"`, which is the category
§5.5.6 calls the most valuable in the stream, so leaving the field `null` was the costliest available
outcome.

> **This is not a complaint about the update.** The feature is good and the timing was luck. The defect
> is that the framework has no legal move for the situation the update created, and it will create it
> again.

### Repro

Any record emitted before a schema addition reproduces it. The concrete instance:

| Time (UTC) | Event |
|---|---|
| `07:10:35`–`07:13:02` | `*log-miss TfLens` emitted `MISS-TfLens-20260828-01`, its `miss-fix`, and `MISS-TfLens-20260828-02`. `why_missed` existed in neither `SCHEMA.md` nor any task file. |
| `07:17:47` | `update-framework.sh` rewrote **ten files at one mtime** — `SCHEMA.md`, `tf-metrics.sh`, `metrics-report-template.md`, and every emitting task (`log-miss`, `verify-phase`, `build-phase`, `triage-issues`, `amend-docs`, `metrics-report`, `_metrics-emit-gate`) — adding `why_missed`. §5.5.6 is labelled *"ported from the Playbook 2026-08-28"*. |
| after | Both `miss` records carry `found_by:"owner"`. §5.5.6: an escape without `why_missed` *"wastes the most valuable record in the stream."* `tf-metrics.sh` counts them in a named waste bucket (line ~258). |

### Expected

A supported way to set a field that was `null` on an existing record — or an explicit statement that
records predating a field stay `null` and are excluded from that field's denominator.

### Actual

Neither exists, and all three available moves are forbidden:

- **Edit the record.** `_metrics-emit-gate.md` constraint 5 and `docs/metrics/README.md`: *"Never rewrite,
  compact, sort, or de-duplicate a history file… If a record is wrong, the correction is a **new record**,
  never an edit."*
- **Append a correction.** `misses.jsonl` has exactly two kinds, `miss` and `miss-fix` (§5.5.1, §5.5.2).
  Neither can carry one. The rule names a remedy the stream does not implement.
- **Re-emit the miss.** Barred by §5.5.4 collapse — and both misses count as *still open* (`MISS-…-01`'s
  `miss-fix` carries `verdict_after:"Needs re-verify"`, not `Verified`), so a re-emit would double the
  miss count and make it a measure of retry patience, which is the exact failure §5.5.4 exists to prevent.

The field is therefore unreachable: leave it `null` forever, or break constraint 5.

### Workaround

Edited the two lines in place — **on the owner's explicit instruction, after presenting the conflict** —
inserting one key each and touching no other bytes:

```diff
  "miss_class":"wrong-behaviour",
+ "why_missed":"missing-checklist-item",
  "artifact":"devguide",

  "miss_class":"unspecified-gap",
+ "why_missed":"dependency-not-declared",
  "artifact":"tests",
```

Re-validated after: 3/3 records parse, both values in the §5.5.6 vocabulary, the `miss-fix` line
untouched (`cost_attribution:"sole"` and its token window intact), and the escapes-without-`why_missed`
bucket back to 0. An agent should not be making that call, which is why it was put to the owner.

### Suggested fix

A third record kind, so the correction rule has something to name:

```json
{"kind":"miss-amend","miss_id":"MISS-App-20260828-01","field":"why_missed","value":"missing-checklist-item"}
```

- May only set a field that is `null` on the parent; **never** overwrites a non-`null` value, so it
  cannot rewrite history — it completes a record rather than altering a fact.
- `tf-metrics.sh` folds amendments into the parent before counting; an amendment with no parent is an
  **orphan**, reported and counted, exactly as §5.5.2 already treats an orphan `miss-fix`.
- Restrict it to fields whose vocabulary is closed, so it can never become a free-text back door
  (constraint 7).

**Cheaper alternative, if a third kind is unwanted:** say so in §5.5.6. The reporting side already
behaves correctly — line ~248's comment reads *"Denominator is records that CARRY the field"*, so a
`null` does not distort the distribution. Only two things are missing: a sentence stating that records
predating a field stay `null` legitimately, and suppression of the escape-waste warning for records
whose `ts` precedes the field's introduction date (§3.5 already establishes that pattern for `perf`,
and names it as *"the rule for any future gate"* — this is the same hazard arriving on a different
stream).

Either way the general point stands: **`why_missed` will not be the last field added to this schema**,
and every addition repeats this unless the completion path is defined once.

---

## TF-004 — `tf-render-html`'s checklist guard matches any `*-Checklist.md`, not just the requirements checklist

> ## ✅ FIXED UPSTREAM — 2026-08-28
>
> The guard now identifies the document by **content**, which is the more robust of the two options
> this entry offered: a `*-Checklist.md` is refused only when it also carries `## Requirements Status`
> or the template's `SINGLE SOURCE OF TRUTH` marker. Verified against the real `TfLens-Checklist.md`
> (still refused, exit 2) and a deployment-runbook fixture (renders). The rename round-trip below is no
> longer needed. Nothing else in the entry needs action; it is kept as the record of what was wrong.
>
> **✅ CLOSED — verified in this repo 2026-08-28.** `bash .tfcore/utils/tf-render-html.sh
> docs/TfLens-Deployment-Checklist.md` → `rendered … (37.7 KB, 13 H2, sidebar)`, exit 0, matching the
> team's own figure exactly. `bash .tfcore/utils/tf-render-html.sh docs/TfLens-Checklist.md` → still
> `REFUSED`, exit 2 — and the message now reads *"TfLens-Checklist.md **is the requirements
> checklist**"* rather than the old suffix guess, so the refusal states the actual reason.
> **The rename round-trip in the Workaround section below is superseded — do not use it.**

**Severity:** Low — a false positive with an easy workaround, but it blocks a legitimate document.

**Component:** `.tfcore/utils/tf-render-html.py` line ~464
**Found:** 2026-08-27, rendering a deployment runbook the owner asked to be named `Deployment-Checklist.md`.

### Encountered in

`docs/TfLens-Deployment-Checklist.md` — the human-facing deployment runbook (prerequisites, secrets,
the GitHub-token setup, compose steps, first-run verification), whose name the owner chose. It is the
only document in the project that trips the guard, and it must be rendered like every other
owner-facing document.

### Repro

```bash
bash .tfcore/utils/tf-render-html.sh docs/TfLens-Deployment-Checklist.md
```

### Expected

The document renders. It is a human-facing deployment runbook — prerequisites, secrets, the GitHub-token
setup, compose steps, first-run verification — with an `Audience:` line and no Requirements Status table.

### Actual

```
tf-render-html: REFUSED — TfLens-Deployment-Checklist.md is a checklist — checklists are
AI-agent working documents and are NEVER rendered to HTML (html-render-shell §0).
```

The guard is `re.search(r"-Checklist\.md$", base, re.I)`, which matches **any** filename ending
`-Checklist.md`. The rule it enforces (`generate-html.md`, `html-render-shell §0`) is specifically about
`docs/{AppName}-Checklist.md` — the per-REQ Requirements Status document agents read in markdown. A
deployment checklist, a release checklist, a QA checklist and so on are ordinary human documents that
happen to share the word.

### Workaround

Render from a temporarily-renamed copy and move the output back:

```bash
cp docs/TfLens-Deployment-Checklist.md docs/TfLens-Deployment-Runbook.md
bash .tfcore/utils/tf-render-html.sh docs/TfLens-Deployment-Runbook.md
mv docs/TfLens-Deployment-Runbook.html docs/TfLens-Deployment-Checklist.html
rm docs/TfLens-Deployment-Runbook.md
```

Ugly, and it would be easy for a future agent to instead "solve" this by hand-authoring the HTML — the
exact path TF-003 removed.

### Suggested fix

Tighten the guard so it identifies the document rather than guessing from a suffix. Either:

- match the canonical name only — `^{AppName}-Checklist\.md$`, resolved the way the tasks already
  resolve `{AppName}`; or
- match on content — a file carrying the `## Requirements Status` heading (or the
  `SINGLE SOURCE OF TRUTH` marker comment the checklist template ships) is the agent document; anything
  else is not.

The content check is the more robust of the two and does not depend on naming discipline.

---

---

## TF-007 — the gate set has no asset-integrity gate, so a page can lose its entire stylesheet and every gate still passes

**Severity:** High · **Raised:** 2026-08-28 · **Status:** open · **Found by:** owner, UAT

### What happened

On 2026-08-28 `*handoff-phase` declared TfLens **READY FOR UAT** on a build reporting **140 of 143
`Verified`**, with acceptance, data-render, visual-truth, standards, BRD §13 parity and perf all
green. The owner opened `/login` and got an unstyled single column: brand panel, bullets and sign-in
card stacked at x=0. One stylesheet — the Blazor scoped-CSS bundle — had not arrived, and it carried
100% of that page's layout.

**No gate asked the question that would have caught it, and none of them could have.**

| Gate | Why it passed |
|---|---|
| acceptance | Every control was present and every assertion about behaviour held. An unstyled page behaves correctly. |
| data-render (§4a) | "Does this control carry non-placeholder text?" — yes. Text renders fine without CSS. |
| visual-truth (§4b) | "Do these boxes overlap / clip / sit off-viewport?" — no. A single stacked column overlaps nothing. It is the *tidiest* possible failure. |
| standards | File-level; never loads the app. |
| perf | Measures latency. An unstyled page is if anything faster. |

The visual-truth gate is the one that ought to own this, and its geometry checks are structurally
blind to it: **total loss of layout produces a page that passes every geometric assertion.** Partial
breakage overlaps; complete breakage stacks neatly.

### The gap, stated precisely

Nothing in the framework verifies that **the assets a page declares actually arrived**. A 404 on a
`<link rel="stylesheet">` or a `<script src>` produces no console error the gates read, no server log
line, no Blazor error boundary, and no failed assertion. The app renders something that looks
intentional and every gate agrees.

### What TfLens did about it locally

Added `REQ-NFR-015` and `tests/verify/asset-integrity.spec.ts`: for `/login` and the authenticated
shell, read every `<link rel="stylesheet">` and `<script src>` the document declares and assert a
**200 with a non-empty body** for each. Fourteen lines of real logic. It runs in under a second and
would have caught this on the day it was introduced.

### Ask

**Add an asset-integrity gate to `verify-phase.md` §4, between `render` (§4a) and `visual` (§4b)**,
with `"assets"` in the `gates_run` vocabulary and the `gate` enum in `SCHEMA.md` §3.2 so its catch
rate is measurable like every other gate. The check is generic — it needs no knowledge of the app,
only the rendered document — so it belongs in the framework rather than in each project.

Two smaller companions, both from the same session and both currently unowned by any gate:

1. **The scaffold writes a `.gitignore` with no section for the project's own stack.**
   *Upgraded 2026-08-29 from "a check worth adding" to a located root cause, with evidence.*

   > **Whose fault this is, stated plainly, because it was first stated wrongly.** The agent that ran
   > day-1 generated this file and did not read it, having just chosen the stack and written the
   > solution itself. That agent is responsible. This entry asks the framework to make the mistake
   > harder to make — which is worth doing precisely *because* it is easy to make — but a generator's
   > omission is not a defence for the agent operating the generator, and TfLens's own record
   > (`REQ-NFR-016`) names the phase, not the template.

   TfLens's `.gitignore` was created by the day-1 scaffold (commit `979265f`, 2026-08-26). Every
   section in it is framework-managed and labelled as such — `.tfcore/`, `.claude/`, `node_modules/`,
   `tests/.artifacts/`, `playwright-report/`, `logs/`. It is a complete, careful ignore file **for
   TechieFlow's own artifacts**, and it contains no `bin/`, no `obj/`, and no rule of any kind for
   the stack the project is actually written in — in a repository whose `core-config.yaml` and four
   `.csproj` files say .NET 10 throughout.

   The consequence was mechanical and immediate: the first build produced build output, and commit
   `80cb71c` — named, with some irony, *"Updated git ignore"* (2026-08-27) — swept **1,041**
   build-output files into the index. Four later commits added more, reaching **1,962**.

   Those files carry the static-web-assets manifest, whose content roots are **machine-absolute**:
   `/mnt/c/…` + `/home/<user>/.nuget/…` after a WSL build, `C:\1MyCode\…` +
   `C:\Users\<user>\.nuget\…` after a Windows build (both captured on 2026-08-28). Committing them
   ships one machine's absolute paths to another, which is a plausible route to precisely the 404
   this entry is about.

   **Ask:** the day-1 tasks already know the stack — they choose it, write it into `core-config.yaml`
   and generate the solution. Whichever step emits `.gitignore` should emit the stack's build-output
   rules with it (`bin/`, `obj/` for .NET; `__pycache__/`, `.venv/` for Python; `dist/`, `build/` for
   Node), and `update-framework.sh` should assert on every refresh that a repository's build output
   is ignored **and untracked** — the second half matters, because a tracked file is never ignored no
   matter what the ignore file says, so adding the rule later fixes nothing on its own. Every project
   the scaffold has ever created is likely to carry this.

   **Reproduce:** scaffold a new .NET project and run one build; the artefacts are stageable.
2. **A "no unreproducible construct" prompt.** The same UAT reported a modal dialog leaving the page
   dimmed and dead. Nine reproduction attempts in headless Chromium could not produce it. TfLens's
   resolution was to delete the construct — the flows became routes (`REQ-UI-044`) — which is
   probably the right general answer: *if the harness cannot reproduce a UI construct's failure mode,
   the harness cannot sign it off either.* Worth a line in `_smoke-test-policy.md`.

**Verify:** point the gate at a page and 404 one of its stylesheets; the run must fail with
`gate:"assets"`. Then run it against a healthy page; it must cost well under a second.

---

## TF-008 — no gate compares a built screen to its approved mockup, so a screen can lose its entire design and every gate still passes

**Severity:** High (blocker)

**Encountered in:** `*triage-issues` / `*fix-issues`, TfLens, 2026-08-29. Owner UAT: *"it's still not
matching the mockups present in docs/mockups/ folder"*.

### Repro

1. Build any screen that has an approved mockup in `docs/mockups/`.
2. Render a control the mockup draws as a **badge** as plain text instead; omit an **icon**; let a
   header **wrap** to two rows; size a value column narrower than its longest number.
3. Run `*verify` and let the §4a data-render and §4b visual-truth gates grade it.

### Expected

At least one gate fails. The screen does not match the design it was built from.

### Actual

**Every gate passes, and the REQ reaches `Verified`.** In TfLens this produced a checklist reading
**145 `Verified`** against a running app with structural drift on **13 of 14 comparable screens** — 20
distinct findings, 15 REQs demoted in one sitting.

The mechanism is not a bug in either gate; it is what they measure:

- **§4a data-render** asks *does the control show data?* A badge rendered as plain text **has text**.
- **§4b visual-truth** asks *do controls overlap, clip, or leave the viewport?* A header that wraps to
  two rows does not overlap. A 71px value column that splits `2,287,975,139` across three lines does
  not overlap. A missing icon is nothing to measure. An unstyled-but-well-spaced screen passes both.

Concrete escapes from the TfLens run, all of which passed both gates:

| Symptom | Measured |
|---|---|
| Header wrapped to two rows on all six report routes | 105px against the mockup's 64px |
| `/harness` value column starved by a `nowrap` label column | **71px**; `Cache read 2,287,975,139` broke across 3 lines, mid-number |
| `Days since` column pushed out of its card | present in the DOM, clipped off the right edge |
| Status pill rendered as plain text | `/export` parity verdict, the one value on the page meant to be seen at a glance |
| Measured-vs-estimate tile distinction dropped | `/misses`, where the mockup's own note says losing it hands the reader "a plausible wrong number" |

There is also a **document-level blind spot next to §4b**: on `/routing`, `document.scrollHeight` was
**2607px against a 900px viewport** — the page had escaped the app shell's scroll container and rendered
~1,700px of blank void with the shell repainted at the bottom. No gate looks at document height, so this
passed too.

### Workaround

None available in-repo. Detection required a human comparing 18 screenshots by hand; the repair was
`*triage-issues` → `*fix-issues`. Logged locally as `REQ-NFR-020`.

### Suggested fix

A **`mockup-parity` gate** in `verify-phase`, run alongside §4a/§4b and reported in `gates_run`:

1. For every screen with a mockup in `docs/mockups/`, capture the built page and the mockup at the same
   viewports (1280 and 390) and compare **structurally**, not pixel-wise — pixel diffing on live data is
   unusable and would be ignored within a week. Fail on: a control the mockup renders as a badge/pill
   rendered as bare text; a missing icon or icon button; a semantic colour that does not match (status
   green/amber/red, chart series); a header or row that wraps where the mockup is single-line; a table
   column clipped out of its container; **a value cell narrower than its longest unbreakable token** (a
   formatted number must never break mid-digit).
2. Assert `document.scrollHeight <= clientHeight + 2` on every route. This is cheap, has no false
   positives in a shell-scrolled app, and would have caught the `/routing` void on its own.
3. Report a screen with no mockup as **`⚠ NO-MOCKUP`**, never as a silent pass — the same discipline
   `⚠ STATIC-ONLY` already uses.

**Why this is worth a gate rather than a checklist item.** This is the second defect class in two days
that every gate passed and a human caught (`TF-007`, no asset-integrity gate, 2026-08-28). Both have the
same shape: **the gate set measures whether a screen is alive, not whether it is right.** TfLens's own
telemetry now says so numerically — `insufficient-verify-method` is **24 of 46** answered `why_missed`
records, and the `app` escape rate is **91%**, with `escaped` (22) larger than every real gate catch
combined (5). Adding acceptance criteria does not help: in every case above the acceptance existed and
was met. The missing thing is a gate that can fail.
