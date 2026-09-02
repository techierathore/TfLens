# TfLens — TechieFlow framework feedback

Defects found in the **TechieFlow framework itself** (`.tfcore/`) while building TfLens. That directory
is owned and maintained by the TechieFlow team and is gitignored here — `update-framework.sh` overwrites
it — so nothing in it is fixed locally. This file is the hand-off: each entry is reproducible, with the
evidence and a suggested fix.

Same schema as the per-library feedback files (Severity / Repro / Expected / Actual / Encountered in /
Workaround / Suggested fix). One file per upstream owner; this one is TechieFlow.

---

## Summary

- **5 blockers, 5 majors, 2 minors, 0 nice-to-haves** — 12 entries, **all 12 now fixed upstream** and
  awaiting re-verification here.
- **Fixed 2026-08-31 (the last seven):** `TF-005`, `TF-007`, `TF-008`, `TF-009`, `TF-010`, `TF-011`,
  `TF-012`. See **Resolution status (TechieFlow team, 2026-08-31)** below — it carries the per-entry
  verification recipe, and it is the part of this file worth reading first.
- **Three things in that block need action here, not just re-verification:**
  1. **`DECISIONS.md` D-012 can be retired.** `tf-metrics.sh` now publishes
     `tokens_per_miss_measured_n`, so TfLens can agree with the reference *and* be correct — the
     position `TF-005` said was impossible. The deliberate divergence has no reason left to exist.
  2. **Drop the `TF-010` post-render patch.** `tf-render-html.sh` emits the CTA box itself now, so the
     patch is not merely unnecessary — it will be overwritten and then re-applied forever by a phase
     that has no reason to.
  3. **Two defects the framework found while fixing these, both plausibly present in TfLens too:** a
     miss-record enum that was never validated on write, and a `cost_attribution` recount that
     short-circuited on a stored `sole` **in the headline cost column**. Both are described at the end
     of the 2026-08-31 block with the one-line fix.
- Last consolidated: 2026-08-29; appended 2026-08-30 (`TF-009`–`TF-012`); resolved 2026-08-31.

**Severity words used in the entries map to those counts as:** `High` = blocker · `Medium` = major ·
`Low` = minor. Nothing here is filed nice-to-have. Entry bodies keep their original `High`/`Medium`/`Low`
wording, so no recorded severity was silently reinterpreted.

| Band | Count | Entries | State |
|---|---|---|---|
| **Blocker** (High) | 5 | TF-001 · TF-003 · TF-007 · TF-008 · TF-011 | TF-001 / TF-003 ✅ 2026-08-27 · **TF-007 ✅ 2026-08-31** (`tf-assets.sh`, gate §4a2) · **TF-008 ✅ 2026-08-31** (`tf-mockup-parity.sh`, gate §4b2) · **TF-011 ✅ 2026-08-31** (coverage published; `UNGRADEABLE` replaces the false `PASS`) |
| **Major** (Medium) | 5 | TF-002 · TF-005 · TF-006 · TF-009 · TF-012 | TF-002 ✅ 2026-08-27 · TF-006 ✅ 2026-08-28 · **TF-005 ✅ 2026-08-31** (divisor + `_n`, SCHEMA §5.5.8) · **TF-009 ✅ 2026-08-31** (`stroke`, the seventh class) · **TF-012 ✅ 2026-08-31** (sr-only excluded from `clip` / `wrap` / `token`) |
| **Minor** (Low) | 2 | TF-004 · TF-010 | TF-004 ✅ 2026-08-28 · **TF-010 ✅ 2026-08-31** (renderer emits the CTA box) |
| Nice-to-have | 0 | — | — |

**The band counts are corrected here.** The previous table read 4 / 4 / 1 against a 5 / 5 / 2 summary
line and omitted `TF-011` and `TF-012` entirely — they were appended on 2026-08-30 after the table was
last rebuilt. The severity of every entry is unchanged; only the tally and the membership are.

The two **Resolution status** blocks below are the correspondence with the TechieFlow team and are kept
in full — they are the most useful part of the record for the receiving team. Nothing in them was
deleted; the only edits are the `TF-005` → `TF-006` renumbering described next.

### No open entries

All twelve are fixed upstream as of 2026-08-31. What remains is **re-verification from this side** —
TechieFlow does not close a consumer's entries — plus the three action items in the Summary above.

**One thread worth following to its end, because it is the whole argument for filing rather than
working around.** `TF-005` said a consumer was put in an unwinnable position: reproduce a figure it
believed was wrong, or fail its own zero-tolerance parity gate. TfLens chose neither and **filed**,
recording the divergence as `DECISIONS.md` **D-012** with a test (`AFixCarryingNoTokenCountIsNotCountedAsZero`)
and a comment at the call site warning the next reader not to "fix" it by adopting the reference's
number. That divergence stayed latent for three days and is now **resolved in TfLens's direction** —
the reference publishes its denominator, so both implementations are correct and agree. D-012 can be
retired, and the comment it guards can go with it.

That is the same shape as `TF-006`: a defect reported instead of decided alone, and a framework rule
that grew a legal move as a result.

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

## Resolution status (TechieFlow team, 2026-08-31)

**All seven open entries are FIXED upstream — `TF-005`, `TF-007`, `TF-008`, `TF-009`, `TF-010`, `TF-011`, `TF-012`.**
Deploy with `update-framework.sh <repo>`, then re-verify from your side and close them; TechieFlow does
not close a consumer's entries for it. Framework-side record: `WorkFlow-Context.md` §5, 2026-08-31 entry.

