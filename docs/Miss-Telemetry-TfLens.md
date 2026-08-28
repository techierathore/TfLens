# Miss telemetry — TfLens (what has to be added to the lens)

**Status:** DESIGN — nothing in TfLens is implemented yet. **The producing side is now real:** TechieFlow shipped `misses.jsonl` on 2026-08-28, so the stream this document consumes is no longer hypothetical — it is emitting, and its final field list is `.tfcore/telemetry/SCHEMA.md` §5.5, not the design sketch.
**Target repo:** `/mnt/c/1MyCode/TfLens` (`TfLens.slnx`, .NET 9 / Blazor Server / PostgreSQL 16).
**Siblings:** `docs/Miss-Telemetry-TechieFlow.md` (the stream this consumes — **read it first**, especially its §0 implementation status) · `docs/Miss-Telemetry-AI-First-Playbook.md`.
**Feedback loop:** §0.65 exists because TfLens reported **TF-006** (`docs/TfLens-TechieFlow-Feedback.md`) rather than deciding alone. That is the intended path when a framework rule has no legal move.

---

## 0. Requirement updates from the shipped producer (2026-08-28)

Eight changes to what is specified below. They come from the framework implementation, from the first repo refreshed against it — **which was TfLens itself** — and from TfLens's own TF-006 report (§0.65).

**0.1 — `why_missed` is a real column, and its denominator is not the miss count.** The stream carries a `why_missed` field (SCHEMA §5.5.6) saying *which practice failed*, where `miss_class` says *what was missed*. Seven values: `missing-checklist-item` · `insufficient-verify-method` · `code-audit-limitation` · `ambiguous-acceptance` · `dependency-not-declared` · `instruction-ignored` · `other`. `MissRecord` gains `WhyMissed`, and the page gains a failed-practice distribution — arguably the most decision-changing band on it, because it answers *"is our specification weak or our verification weak?"*.

**The trap, and it is exactly the product's stated failure mode:** the field is **optional**, and `null` means *not assessed* — never a zero in some category. So the distribution's denominator is **records that carry the field**, and the page must print `n of N misses assessed` beside it. A distribution rendered over all misses silently understates every category and is a plausible wrong number of the purest kind. `tf-metrics.sh` already reports it this way (`why_missed_n`); parity will catch a divergence, but the shape should be right by construction.

**Second-order figure worth carrying:** `escapes_missing_why` — escapes (`FoundBy ∈ {owner, production}`) that arrive with no `WhyMissed`. It is a **data-quality** figure, not a quality figure: it counts the most valuable records in the stream arriving incomplete. Show it on Coverage, not on the KPI row.

**0.2 — `OriginConfidence` is emitter-derived, which makes Guard 1 stronger than specified.** No agent writes it, and `tf-emit.sh` forces `OriginModel` / `OriginHarness` to `null` whenever the lookup fails. So a non-`linked` record cannot carry a model name at all, and `MissAttributionTaint` is filtering on a value the producer controls rather than on an agent's self-assessment. The guard does not change; its guarantee is simply real.

**0.3 — "Open" needs two predicates, not one, and they deliberately disagree.** The shipped report splits the lifecycle three ways, and TfLens must too:

| Question | Predicate | Where it belongs |
|---|---|---|
| **How much work is outstanding?** (the backlog the owner reads) | latest `MissFix.VerdictAfter ∉ {Verified, wont-fix}` | the KPI tile "open misses" |
| **Is this defect still live?** (the producer's collapse check) | latest `VerdictAfter != "Verified"` — **`wont-fix` is still live** | not TfLens's job, but explains the gap |
| Deliberately declined | latest `VerdictAfter == "wont-fix"` | its own tile / column, never folded into open |

`deferred` is outstanding work and stays **open** in both. `wont-fix` is a decision, not a backlog item, but the next failure on that REQ is still the same defect. `tf-metrics.sh` carries a standing comment that these two predicates are asking different questions and must not be reconciled; a TfLens reviewer will otherwise "fix" one of them. §3.4's *Open misses* row is superseded by this table.

**0.4 — `FixCmd` includes `log-miss`.** TechieFlow shipped a `*log-miss {App} "<what was missed>" [--fixed]` command — a 20-second front door that never boots the app. Its `--fixed` mode writes a `miss-fix` with `fix_cmd:"log-miss"` and, when the repairing run cannot be identified, **omits `fix_run_id` deliberately** so the record costs `none`. Expect a non-trivial share of `CostAttribution == "none"` from this path, and treat it as correct rather than as missing data — the "unattributable fixes" count in the §3.4 `MissCost` shape is what surfaces it.

**0.5 — `ProjectType` can now be `framework`, and a repo can legitimately span two segments.** Two facts from the rollout:

- `framework` is detected **structurally** by the producer (a `scaffold-brownfield.sh` beside `.tfcore/tasks/`) and written nowhere, so it appears on records with no `core-config.yaml` support. Nothing to do beyond not treating it as unexpected.
- **The reclassification split is a real reporting hazard TfLens will hit first, because TfLens caused it.** TfLens was classified `docs` while carrying 225 gate records — every greenfield repo is born `docs` (at scaffold time there is no `src/`) and used to stay there. The producer now upgrades `docs` → `app` on refresh, but **already-written records keep `project_type:"docs"`**: streams are append-only and corrections happen at read time. Since §6 forbids pooling across `project_type`, one project legitimately appears under two segments with no visible reason. `tf-metrics.sh --report` states the split whenever a repo's current classification disagrees with its own records; **TfLens must do the same** — detect it, say it on the page, and describe each segment as a *period* of the project rather than as the whole of it. Silently, this makes one project look like two.

**0.6 — The parity dependency in §3.7 is satisfied.** `tf-metrics.sh --rollup --json` now reports a `misses` block, so the miss figures are diffable from day one and the "ship the page with figures marked unverified" fallback is not needed. The keys to match:

```
misses_total · miss_fixes_total · orphan_fixes · open_misses · wont_fix · resolved_misses
why_missed_n · why_missed{} · escapes_missing_why · why_missed_eligible · why_missed_predates_field
amendments_applied · orphan_amends
class_distribution{} · found_by{} · design_miss_share · escape_share
attributed_n · attribution_excluded · by_origin_phase{} · by_origin_model{} · by_origin_agent{}
cost_sole_n · cost_shared_n · cost_unattributable_n
tokens_per_miss_measured · tokens_per_miss_apportioned
cost_usd_per_miss_measured · cost_usd_records
```

Note `by_origin_agent` — the producer reports per-agent alongside per-model (they answer different questions: which model to route to vs. which persona's instructions to tighten). §3.4's table lists only phase and model; add the agent figure, under the same `linked`-only constraint and inside the same observational-labelling band.

**0.65 — There is a THIRD record kind: `miss-amend`.** Added the same day, from TfLens's own TF-006 report. §3.2's parser change is therefore a three-way dispatch on `kind` within `StreamKind.Misses`, not two:

| `kind` | Handling |
|---|---|
| `miss` | `MissRecord` |
| `miss-fix` | `MissFixRecord` |
| `miss-amend` | `MissAmendRecord` — `MissId`, `Field`, `Value` + the common set |
| anything else | `InvalidLines++`, skip (unchanged) |

**What it is:** an append-only way to *complete* a record — it may set a field that is `null` and may **never** overwrite one that is not, including a value an earlier amend set. It exists because constraint 5 ("the correction is a new record, never an edit") named a remedy this stream did not implement, so a `why_missed` left empty was unreachable.

**What TfLens must do with it:**

- **Fold amendments into the parent before computing anything**, oldest first, and **re-apply the null-check while folding** — do not trust that the producer already enforced it. TfLens ingests archived files from many machines, and a merged stream can carry an amend and a later-written value in either order. Only fields on the allowlist (`why_missed` today) and values inside its closed vocabulary may be applied.
- **An amend naming no known `miss`, or a field off the allowlist, is an orphan** — counted and surfaced on Coverage, never applied, exactly as an orphan `miss-fix` is.
- **Store the amend rows, do not collapse them at ingest.** Folding is a read-time operation over the stored records, so `RebuildAsync` replays and re-derives them like everything else. Natural key `(UserId, Repo, MissId, Field, Ts)`; a re-parse of the same archived file must not double-insert.
- The producer's parity keys gain `amendments_applied` and `orphan_amends` (§0.6).

**And the eligibility floor is now enforced upstream, so §0.1's denominator has a second term.** `tf-metrics.sh` carries `FIELD_SINCE = {"why_missed": "2026-08-28"}` beside its existing `LATE_GATES` table: a miss written before the field existed had no field to fill, so it leaves that field's denominator entirely and is reported separately (`why_missed_eligible`, `why_missed_predates_field`). TfLens must mirror both the floor and the two counts, or its `n of N assessed` will disagree with parity on any repo holding pre-2026-08-28 misses. It is the same rule as `LATE_GATES`, which TfLens already implements for `perf` — one table, one code path if you can manage it.

**0.7 — Both `n < 3` and the exclusions are already enforced in the producer's code**, not merely in prose, and both are printed with the counts that bound them. `Figure` and `MissAttributionTaint` therefore have a working reference implementation to diff against when parity disagrees.

---

## 1. What TfLens is being asked to do

TechieFlow gains a fifth stream, `docs/metrics/misses.jsonl`, carrying three record kinds: a **`miss`** (what was missed, which phase/agent/model let it through, who found it), a **`miss-fix`** (the repair run, its outcome, its token/cost window) and a **`miss-amend`** (§0.65 — completes a field the `miss` left `null`, never overwrites one). TfLens must pull it, archive it, parse it, store it, compute over it, and show it — under the same provenance discipline it already applies to the four existing streams.

The product's stated dangerous failure mode is *a plausible wrong number* (BRD §1). Miss data is the most seductive material in the whole system for producing one, because it invites two specific mistakes:

- **Presenting an apportioned cost as a measured cost.** A fix run that repaired three misses has one token window. Dividing by three is arithmetic, not measurement.
- **Presenting an inferred attribution as an observed one.** "This model produces the most misses" is a career-shaping claim if half the attributions were guessed.

Both are handled the way TfLens already handles live-vs-backfilled: **in the shape of the result type, with no switch to relax it.**

## 2. Rollout safety — TfLens needs no coordination window

`RepoRegistry` / `RepoSyncService` fetch a fixed list of stream file names. A repo that starts emitting `misses.jsonl` before TfLens knows the name is simply not read: no crash, no partial ingest, no wrong number. Deploy TechieFlow first and TfLens whenever convenient. The reverse order is also safe — a repo with no `misses.jsonl` produces an empty stream row, exactly as `commits.jsonl` does today on a clone that never committed.

## 3. Changes, layer by layer

### 3.1 Contracts — `src/TfLens.Core/Contracts/StreamRecords.cs`

Add `StreamKind.Misses = 5` and `StreamNames.Misses = "misses"`, appended to `StreamNames.TechieFlow` so it reports last. Extend `ToKind` / `ToName`.

Two new records, `MissRecord` and `MissFixRecord`, following the existing conventions exactly: `required int UserId` / `string Repo` / `string SourceSha` / `string Ts`, snake_case wire → PascalCase columns, **absent optional stays `null` and is never coerced to zero**, unrecognised properties into `Overflow`.

`MissRecord` columns: `MissId`, `ReqId`, `ReqClass`, `MissClass`, `Artifact`, `Severity`, **`WhyMissed`** (§0.1 — nullable, and `null` means *not assessed*, so never coerce it to a bucket), `OriginPhase`, `OriginAgent`, `OriginRunId`, `OriginConfidence`, `OriginModel`, `OriginHarness`, `FoundBy`, `FoundPhase`, `FoundGate`, `FoundRunId`, `FailureClass`, plus the §1 common set (`V`, `App`, `ProjectType`, `ProjectTypeInferred`, `Backfilled`, `Harness`).

`MissFixRecord` columns: `MissId`, `ReqId`, `FixRunId`, `FixCmd`, `FixAttempt`, `VerdictAfter`, `Reopened`, `CostAttribution`, `TokensIn`, `TokensOut`, `TokensCacheRead`, `TokensCacheWrite`, `CostUsd`, `TokensScope`, `Model`, plus the common set.

### 3.2 Parser — `src/TfLens.Core/Parsing/StreamParser.cs`

**This is the one genuinely structural change in TfLens.** Today `StreamKind` maps 1:1 to a table and `AddRecord` switches on the stream alone. `misses.jsonl` is the first stream whose records do not all have the same shape, so `AddRecord` must dispatch on the record's own `kind` **within** the `StreamKind.Misses` case:

- `kind == "miss"` → `MissRecord`
- `kind == "miss-fix"` → `MissFixRecord`
- `kind == "miss-amend"` → `MissAmendRecord` (§0.65)
- anything else → `InvalidLines++` and skip. **Not** an exception: the existing rule is that a malformed line is counted and skipped, never fatal (REQ-FN-032), and an unknown `kind` in a stream TfLens does know is the same class of event.

The documented-field sets follow the pattern already there — `MissDocumented` / `MissFixDocumented`, `MissMapped` / `MissFixMapped`, `MissKnown` / `MissFixKnown` — and `IsDocumented(StreamKind, string)` needs a case for `Misses`. Note the wrinkle it introduces: `IsDocumented` is keyed on stream, and `misses` has two field vocabularies. Take the **union** for the Coverage page's "fields observed that SCHEMA.md does not document" report, and say so in the XML doc — a `miss-fix`-only field observed on a `miss` record is not worth a separate report and would only produce noise.

The class-level XML `<remarks>` block is the project's mapping table of record. Extend it with the `misses` section in the same style.

**Dedupe** — `src/TfLens.Core/Parsing/Dedupe.cs`:

| Kind | Natural key | Collapse rule |
|---|---|---|
| `miss` | `(UserId, Repo, MissId)` | Keep the **earliest** `Ts`. A miss is opened once; a duplicate is a re-parse of the same archived file, not new information |
| `miss-fix` | `(UserId, Repo, MissId, FixRunId)` | Keep the latest `Ts` |
| `miss-amend` | `(UserId, Repo, MissId, Field, Ts)` | Keep the **earliest** `Ts`. Amendments are additive and each is a distinct fact; a duplicate is a re-parse of the same archived file (§0.65) |

Neither needs `merge=union` handling of the kind `commits` needs — misses are events on one machine and cannot be independently reconstructed elsewhere (SCHEMA.md §5's reasoning, applied unchanged).

### 3.3 Storage — `database/001-schema.sql` + `PostgresStore.cs`

Three tables, `"Miss"`, `"MissFix"` and `"MissAmend"` (§0.65), in the existing house style: every identifier double-quoted, `"UserId"` a real column and part of every unique index (ADR-013), `CREATE TABLE IF NOT EXISTS` so the file stays idempotent at every startup with no migration framework.

```sql
-- unique keys
CREATE UNIQUE INDEX IF NOT EXISTS "UxMiss"    ON "Miss"    ("UserId","Repo","MissId");
CREATE UNIQUE INDEX IF NOT EXISTS "UxMissFix" ON "MissFix" ("UserId","Repo","MissId","FixRunId");
CREATE UNIQUE INDEX IF NOT EXISTS "UxMissAmend" ON "MissAmend" ("UserId","Repo","MissId","Field","Ts");
-- read paths
CREATE INDEX IF NOT EXISTS "IxMissUserRepo"     ON "Miss"    ("UserId","Repo");
CREATE INDEX IF NOT EXISTS "IxMissOriginModel"  ON "Miss"    ("UserId","OriginModel");
CREATE INDEX IF NOT EXISTS "IxMissFixUserRepo"  ON "MissFix" ("UserId","Repo");
CREATE INDEX IF NOT EXISTS "IxMissFixMissId"    ON "MissFix" ("UserId","MissId");
CREATE INDEX IF NOT EXISTS "IxMissAmendMissId"  ON "MissAmend" ("UserId","MissId");
```

`ITelemetryStore` gains `ReadMissesAsync`, `ReadMissFixesAsync` and `ReadMissAmendsAsync` mirroring `ReadGatesAsync`'s signature. `UpsertAsync` handles the three new `ParseResult` collections. `DeleteRepoDataAsync` must purge **all three** tables — miss one and removing a repo leaves orphaned rows that reappear in every figure, which is the worst kind of bug in a product whose promise is correct numbers. `RebuildAsync` replays them from the raw archive like everything else; **amendments are folded at read time, never at ingest**, so a rebuild re-derives the same values (§0.65).

`SyncState` gains a `misses` row per repo; `Coverage`'s per-repo stream table goes from four rows to five.

### 3.4 Metrics engine — `src/TfLens.Core/Metrics/`

New `MissMetrics.cs`, plus the two provenance guards below. Every figure returns a `Figure`, so `insufficient data (n=…)` and *not applicable* are unrepresentable as numbers — the existing ADR-007 protection carries over for free.

**Figures to compute** (all live-only, segmented per `project_type` via `Segment.cs`, exactly as the existing three questions are):

| Figure | Definition | Extra constraint |
|---|---|---|
| Miss rate per origin phase | `miss` grouped by `OriginPhase`, over `Run` records of that `Cmd` | `OriginConfidence == "linked"` only |
| Miss rate per origin model | grouped by `OriginModel` | `OriginConfidence == "linked"` only |
| Miss rate per origin agent | grouped by `OriginAgent` (§0.6) | `OriginConfidence == "linked"` only |
| Miss class distribution | count of `MissClass` | — |
| **Failed-practice distribution** | count of `WhyMissed` (§0.1) | **Denominator is records where `WhyMissed != null`**, printed as `n of N assessed` — never all misses |
| Design-miss share | `MissClass == "unspecified-gap"` ÷ all | — |
| Escape share of misses | `FoundBy ∈ {owner, production}` ÷ all | Rendered **beside** the existing `gates`-derived escape rate, never merged |
| Open misses | `miss` with no matching `miss-fix`, or whose latest `miss-fix.VerdictAfter ∉ {Verified, wont-fix}` | **Superseded by §0.3** — `wont-fix` is its own bucket, `deferred` stays open |
| Declined misses | latest `MissFix.VerdictAfter == "wont-fix"` | Its own tile; never folded into open (§0.3) |
| Tokens per miss fixed | Σ `TokensOut` ÷ count over `MissFix` | `CostAttribution == "sole"` only |
| Measured cost per miss fixed | Σ `CostUsd` | **OpenCode records only** — `Pooled.cs` already owns this rule |
| Median time-to-close | `MissFix.Ts − Miss.Ts` | — |

**Guard 1 — `MissAttributionTaint.cs`.** A sibling of the existing `TaintSet.cs`. `TaintSet` excludes REQs with any backfilled gate record from the live first-pass rate; this one excludes `OriginConfidence != "linked"` records from every per-model, per-agent and per-phase figure, and — like `TaintSet` — is both **applied and displayed**. The page states how many misses were excluded and why. An exclusion the reader cannot see is indistinguishable from a bug.

**Guard 2 — apportioned cost never returns as a plain figure.** Give the token/cost figures a return type that carries the attribution split, e.g.

```csharp
public sealed record MissCost(Figure Sole, Figure Apportioned, int NoneCount);
```

A page binding `MissCost` cannot render a single blended number, because no such property exists. This is the same technique as `Figure` itself: **make the wrong number unrepresentable rather than forbidden.**

`MetricsEngine` / `CachingMetricsEngine` thread the new reads through; `AnalysisResult` gains a `Misses` section; `AnalysisCacheInvalidator` invalidates on `misses` sync as it does for the other streams.

### 3.5 Cost display — reuse `RateCard`, do not extend the streams

The owner's question is "how much money did the miss cost". The honest answer differs by harness, and TfLens already has the machinery:

| Harness | On the page |
|---|---|
| OpenCode | **Measured USD** — from `CostUsd`. The only measured dollars in the product |
| Claude Code | **Tokens** as the primary figure; USD only via `RateCard`, carrying `RateCard.EstimateLabel` and a `_usd_estimate` key in every export |
| Codex | As Claude Code |

No new machinery, no new rule, and no dollars in the streams. `RateCard.FileNote` already says it better than a new doc would: *"Nobody was billed these amounts."*

### 3.6 UI — a sixth report page, `/misses`

`ShellNavigation.Items` gains one entry, between Routing and Export:

```csharp
new ShellNavItem("/misses", "Misses & rework", "bug", ReportsSection, false, true)
```

`HasFrameworkSwitch: true` — the framework switch renders, and on the Playbook axis the page shows `PlaybookEmpty` until the Playbook emits miss data (see the sibling document). That is the established pattern for a surface one framework has and the other does not; do not hide the switch.

**Page shape** — four bands, in the order that answers the owner's question:

1. **KPI row** (`StatGroup` / `StatTile`): open misses (§0.3 predicate) · declined (`wont-fix`) · misses this period · design-miss share · escape share · tokens spent on rework · measured USD on rework (OpenCode only, with the harness named on the tile).
2. **Where misses come from** — origin phase × miss class, with the excluded-attribution count stated beneath. This is the "did we specify badly or build badly" answer. Beside it, the **failed-practice** distribution (`WhyMissed`, §0.1) carrying its own `n of N assessed` denominator on its face — it answers the sharper version of the same question: *specification weak, or verification weak?*
3. **Who was running** — origin model and origin agent, `linked` records only, with the taint count visible. **Label this band as observational.** Miss counts per model are confounded by which model gets the hard work; a page that implies causation invites a bad routing decision. One line of standing copy, not a tooltip.
4. **Cost of rework** — the `MissCost` split rendered as it is shaped: a measured column, an apportioned column, and a count of unattributable fixes. Never one blended number.

A per-miss detail table (id, REQ, class, severity, origin, found by, status, tokens) with the raw record behind a disclosure, matching how the existing pages let a reader reach the evidence.

**Charts:** load the `dataviz` skill before writing any chart code, and keep to the existing palette so the page reads as part of the same system.

### 3.7 Coverage, Export, parity

- **Coverage** (`Coverage.razor`, `CoverageFacts.cs`, `ReadCoverageFactsAsync`): the per-repo stream table becomes five rows; the unknown-field report covers the new stream; the health badge counts a repo emitting misses with no `miss-fix` records at all as a **warning, not an error** — likely the fix path is not wired yet, which is worth saying and not worth failing on. Two more Coverage facts from §0: **`escapes_missing_why`** (escapes arriving with no `WhyMissed` — a data-quality signal, not a quality one) and the **classification split** (§0.5 — a repo whose records carry a `ProjectType` its current classification disagrees with, which must be stated rather than silently rendered as two projects). Orphan `miss-fix` records (`MissId` matching no `miss`) are counted here too, never dropped.
- **Export** (`SnapshotExporter`, `SnapshotJson`, `SnapshotMarkdown`): a `misses` section. Estimated-dollar keys end in `_usd_estimate`; measured ones do not. The attribution split survives into the JSON as three distinct keys — never collapsed for tidiness.
- **Parity** (`ParityRecord`, BRD §13): parity is the acceptance gate before any figure is quotable, so the new figures need their counterpart in `tf-metrics.sh --rollup --json`. **That dependency is now satisfied** (§0.6) — the producer shipped 2026-08-28 and reports a `misses` block with the keys listed there, so every miss figure is diffable from the first commit and none needs to ship marked unverified. Diff against those key names exactly.

### 3.8 Tests

- `StreamParser` tests: all three kinds on one file; an unknown `kind` counted as invalid, not thrown; overflow fidelity; **`null` vs `0` on every nullable** (the existing `StoreNullVsZero` discipline).
- `Dedupe` tests: earliest-wins for `miss`, latest-wins for `miss-fix`, earliest-wins per `(MissId, Field)` for `miss-amend`.
- **Amend-folding tests (§0.65), the invariant this stream stands on:** an amend fills a `null` ✓ · an amend **never** overwrites a non-`null` value, whichever order the two records arrive in ✓ · a second amend of the same field is ignored ✓ · a field off the allowlist or a value outside its vocabulary is never applied and counts as an orphan ✓ · an amend naming no known `miss` counts as an orphan ✓ · a `why_missed` supplied only by an amend reaches the failed-practice distribution ✓.
- **Eligibility-floor test:** a miss with `Ts` before `FIELD_SINCE["why_missed"]` is outside that field's denominator and is counted separately — the same shape as the existing `LATE_GATES` test for `perf`.
- Metrics tests: a `shared:3` record never reaches the sole column; a non-`linked` record never reaches a per-model figure; `CostUsd` never sums across harness (`Pooled.cs` already has this test shape).
- Guardrail test: `DeleteRepoDataAsync` leaves zero rows in both new tables.
- Playwright: `/misses` renders every control with data (render gate) and is clean at 1280 and 390 (visual gate).

## 4. BRD and checklist changes

TfLens's BRD lists **five** report pages; this makes six, so it is a scope change and goes through the framework's own process rather than being slipped in.

1. **BRD amendment** — a new `### Amendment 2026-08-28 — miss telemetry and rework economics` under §11, in the style of the two existing amendments.
2. **New feature `F-MISS`** in §9, and one new line in the §9 screen inventory.
3. **New BRD ledger entries** in §10 (continuing from BRD-111) covering: pull/archive/parse the fifth stream · two record kinds on one stream · the two new tables and their purge · the miss figures · the attribution taint rule · the apportioned-cost result shape · the `/misses` page · the Playbook empty state · export keys · parity coverage.
4. **New REQ rows** via `*split-brd` or `*amend-docs`: roughly `REQ-UI-0xx` for the page and its bands, `REQ-FN-0xx` for parser/store/engine/export/parity, and at least one `REQ-NFR-0xx` for the "no blended cost figure, no pooled attribution" invariant — the invariants are the product here, so they get their own acceptance criteria rather than living in prose.
5. **Phase:** this is Phase 3 work, after the existing Playbook items.
6. **`DECISIONS.md`** — a new ADR recording the two decisions that will be questioned later: *two record kinds on one stream* (and why not two files), and *apportioned cost gets a distinct result type* (and why not a flag).

## 5. What TfLens must not do

- **Must not blend measured and apportioned cost** into one figure, anywhere — page, export, or parity.
- **Must not compute a per-model or per-agent figure from `inferred` attributions**, and must not hide how many were excluded.
- **Must not render the `WhyMissed` distribution over all misses.** The field is optional and `null` means *not assessed*; using the miss count as the denominator understates every category and prints a plausible wrong number (§0.1).
- **Must not fold `wont-fix` into open misses**, or reconcile the two open-predicates in §0.3 — they answer different questions and agreeing would break one of them.
- **Must not present rate-card dollars as spend.** `RateCard.EstimateLabel` on every such figure, `_usd_estimate` on every such key.
- **Must not fold miss records into the existing escape rate.** Escape rate keeps its definition and its source (`gates.jsonl`); the miss-stream's escape share is a second, adjacent figure. Two definitions of one word on one page is how a report loses its reader.
- **Must not write to any repo.** TfLens is read-only over telemetry (BRD §1) and stays that way.
- **Must not fail a sync because a miss record is malformed.** Count it, skip it, surface it on Coverage — the same contract as every other stream.

## 6. Decisions taken (owner-approved 2026-08-28)

**6.1 — `/misses` is its own page, a sixth nav item.** Not a band on Three questions. The three questions are canon and a well-understood surface; adding a fourth would dilute it, and misses carry a cost dimension the three questions deliberately exclude.

**6.2 — The page defaults to ALL HISTORY, with a period filter.** Miss counts are low-volume; a default period would routinely render `insufficient data (n=…)` on a page whose entire job is to show the owner a trend. The filter narrows; it does not gate the first view.

**6.3 — Priced dollars for Claude/Codex are shown by default, on a visibly distinct tile.** The owner's question is a money question, so hiding the only available answer behind a toggle defeats the page. The estimate tile must be visually separated from the measured-OpenCode tile — a different tile treatment and `RateCard.EstimateLabel` on its face, never the same row, never the same styling. A reader who cannot tell the two apart at a glance has been given a plausible wrong number, which is the failure mode this product is built against.