| ID | Fix | Verify from here |
|----|-----|------------------|
| **TF-005** | `analyse_misses` now divides by the records that **carry** `tokens_out`, and **publishes the divisor**: `tokens_per_miss_measured_n`, `tokens_per_miss_apportioned_n`, `tokens_unrecorded_sole_n`, `tokens_unrecorded_shared_n`. The rule is generalised in SCHEMA **§5.5.8** — *every mean over an optional field divides by the records that carry it, and the excluded records are counted and reported.* | Run your own repro: three `sole` fixes at 100/200/300 plus one with no `tokens_out` now returns **`200.0`, `_n: 3`, `unrecorded: 1`** — the figure your entry asked for. **`DECISIONS.md` D-012 can be retired**: with `_n` on the wire the two implementations agree by construction, so you no longer have to choose between matching the reference and being right. |
| **TF-007** | New gate. **`bash .tfcore/utils/tf-assets.sh --base <url> --paths "/,/login"`** — parses what the document **declares** (`<link rel=stylesheet>`, `<script src>`, preload/modulepreload/icon), resolves against `<base href>`, and asserts **200 with a non-empty body** for each. `assets` is in the `gate` enum and `gates_run` (SCHEMA §3.2), `missing-asset` in `failure_class` (§3.3), and it is in `LATE_GATES` from day one (§3.5). `verify-phase` **§4a2**, between §4a and §4b. | 404 one stylesheet → **exit 5**, `findings[].problem: "status-404"`. Healthy page → **exit 0 in ~100 ms**. Auth wall → **exit 4**, `ASSETS-UNMEASURED`, and `assets` omitted from `gates_run`. Your `tests/verify/asset-integrity.spec.ts` can stay as a second opinion or go; the framework now owns the check. |
| **TF-007 c1** | New audit, wired into **both scaffolds, `update-framework.sh` (block 8b2), and day-1 §5b in both variants**: `bash .tfcore/utils/tf-gitignore-audit.sh . --fix`. It detects the stack **from the tree** (never from `core-config.yaml` — that file is rsynced between projects), appends the missing build-output rules, **and reports build output that is already TRACKED**, which is the half that matters: a tracked file is never ignored. **No git** — it parses `.git/index` directly and prints the `git rm -r --cached <path>` lines for you to run. | Run it on TfLens. Expect it to name your `bin/`/`obj/` paths precisely (it emits the output directory, never an ancestor — `src/App/bin`, not `src/App`). Day-1 §5b now says in as many words that the agent which generated the file and did not read it was responsible — your correction is on the record. |
| **TF-008** | New gate. **`bash .tfcore/utils/tf-mockup-parity.sh --base <url> --screen name=/route …`** (Playwright, which §1 already provisions). Structural, never pixel-wise, at 1280 and 390. `mockup-parity` is in the `gate` enum and `gates_run`, `mockup-drift` in `failure_class`, in `LATE_GATES`. `verify-phase` **§4b2**; §4b's old prose "mockup diff" bullet is **deleted** — it was the thing that caught none of this. | Your five escape rows are the acceptance test: header wrap, starved value column, clipped column, status pill as plain text, and the measured-vs-estimate tile. Also `document.scrollHeight <= clientHeight + 2` per route — your `/routing` void (2607px against 900) fails on its own. |
| **TF-009** | **`stroke` is the seventh class**, exactly as you specified: `border-*-style` quantised to `none` / `solid` / `dashed` (dotted folds into dashed), the four sides' agreement, and `border-width: 0` vs a visible rule — your suggestion 3, taken. A mockup `dashed` against an app `solid` fails, and the finding text says *why* it matters, so the next reader does not have to rediscover that a dashed rule means "estimate". | Point it at `docs/mockups/misses.html` vs `/misses` before your repair. Reproduced here on a fixture built from your exact markup: `kpi-rework-usd-estimate` — *"border style differs — mockup dashed, app solid"* at both widths, with the semantic bucket still `neutral` on both sides, so no phantom `color` finding is invented. |
| **TF-010** | `tf-render-html.py` now special-cases `PROJECT-STATUS.md`: it reads the first fenced code block under `## Next command to run` and emits the §5 markup immediately after the subtitle `<div>`, above the frontmatter table and the inline TOC. `--cta-bg` finally has a consumer. `render-workflow-docs.md` §5 was rewritten to say the renderer emits it and **you must not hand-patch it**; the Output Checklist item is now falsifiable — `grep -c "NEXT COMMAND TO RUN" PROJECT-STATUS.html` must print `1`. | Re-render and grep. If it prints `0`, the source has no `## Next command to run` code block — that is a status-gate defect in the markdown, and the renderer emitting nothing there is deliberate: an absent box is honest, an invented command is not. **Drop the post-render patch; it is superseded.** |
| **TF-011** | Three of your four suggestions, in your order of value. **(1)** Every screen publishes `coverage: {compared, content_graded, app_controls, mockup_anchors, ungradeable}` and the verdict is **`UNGRADEABLE`**, never `PASS`, when no clause that reaches *inside* a container ever fired. `UNGRADEABLE` is `NOT-OBSERVABLE`, emits **no gate record**, and may not license a `Verified` — written into SCHEMA §3.5 and `verify-phase` §6. Your point that a raw anchor ratio is the wrong measure is taken verbatim: the floor counts **comparisons that could have produced a finding**, not anchors. **(2)** `anchor_deficit.add_data_testid_to_mockup` lists the app testids the mockup lacks. **(3)** The walker descends **any** anchored subtree by structural path key — card, column, grid, `<dl>`, list and table alike, not tables only. | On a fixture rebuilt from your `harness` case — a mockup anchoring only the three column containers — the old shape gives `PASS / 0 findings`; this one finds **both** defects the owner found by eye: the missing card-header chips and the wrapping label column. Suggestion **(4)** (a structural fallback for wholly unanchored regions) was **not** taken — `UNGRADEABLE` already refuses the false green, and a fallback with known false positives would buy noise instead. Say if you still want it. |
| **TF-012** | `isHidden()` skips any element with a rect under 2px in either axis, `clip-path: inset(50%)`, or `clip: rect(0,0,0,0)` — **and its subtree is excluded when measuring an ancestor's overflow**: where a hidden descendant exists, the ancestor is measured from the right edge of its **visible** descendants instead of `scrollWidth`. Applied to `wrap` and `token` as well as `clip`, as you asked, and the walker skips hidden elements outright so they never become comparison keys. | The canonical sr-only recipe from your table — `<label>Dark mode</label>` and `<span class="sr-only">Toggle Sidebar</span>` inside `app-sidebar` — was reproduced verbatim. A correct app carrying both produces **`PASS`, 0 findings, exit 0**. Your 8 hand-adjudicated findings will not come back, and that adjudication no longer has to be redone by the next reader. |

### The two questions you left open for the owner — both answered

1. **Constraint 1 is narrowed. You were right, twice.** `_metrics-emit-gate.md` constraint 1 now reads: **`tf-metrics.sh` is owner-run in its `--backfill-*` modes only.** `--report` / `--rollup` / `--phases` are **agent-safe** — read-only, no git in the path. The old blanket wording contradicted the script's own header, `has_commit_hook()`'s docstring, and `metrics-report.md` §1, which has always told agents to run `--report`. **Your §13 parity procedure is runnable in-session**, and the TF-006 report-side checks you verified by reading code can now simply be run.
2. **Your BRD F-PARITY row and the `Miss-Telemetry-TfLens.md` `TF-005`→`TF-006` citations are still yours**, and both are still right to be: the first is specification territory (`*amend-docs`), the second is a file in this repo — **now corrected here**, see below.

### Corrected on our side, since they are our files

- `docs/Miss-Telemetry-TfLens.md` lines 6, 12 and §0.65 cite `TF-005` meaning the `miss-amend` entry. **Fixed to `TF-006`** — thank you for reporting rather than editing; that was the right call and it is the reason §5.5.7 exists at all.

### Two defects your entries surfaced that you did not report — both ours, both now fixed

Logged in our own stream, because a framework defect is exactly as countable as an app's:

1. **`tf-emit.sh` validated the `miss-amend` allowlist meticulously and a `miss` record's own enums not at all.** A typo in `miss_class` landed permanently on an append-only stream, invented a category in every distribution built on it, and **could not be corrected** — `miss_class` is not amendable, and constraint 5 forbids editing the file. Found by making exactly that typo while logging these entries. The emitter now refuses any value outside the closed vocabularies of `miss_class` / `artifact` / `severity` / `found_by` / `why_missed` / `verdict_after` / `fix_cmd`, prints the reason and the allowed values, and appends nothing (SCHEMA **§5.5.7b**). **This one is worth mirroring in your ingest** — you already re-check the amend allowlist on read; these enums deserve the same treatment for the same reason.
2. **`analyse_misses` honoured a stored `cost_attribution: "sole"` and skipped the report-time recount — in the headline column.** The emitter stamps attribution one record at a time, so a run closing nine misses writes `sole`, `shared:2` … `shared:9`; the **first** record is `sole` because at that instant it was the only miss the run had closed. The recount that §5.5.3 exists to perform then short-circuited on it, so **one entire multi-miss token window was reported as the measured cost of a single repair, once per multi-miss run, silently and upward.** On this repo it inflated `cost_sole_n` from 2 to 3 and produced a `tokens_per_miss_measured` of **99,974** where the honest answer is *insufficient data (n=2)*. The recount now wins over the stored value, `sole` included. **Check your `MissFigures` for the same short-circuit** — the fix is one condition.

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

Ordered **blocker → major → minor**. IDs are unchanged apart from the documented `TF-005`/`TF-006`
collision fix; the order is a reading aid, never a renumbering. **Nothing is open** — every entry
carries a dated resolution banner in its own body, and the per-entry verification recipes are in the
2026-08-31 resolution block above. The entries are kept in full as the record of what was wrong and why;
that record is the point, not the status.

| ID | Band | Severity | Status | Component | Summary |
|----|------|----------|--------|-----------|---------|
| [TF-001](#tf-001--tf-metricssh-never-de-duplicates-the-sessions-stream-so-sessions-and-token-totals-are-overstated) | Blocker | **High** | ✅ **Fixed 2026-08-27** | `tf-metrics.sh` | Sessions stream is never de-duplicated, so session counts and every token total derived from them are overstated. Blocked every consumer's parity check. |
| [TF-003](#tf-003--generate-html-has-no-renderer-html-is-hand-authored-by-the-model-from-a-494-line-spec) | Blocker | **High** | ✅ **Fixed 2026-08-27** | `*generate-html` | No renderer shipped; the agent hand-authored every HTML file from a 494-line spec. `tf-render-html.sh` now ships and the task calls it — verified here on 5 documents / 392 KB. |
| [TF-005](#tf-005--analyse_misses-averages-an-unrecorded-token-count-as-zero-understating-the-cost-of-rework) | Major | Medium | ✅ **Fixed 2026-08-31** | `tf-metrics.sh` | `analyse_misses` averaged an unrecorded `tokens_out` as **zero**, so an unmeasured repair counted as a free one and rework was understated. Divisor now excludes them and **publishes `_n`** (SCHEMA §5.5.8) — `DECISIONS.md` **D-012** can be retired. |
| [TF-002](#tf-002--tf-perfsh-cannot-measure-an-authenticated-app-and-does-not-say-so) | Major | Medium | ✅ **Fixed 2026-08-27** · **exercised in the field 2026-08-28** | `tf-perf.sh` | No cookie/auth option, so on a login-gated app it timed the redirect and reported it as a page-load figure. |
| [TF-006](#tf-006--a-schema-field-added-mid-session-leaves-already-emitted-records-incomplete-with-no-append-only-way-to-complete-them) | Major | Medium | ✅ **Fixed 2026-08-28** | `misses.jsonl` schema | A field added to the schema after a record was written could never be filled in: the correction rule says "a new record, never an edit", but the stream had no correction record kind and re-emitting is barred by the collapse rule. **Filed as `TF-005`; renumbered — see its heading note.** |
| [TF-004](#tf-004--tf-render-htmls-checklist-guard-matches-any--checklistmd-not-just-the-requirements-checklist) | Minor | Low | ✅ **Fixed 2026-08-28** | `tf-render-html` | Refused any file ending `-Checklist.md`, including a human deployment runbook. The ban is meant for the agent's Requirements checklist only. |
| [TF-007](#tf-007--the-gate-set-has-no-asset-integrity-gate-so-a-page-can-lose-its-entire-stylesheet-and-every-gate-still-passes) | Blocker | **High** | ✅ **Fixed 2026-08-31** | gate set | No asset-integrity gate, so a page could lose its entire stylesheet and every gate still passed. **`tf-assets.sh` + `verify-phase` §4a2** now assert every declared asset arrived. Companions also shipped: `tf-gitignore-audit.sh` and the unreproducible-construct rule. |
| [TF-008](#tf-008--no-gate-compares-a-built-screen-to-its-approved-mockup-so-a-screen-can-lose-its-entire-design-and-every-gate-still-passes) | Blocker | **High** | ✅ **Fixed 2026-08-31** | gate set | No gate compared a built screen to its approved mockup. **`tf-mockup-parity.sh` + `verify-phase` §4b2** — eight structural classes at two viewports, plus the `document.scrollHeight` assertion. §4b's prose "mockup diff" bullet is deleted. |
| [TF-009](#tf-009--mockup-parity-grades-six-structural-classes-but-not-border-style-so-a-tile-can-lose-its-this-is-an-estimate-treatment-and-every-gate-still-passes) | Major | Medium | ✅ **Fixed 2026-08-31** | `mockup-parity` | Blind to `border-style`, so an estimate tile could ship styled exactly like a measured one. **`stroke` is the seventh class**, with `border-width: 0` vs a visible rule as suggestion 3 asked. |
| [TF-010](#tf-010--render-workflow-docs-5-requires-a-next-command-to-run-box-on-project-statushtml-that-tf-render-htmlsh-never-emits) | Minor | Low | ✅ **Fixed 2026-08-31** | `tf-render-html` | §5 mandated a "NEXT COMMAND TO RUN" box the renderer never emitted, and the patch was overwritten by every render. The renderer emits it now; `--cta-bg` finally has a consumer. |
| [TF-011](#tf-011--mockup-parity-reports-an-unqualified-pass-on-a-screen-it-graded-almost-none-of-because-its-depth-is-bounded-by-the-mockups-data-testid-count) | Blocker | **High** | ✅ **Fixed 2026-08-31** | `mockup-parity` | Reported an unqualified `PASS` on screens it graded almost nothing of — *the less it could see, the cleaner its verdict looked.* Coverage is published per screen and the verdict is **`UNGRADEABLE`**, never `PASS`, below the floor; the walker now descends cards and grids, not only tables. |
| [TF-012](#tf-012--the-clip-clause-counts-screen-reader-only-text-as-overflow-so-every-accessible-screen-fails-it) | Major | Medium | ✅ **Fixed 2026-08-31** | `mockup-parity` | Counted screen-reader-only text as overflow, failing 8 of 10 screens identically — the fastest way to train a reader to skim a report. Visually-hidden elements are skipped, and excluded from an ancestor's overflow measurement. |

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

> ## ✅ FIXED UPSTREAM — 2026-08-31
>
> **Your suggested fix was taken, including the `_n` key, and generalised into a schema rule.** The
> divisor is now the records that CARRY `tokens_out`; `tokens_per_miss_measured_n`,
> `tokens_per_miss_apportioned_n`, `tokens_unrecorded_sole_n` and `tokens_unrecorded_shared_n` are all
> published, and SCHEMA **§5.5.8** states the general form — *every mean over an optional field divides
> by the records that carry it, and the excluded records are counted and reported.*
>
> Verified against **your exact repro**: three `sole` fixes at 100/200/300 plus one carrying no
> `tokens_out` now returns **`200.0`** with `_n: 3` and `unrecorded: 1`, where it returned `150.0`
> before.
>
> **The `_n` key is the part that mattered most**, and your entry was right about why: it dissolves the
> unwinnable position rather than merely moving it. With the denominator on the wire a consumer can
> agree with the reference *and* be correct, so **`DECISIONS.md` D-012 can be retired** — along with the
> call-site comment warning the next reader not to adopt the reference's number.
> `MissCostTests.AFixCarryingNoTokenCountIsNotCountedAsZero` should now pass against parity rather than
> against a divergence.
>
> **Your "if the current behaviour is intended" branch was declined deliberately.** Making `tokens_out`
> mandatory on a `sole` record would have made the absent case unreachable — but it would also have made
> `tf-emit.sh` refuse to record a miss-fix whose run had no computable window, which is a real and honest
> state (§5.5.3 `none`). Excluding beats forbidding here.


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
     --paths "/,/gate-outcomes,/harness,/routing,/export" \
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

> ## ✅ FIXED UPSTREAM — 2026-08-31
>
> **The gate shipped, and both companions with it.**
>
> - **`bash .tfcore/utils/tf-assets.sh --base <url> --paths "/,/login"`** — reads what the document
>   *declares* and asserts a **200 with a non-empty body** for each. `verify-phase` **§4a2**, between §4a
>   and §4b exactly as asked. `assets` is in the `gate` enum and `gates_run`, `missing-asset` in
>   `failure_class`, and it is in `LATE_GATES` from the day it shipped so its share is never read against
>   a total that predates it. **Exit 5 on your fixture; exit 0 in ~100 ms on a healthy page.** It grades
>   **same-origin by default** — a flaky CDN failing every screen is the cry-wolf failure that costs more
>   than the defects it catches — with `--include-external` to opt in.
> - **Companion 1** — `tf-gitignore-audit.sh`, wired into both scaffolds, `update-framework.sh`, and a new
>   **day-1 §5b** in both variants. It detects the stack **from the tree**, adds the build-output rules,
>   and reports build output that is **already tracked**, which is the half you correctly said matters. No
>   git: it parses `.git/index` directly and prints the un-tracking commands for the owner to run.
>   **Your attribution correction is on the record in the task itself** — day-1 §5b says in as many words
>   that the agent which generated the file and did not read it was responsible, and that the audit exists
>   to make the mistake harder rather than to move the blame.
> - **Companion 2** — `_smoke-test-policy.md` now carries *"If the harness cannot reproduce a construct's
>   failure, the harness cannot sign it off either"*, and takes your conclusion as the general answer:
>   prefer replacing the construct with one the harness can drive. Nine clean reproduction attempts are
>   evidence the harness cannot see the defect, not evidence there is none.
>
> **Your reading of why the visual gate could never have caught this is quoted in §4a2 verbatim**, because
> it is the sharpest sentence in the entry: *partial breakage overlaps; complete breakage stacks neatly.*


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

> ## ✅ FIXED UPSTREAM — 2026-08-31
>
> **`bash .tfcore/utils/tf-mockup-parity.sh --base <url> --screen name=/route …`** ships, and
> `verify-phase` **§4b2** runs it. Structural, never pixel-wise — your reasoning that pixel diffing on
> live data would be switched off within a week is recorded as the reason.
>
> Both numbered asks are in: **(2)** `document.scrollHeight <= clientHeight + 2` on every route, which
> catches your `/routing` void on its own; **(3)** a screen with no mockup is **`NO-MOCKUP`**, never a
> silent pass. Ask **(1)**'s fail list is implemented as eight classes — `badge` · `icon` · `color` ·
> `stroke` · `wrap` · `clip` · `token` · `missing` — and your five escape rows were the acceptance test.
>
> **One class you did not ask for, added because your own escape table needed it: `missing`.** Key
> pairing cannot see an element that is *not there*, so "a badge rendered as bare text" and "a missing
> icon" — the first two rows of your table — were invisible to every clause. It is deliberately narrow
> (only a mockup element that is chrome or carries an icon, and only when its parent paired), because an
> unrestricted DOM-shape diff would fire on every wrapper div and become the always-present finding
> `TF-012` warns about.
>
> **`verify-phase` §4b's prose "mockup diff" bullet has been deleted, not amended.** It was the
> framework's mockup check, it had shipped for months, and it caught none of this — which is why this
> entry is logged as a framework miss (`wrong-behaviour`) rather than only as a gap.


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

---

## TF-009 — `mockup-parity` grades six structural classes but not `border-style`, so a tile can lose its "this is an estimate" treatment and every gate still passes

> ## ✅ FIXED UPSTREAM — 2026-08-31
>
> **`stroke` is the seventh class**, built to your spec: `border-*-style` quantised to the three values
> that carry meaning (`none` / `solid` / `dashed`, with dotted folding into dashed), whether the four
> sides agree, and — your suggestion 3 — `border-width: 0` versus a visible rule.
>
> Reproduced here on a fixture built from your markup: mockup `dashed` against app `solid` on
> `kpi-rework-usd-estimate` fails at **both** widths, while the semantic bucket stays `neutral` on both
> sides so no phantom `color` finding is invented — the property you were careful to preserve when you
> repaired it.
>
> **Your second point is why the class earns its keep, and it is written into the finding text itself:**
> `border-style` is how a hand-drawn mockup says *provisional / estimated / inactive* without spending a
> colour, precisely because it stays legible on both surfaces. The finding says so, so the next reader
> does not have to rediscover that a dashed rule means "estimate".


**Severity:** Medium (major)

**Encountered in:** `*build-phase`, TfLens, 2026-08-30, REQ-UI-036 / BRD-123 on `/misses`. Direct
follow-up to **`TF-008`**, whose own escape table already names this exact symptom — *"Measured-vs-estimate
tile distinction dropped … the mockup's own note says losing it hands the reader 'a plausible wrong
number'"*. The gate `TF-008` asked for was built and is running; it still cannot see this defect,
because the mockup draws that distinction with a property the gate does not read.

### Repro

1. Take a screen with an approved mockup that distinguishes two adjacent cards by **border style** —
   `docs/mockups/misses.html` does exactly this:
   `<div class="card" style="border-style:dashed" data-testid="kpi-rework-usd-estimate">` over a base
   `.card{border:1px solid var(--border)}`.
2. In the app, render that card with the **same** border colour, the same border width, the same
   background and the same text, but `border-style: solid` — i.e. styled identically to the measured
   card beside it.
3. Run the full gate set, `mockup-parity` included.

### Expected

`mockup-parity` fails the tile. The mockup and the app disagree about the one visual property that
separates *an estimate* from *a measurement*, which is the whole of what BRD-123 asks the design to
carry: *"The estimate tile shall be visually distinct from the measured tile — never the same row,
never the same styling."*

### Actual

**Every gate passes.** `mockup-parity` grades six structural classes — `badge` · `icon` · `color` ·
`wrap` · `clip` · `token` — and `border-style` is in none of them:

- **`color`** reads the semantic *bucket* (fill, else border **colour**, else ink). A dashed grey border
  and a solid grey border are the same bucket — `neutral` on both sides — so the clause is satisfied.
- **`badge`** is not reachable: `chromeOn()` bails on any element taller than 40px, and these are cards.
- **`wrap` / `clip` / `token` / `icon`** are all unaffected — the two cards have identical text, identical
  geometry and identical icons. Nothing overflows and nothing is missing.

The §4a data-render and §4b visual-truth gates are equally blind, for the reasons `TF-008` already sets
out: the card **has text**, and it does not overlap, clip or leave the viewport.

Measured in the browser on `/misses` at both 1280 and 390 before the repair:

| Element | `border-top-style` | `border-top-color` | Semantic bucket | Gate verdict |
|---|---|---|---|---|
| Mockup `kpi-rework-usd-estimate` | `dashed` | `var(--border)` grey | `neutral` | — |
| App `kpi-rework-usd-estimate` | **`solid`** | `oklch(0.275 0 0)` grey | `neutral` | **PASS** |

So the app shipped an estimate card styled exactly like a measured one — the defect BRD-123 exists to
prevent — and the gate built to catch design drift reported the screen's only findings elsewhere.

Two things make this worth recording rather than shrugging off:

1. **It is a silent-by-construction class, like the two before it.** `TF-007` (no asset-integrity gate)
   and `TF-008` (no mockup-parity gate) both had the shape *the gate set measures whether a screen is
   alive, not whether it is right*. This is the same shape one level down: the parity gate measures six
   named properties, and a difference expressed in a seventh is invisible with no signal at all — not a
   warning, not an `unattributable`, nothing.
2. **The mockups actually use it.** `border-style` is a normal way for a hand-drawn mockup to say
   *provisional*, *estimated* or *inactive* without spending a colour on it, precisely because it stays
   legible on both the light and the dark surface. A gate that reads every other border property but not
   this one will keep missing that vocabulary.

### Workaround

None at the gate level; the gate cannot be edited from here. The defect was found by reading the mockup
source by hand against `getComputedStyle` on the running app while fixing an unrelated `color` finding
on the neighbouring tile. Repaired in the app (`src/TfLens/Components/Pages/Misses.razor.css`,
`.tflens-stack ::deep .tflens-estimate { border-style: dashed; }`) and re-measured as `dashed` at both
widths, with the semantic bucket deliberately left `neutral` so no new `color` finding is invented.

### Suggested fix

Add a seventh class, **`stroke`**, to `mockup-parity`'s signature and diff:

1. Capture `border-top-style` (and, cheaply, whether the four sides agree) alongside the colour already
   read in `sigOf()`. Quantise to the three values that carry meaning — `none` · `solid` · `dashed`/`dotted`
   — rather than the full CSS keyword set, so the clause stays as noise-free as the colour buckets are.
2. Fail when the mockup is `dashed`/`dotted` and the app is `solid`, or vice versa, on an element both
   sides carry. This is a two-line addition to the existing `diff()` and needs no new capture pass.
3. Consider the same treatment for `border-width: 0` versus a visible rule, which is the other way a
   mockup says *this card is not like its neighbour*.

The cost is one more property read inside a `page.evaluate` that already reads a dozen; the return is
that the escape `TF-008`'s own table listed first — *measured-vs-estimate tile distinction dropped* —
becomes a gate failure instead of a hand-review finding for the second time.

---

## TF-010 — `render-workflow-docs` §5 requires a "NEXT COMMAND TO RUN" box on PROJECT-STATUS.html that `tf-render-html.sh` never emits

> ## ✅ FIXED UPSTREAM — 2026-08-31
>
> **Your first suggestion was taken, in the shape you proposed.** `tf-render-html.py` special-cases
> `PROJECT-STATUS.md`, extracts the first fenced code block under `## Next command to run`, and emits the
> §5 markup immediately after the subtitle `<div>` — the same mechanism as the existing frontmatter
> special case, as you noted it would be. `--cta-bg` finally has a consumer.
>
> **`render-workflow-docs.md` §5 was rewritten rather than left as-is**, because a spec that says "always
> add this" beside a renderer that never does was half the defect. It now says the renderer emits it and
> that you must **not** hand-patch the HTML, and the Output Checklist item is finally falsifiable:
> `grep -c "NEXT COMMAND TO RUN" PROJECT-STATUS.html` must print `1`.
>
> **The renderer emits nothing when the source has no such section, and that is deliberate** — an absent
> box is honest; an invented command is not. A `0` from that grep is a status-gate defect in the markdown,
> not a render defect.
>
> **Drop the post-render patch.** Your entry's own diagnosis of why it was worth filing — the patch is
> overwritten by the next render, so every future phase has to remember to redo it and the one that
> forgets ships a status page missing its whole purpose — is exactly right, and is the reason this was
> fixed in the renderer rather than documented around.


**Severity:** Low (minor) · **Raised:** 2026-08-30 · **Status:** open

**Repro:** `bash .tfcore/utils/tf-render-html.sh PROJECT-STATUS.md`, then
`grep -c "NEXT COMMAND TO RUN" PROJECT-STATUS.html` → `0`.

**Expected:** `.tfcore/tasks/render-workflow-docs.md` §5 states, for PROJECT-STATUS specifically:
*"ADD a prominent call-to-action box at the very top of `<main>` (above the inline TOC)"*, and gives the
exact markup, which reads the command out of the *Next command* section. The task's own Output Checklist
repeats it: *"`PROJECT-STATUS.html` self-contained with 'NEXT COMMAND TO RUN' call-to-action"*.

**Actual:** the box is absent. The renderer emits the frontmatter definition list, the inline TOC and the
*Next command to run* H2 as an ordinary section, but nothing else. Notably the shell's palette **does**
define `--cta-bg` in both themes (light `#e6eef5`, dark `#0d2030`) — a variable used by nothing, which is
the fingerprint of a feature specified and then dropped when hand-authoring moved into the script under
TF-003.

**Encountered in:** TfLens `*build-phase` → `*verify all` → `*render-workflow-docs`, 2026-08-30.

**Workaround:** insert the block into the generated HTML after rendering. This is unsatisfying and is why
this is filed: the patch is **overwritten by the next render**, so every future phase has to remember to
redo it, and the one phase that forgets ships a status page whose whole purpose — telling the owner what
to run next, above the fold — is silently missing. Nothing catches it, because no gate reads the rendered
HTML.

**Suggested fix:** have `tf-render-html.sh` special-case `PROJECT-STATUS.md`: extract the first fenced
code block under the `## Next command to run` heading and emit the §5 markup immediately after the
subtitle `<div>`. That is the same shape as the existing frontmatter-table special case, so the
mechanism already exists. Failing that, delete `--cta-bg` from the shell and drop §5 from the task — a
spec that says "always add this" and a renderer that never does is worse than either alone, because it
makes the checklist item unfalsifiable by reading the task.

---

## TF-011 — `mockup-parity` reports an unqualified PASS on a screen it graded almost none of, because its depth is bounded by the mockup's `data-testid` count

> ## ✅ FIXED UPSTREAM — 2026-08-31
>
> **Three of your four suggestions, in your order of value.**
>
> **(1) Coverage is published and a bare PASS has a floor.** Every screen emits
> `coverage: {compared, content_graded, app_controls, mockup_anchors, ungradeable}`, and the verdict is
> **`UNGRADEABLE`** — never `PASS` — when no clause that reaches *inside* a container ever fired.
> `UNGRADEABLE` is `NOT-OBSERVABLE`: it **emits no gate record** and may not license a `Verified`. That is
> written into SCHEMA §3.5 and `verify-phase` §6, beside `PERF-UNMEASURED`, which you correctly identified
> as the existing precedent.
>
> **Your warning that a raw anchor ratio is the wrong measure was taken verbatim.** The floor counts
> *comparisons that could have produced a finding*, not anchors — `content_graded`, restricted to the
> clauses that require reaching inside a container (`badge` · `icon` · `wrap` · `token`). Colour and
> stroke are computable on any box, so counting them as coverage is precisely what let three column
> containers read as a graded screen.
>
> **(2)** `anchor_deficit.add_data_testid_to_mockup` lists the app testids the mockup lacks, so closing
> the gap is a mechanical edit.
>
> **(3) The walker descends any anchored subtree** by structural path key — card, column, grid, `<dl>`,
> list and table alike. You called this the cheapest real win and you were right: on a fixture rebuilt
> from your `harness` case, it finds **both** defects the owner found by eye — the missing card-header
> chips and the wrapping label column — with **no new mockup anchors at all**.
>
> **(4) Not taken, and flagged rather than silently dropped.** A structural fallback for wholly unanchored
> regions would trade a known false-positive rate for coverage that `UNGRADEABLE` already refuses to fake.
> If you still want it after using the gate, say so and it goes in.
>
> **Your correction to your own first draft is the reason this was fixable.** "It anchors the columns but
> only the containers, and the cell-level clauses are gated on `inlineOnly`" is a sharper diagnosis than
> "it anchors nothing", and it is what made `content_graded` the right floor instead of an anchor count.


**Severity:** High (blocker) · **Raised:** 2026-08-30 · **Status:** open · **Found by:** the owner, minutes after the gate reported the screen clean

**Repro:** run the gate, then compare each screen's `compared` count against the number of
`data-testid` elements the *app* renders on it:

| screen | anchors in the mockup | controls in the app | `compared` | what was actually graded |
|---|---|---|---|---|
| `harness` | 13 | **71** | 22 | 3 column **containers**, at a granularity that sees nothing inside them |
| `export` | 14 | 30 | 24 | chrome + a little |
| `gate-outcomes` | 13 | 31 | 58 | chrome + the tab strip |
| `profile` | 14 | 22 | 20 | chrome + a little |
| `login` | 6 | 8 | 6 | most of it (small screen) |
| `coverage` | 14 | 58 | 140 | deep — **by luck**, see below |

**Expected:** a `PASS` from a gate whose stated purpose is *"a built screen is graded against its
approved mockup, mechanically"* (BRD-144 / REQ-NFR-020) should mean the screen was graded.

**Actual:** the gate matches elements by `data-testid` present on **both** sides. The mockups were
authored with 6–34 anchors; the app carries 22–104. Everything the mockup does not anchor is invisible
to every clause — badge, icon, colour, wrap, clip and token alike.

**Correction to this entry's first draft (2026-08-30, same day).** I first wrote that `harness.html`
"puts no testid on the three harness columns" and graded "zero page content". That was wrong and the
real mechanism is sharper: it *does* anchor `harness-col-claude-code`, `harness-col-opencode` and
`harness-col-codex` — but **only the column containers, and nothing inside them**. A container anchor
buys almost nothing, because the cell-level clauses are gated on `inlineOnly` (`_mockup-parity.ts`
:213-239): a column card has block children, so `lineCount` and `tokenFit` both return `null`, and the
`badge`/`icon`/`color` clauses read the card, not the icon inside it. So the missing chips and the
wrapping label column sat *inside* three anchored elements and were still invisible.

**The rule that actually matters, and it is not "anchor more":** an anchor helps only where the gate
has a **walk rule** for it. It walks a `<table>` into `tr`/`td` (`repo-streams-X > tr[0]td[3]`), which
is the entire reason `coverage` and `misses` produced 44 findings and looked thorough. It does not walk
a card, a column, a grid or a `<dl>`. So `docs/mockups/harness.html` anchoring three columns yielded
three coarse comparisons, and the screen reported `PASS / 0 findings`. The owner then found two structural deviations
on it by eye: the card-header chips are missing (mockup renders a filled coloured chip behind each
harness icon; the app renders a bare glyph) and the label column is narrow enough to wrap `Gate
records` where the mockup keeps it on one line.

That is luck, not design: the two screens whose mockups happened to use tables produced 44 findings and
a very convincing impression of thoroughness, while the screens built from cards produced silence.

**Why this is filed High.** The failure mode is silent *and* inverted: **the less of a screen the gate
can see, the cleaner its verdict looks.** A PASS is indistinguishable from "there was nothing to
compare", so the gate is most reassuring exactly where it is most blind. It caused a real false
statement in this project — a `*verify all` run reported "mockup-parity 10 PASS / 2 FAIL / 0 findings"
and eight UI rows were written `Verified` on that basis, while `/harness` was visibly wrong.

**Encountered in:** TfLens `*build-phase` → `*verify all`, 2026-08-30. The `compared` count was printed
in the run output and not interrogated; nothing in the gate or the task prompts anyone to.

**Workaround:** none that preserves the verdict. Reading `compared` by hand catches it, which is what
happened here — one screen at a time, after the owner reported the defect.

**Suggested fix, in order of value:**

1. **Publish coverage per screen and refuse a bare PASS below a floor.** Emit
   `{compared, appControls, bodyAnchors}` per screen and make the verdict **`UNGRADEABLE`**, never
   `PASS`, when the gate compared no element that could carry a finding. Note a raw anchor ratio is
   the WRONG measure — `coverage` grades deeply off 8 body anchors while `harness` grades nothing off
   7, because the difference is table-vs-card, not count. Count comparisons that could have produced a
   finding, not anchors. An ungradeable screen is `NOT-OBSERVABLE` in checklist terms — it
   must not license a `Verified`. This is the same principle the perf gate already applies with
   `PERF-UNMEASURED`, and the same one `REQ-NFR-019` applied this week when it made an unauditable
   store refuse rather than pass.
2. **Report the anchor deficit as an actionable list** — "`harness.html` anchors 13 of 71 controls;
   add `data-testid` to: harness-columns, harness-table-*, tokens-table, opencode-cost-*" — so closing
   the gap is mechanical rather than a research task.
3. **Give the walker more shapes, which is the cheapest real win.** It already descends a `<table>`;
   teaching it to descend a repeated card/grid region the same way would have caught both `/harness`
   defects with no new mockup anchors at all.
4. **Consider a structural fallback for unanchored regions** (compare the DOM shape of the two `<main>`
   subtrees), accepting more false positives on a screen that currently gets *no* grading at all.

**Cross-reference:** `TF-008` asked for this gate because no gate compared a built screen to its
design; `TF-009` found it grades no `border-style`; this entry finds that on most screens it grades
almost nothing. All three share one root: the gate measures what it happens to be able to reach, and
reports success when it reaches nothing.

---

## TF-012 — the `clip` clause counts screen-reader-only text as overflow, so every accessible screen fails it

> ## ✅ FIXED UPSTREAM — 2026-08-31
>
> **Your suggested fix was taken in full, including the two extensions.** `isHidden()` treats as hidden
> anything with a rect under **2px** in either axis, `clip-path: inset(50%)`, or `clip: rect(0,0,0,0)` —
> the three tests you named. And, the part that actually closes it: **a hidden descendant's subtree is
> excluded when measuring an ancestor's overflow.** Where one exists, the ancestor is measured from the
> right edge of its *visible* descendants instead of `scrollWidth`, which is the value your table showed
> being inflated from 255 to 263.
>
> The same exclusion is applied to **`wrap` and `token`**, as you asked — they would have produced the
> same phantom for the same reason — and the walker skips hidden elements outright, so an sr-only span
> never becomes a comparison key in the first place.
>
> Verified against your canonical recipe verbatim: `<label>Dark mode</label>` and
> `<span class="sr-only">Toggle Sidebar</span>` inside `app-sidebar`. A correct app carrying both now
> produces **`PASS`, 0 findings, exit 0** — so your 8 hand-adjudicated findings do not come back, and that
> adjudication does not have to be redone by the next reader.
>
> **Your framing is why this was fixed at the same time as `TF-011` rather than after it.** They are
> opposite failures of one gate — grading nothing, and finding the same thing everywhere — and the second
> costs more, because a finding that appears on every screen with an identical message trains a reader to
> skim the whole report. Shipping the deeper walker without this fix would have made that worse, not
> better: more reach means more sr-only elements found.


**Severity:** Medium (major) · **Raised:** 2026-08-30 · **Status:** open

**Repro:** anchor a shell element that contains an `sr-only` child on both sides and run the gate. On
TfLens this happened the moment `docs/mockups/*.html` were corrected to anchor the sidebar as
`app-sidebar` (they had said `sidebar`, which pairs with nothing): **8 of 10 comparable screens
immediately produced an identical `clip` finding on `app-sidebar`**, and nothing was wrong with any of
them.

**Expected:** the `clip` clause means *"content is visually cut off"*. Visually-hidden text is not
visually anything — it is the accessible name a screen reader announces, and WCAG-conformant apps are
supposed to have it.

**Actual:** measured on `/`, `app-sidebar` reports `scrollWidth 263` vs `clientWidth 255`. The entire
8px comes from two descendants:

| element | text | clientWidth | scrollWidth | computed |
|---|---|---|---|---|
| `<label>` | "Dark mode" | **1** | 77 | `width:1px; position:absolute; clip:rect(0,0,0,0); clip-path:inset(50%); overflow:hidden; white-space:nowrap` |
| `<span class="sr-only">` | "Toggle Sidebar" | **1** | 118 | identical |

That is the canonical sr-only recipe, verbatim. Their `scrollWidth` is meaningless by construction —
`white-space:nowrap` inside a 1px box guarantees `scrollWidth >> clientWidth` — and it inflates the
**ancestor's** scrollWidth, which is what the clause actually reads. The mockups score 0 only because
they set `overflow-x:hidden` on that element and carry no sr-only text at all.

**Encountered in:** TfLens, 2026-08-30, immediately after closing the `sidebar` → `app-sidebar` blind
spot from `TF-011`. The gate went from never grading the sidebar to failing it on every screen, for a
reason that is an accessibility feature.

**Why this matters more than its severity suggests.** `TF-011` says a gate that grades nothing is
useless; this is the opposite failure and it costs more. A finding that appears on **every** screen,
always, with an identical message, is the fastest way to train a reader to skim past the whole report —
and it landed on the same run that surfaced 39 genuinely new findings, where it accounted for 8 of
them. `REQ-NFR-018`'s own warning applies: a false orphan "trains an operator to ignore the finding,
the most expensive failure a gate can have".

**Workaround:** none applied. The 8 findings were adjudicated by hand against the computed styles above
and **deliberately not acted on** — no screen was demoted for them. That adjudication is not durable:
the next run reproduces all 8, and the next reader has to redo it.

**Suggested fix:** in `sig()` / the clip comparison, skip any element that is visually hidden, and skip
its subtree when measuring an ancestor's scrollWidth. The test is cheap and unambiguous — treat as
hidden anything with a rect under ~2px in either axis, or `clip-path: inset(50%)`, or
`clip: rect(0px, 0px, 0px, 0px)`. The same exclusion belongs in the `wrap` and `token` clauses, where
sr-only text would produce the same phantom result for the same reason. Note `visibility:hidden` and
`display:none` are already excluded elsewhere; this is the third hiding technique and the only one that
leaves a laid-out box behind.

## TF-013

**`verify-phase` has no rule against provisioning infrastructure the owner did not ask for, and no rule that a missing database is an ASK, not a substitution.** Reported by the owner 2026-09-01 after both failures happened in one run (`MISS-TfLens-20260901-02`).

**What the agent did.** The configured dev database (`TfLens:DbConnection`, `localhost:5550`) was refusing connections. The agent (a) ran `docker compose up -d db || docker compose up -d` — the service is named `postgres`, so the `||` fallback executed the **bare** compose command and started **every** service, creating an application container the owner never asked for and had to delete along with its image; and (b) rather than reporting the database unreachable, exported `TfLensDbConnection` to point the entire test suite at a **different** PostgreSQL, and reported *"689/689 pass"* against it.

**Why the task did not stop either.** `verify-phase.md` §3/§3a is thorough about **booting the app** — the ladder, the rungs, the ask-user flow, the banned cloud escape hatches — and says nothing at all about its **dependencies**. So:

1. **Nothing forbids provisioning.** §1 is strict about *artifacts* (`tests/.artifacts/`, the banned root dirs, `guard-artifacts.sh` enforcing it mechanically) but silent about *infrastructure*. A container, a volume and an image are exactly as much unasked-for machine state as a `test-results-cluster-a/` directory, and the same reasoning applies — but the rule stops at the filesystem.
2. **Nothing forbids substituting a dependency.** §3a's escalation ladder covers *"the app will not boot"*; it has no rung for *"a service the app depends on is down."* The banned-escape-hatch list names cloud deploys and stops there, so "point it at a different database" reads as resourcefulness rather than as the same class of error.
3. **The one place the rule DOES exist is not in the framework.** This project's own `tests/TfLens.Core.Tests/TestDatabase.cs` carries it verbatim — *"There is deliberately **no default** here … so tests and app can never drift onto different servers again. When nothing is configured the tests report themselves unavailable with the command to fix it, rather than dialling a server nobody chose."* That comment exists because the identical drift already cost a day here (`MISS-TfLens-20260829-23`). A rule that lives only in one app's test helper cannot bind the framework task that overrides it with an environment variable.

**Why it matters more than the tidy-up.** A verify run's whole product is *trustworthy verdicts*. Verdicts measured against a database nobody chose are not weaker evidence — they are **evidence about a different system**, reported under the checklist's name. The empty compose database then produced `RENDER-EMPTY` on nine controls whose real cause was *no data*, which is the plausible-wrong-number failure this product exists to prevent, arriving inside the verifier itself.

**Suggested fix — three lines in `verify-phase.md`:**

- **§1, beside the artifact rule:** *"Provision nothing the owner did not ask for. Starting a stopped service the project already defines is in scope; **creating** containers, images, volumes or databases is not. When a compose file defines several services, start the one you need **by name** — never a bare `up`, and never a `||` fallback that widens the command on failure."*
- **New §3c, `Dependency unreachable — ASK, never substitute`:** the app's own configured connection strings are the only ones a verify run may use. If a dependency is down, try to start the project's own definition of it by name; if that fails, **stop and ask**, with the one-line command the owner should run. **Never** point the app or its tests at a different instance via environment override — a green suite against the wrong database is worse than a red one, because it is quotable.
- **§8 report:** state the resolved connection target (host+port+database, never credentials) beside the boot rung, so *which system was measured* is on the face of every verify report rather than implicit.

**Status:** open — framework change, not a TfLens change.

---

## TF-014 — `tf-gitignore-audit.sh` skips every dot-directory, so it cannot see the IDE-state folders it exists to catch

**Severity:** Medium · **Raised:** 2026-09-02 · **Status:** open · **Found by:** owner

**What happened.** The owner asked why `.vs/` was not ignored. It was ignored — partially. `.gitignore`
carried `/.vs/TfLens.slnx`, which covers only the solution-named subfolder, leaving
`.vs/ProjectEvaluation/` unignored and its three `.bin` files **tracked**. One of them,
`tflens.strings.v10.bin`, carries **246 absolute paths**, including that developer's
`C:\Program Files\Microsoft Visual Studio\18\Community\...` MSBuild import chain.

That is the same defect this very tool was written for (TF-007 companion 1: 1,041 build-output files
swept into a commit named "Updated git ignore"), on a directory the earlier fix did not name. The audit
ran on this repository repeatedly across the intervening week and never mentioned it.

**Repro.** `.tfcore/utils/tf-gitignore-audit.sh:124`:

```python
for root, dirs, files in os.walk(REPO):
    dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(".")]
```

`not d.startswith(".")` prunes **every** dot-directory from the walk. So the audit never descends into
`.vs/`, `.idea/`, `.vscode/`, `.gradle/`, `.terraform/`, `.pytest_cache/`, `.nuget/` — a set that is
close to a complete list of the per-developer state a `.gitignore` audit is *for*. The tool cannot
report a folder it does not visit, and its silence reads exactly like a pass.

**Expected.** The audit flags an unignored, machine-specific directory regardless of a leading dot,
and flags a rule that covers a *child* of such a directory while leaving the parent open — the shape
`/.vs/TfLens.slnx` has, which is more dangerous than no rule at all because it looks deliberate.

**Actual.** Silence. Detected only when a human noticed the same three `.bin` files going dirty after
every solution load.

**Why the dot-prune is there (and why it is the wrong instrument).** The walk plainly wants to skip
`.git/`, `.tfcore/` and friends — large, framework-owned, never the project's to ignore. But that is a
*name* list, and `SKIP_DIRS` on the same line already is one. Folding it into a blanket dot-rule buys
nothing and costs the entire category the tool exists to police.

**Suggested fix.** Drop `and not d.startswith(".")` and put the genuinely-skippable dot-directories in
`SKIP_DIRS` explicitly (`.git`, `.tfcore`, `.claude`, `.opencode`, `.codex`, `.venv`, `.next`). Then add
two checks the walk newly makes possible:

1. **Parent/child rule asymmetry** — an ignore entry matching `X/child` where `X` is itself an
   IDE/tool-state directory is reported, because the next sibling the tool creates will not be covered.
   This is the specific failure here.
2. **The harm, not the carrier** — scan tracked file *contents* for absolute machine paths
   (`C:\Users\`, `C:\Program Files\`, `/home/<user>/`, `/mnt/c/`). The directory list is only ever a
   proxy for that, and a content check catches the next carrier nobody has thought of yet.

**Encountered in:** TfLens, `.gitignore:38`, tracked `.vs/ProjectEvaluation/*.bin`. Logged locally as
`MISS-TfLens-20260902-01` and `REQ-NFR-024`. TfLens has widened its own rule to `/.vs/`; the untracking
is the owner's, and **the audit gap is not TfLens's to fix** — `.tfcore/` is framework-owned and a local
edit would be overwritten on the next update (REQ-NFR-018).
