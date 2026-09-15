# TfLens — TechieFlow framework feedback

| | |
|---|---|
| App | TfLens |
| Upstream | TechieFlow |
| Updated | 2026-09-15 |

Defects found in the **TechieFlow framework itself** (`.tfcore/`) while building TfLens. That directory
is owned and maintained by the TechieFlow team and is gitignored here — `update-framework.sh` overwrites
it — so nothing in it is fixed locally. This file is the hand-off: each entry is reproducible, with the
evidence and a suggested fix.

Same schema as the per-library feedback files (Severity / Repro / Expected / Actual / Encountered in /
Workaround / Suggested fix). One file per upstream owner; this one is TechieFlow.

---

## Summary

**Nothing is blocked.** 52 entries: none open, 16 fixed upstream and waiting to be re-checked here (TF-018, TF-020, TF-021, TF-025 to TF-028, TF-030, TF-033 to TF-035, TF-042 to TF-044, TF-051, TF-052), 36 closed. TF-051 and TF-052, filed from the 2026-09-15 fix, are fixed the same day. TF-049 and TF-050, filed at the handoff and fixed the same day, were re-checked and closed here on 2026-09-15. TF-045 to TF-048 were re-checked and closed here on 2026-09-14. TF-013 to TF-017, TF-019, TF-022 to TF-024, TF-029, TF-031, TF-032 and TF-036 were re-checked and closed here on 2026-09-11, and TF-037 to TF-041 on 2026-09-12 (the account is in `docs/TfLens-Feedback-Recheck-2026-09-12.md`). Every problem TfLens filed up to TF-052 is fixed in the framework: TF-013 to TF-022 on 2026-09-09 (the reply that day left out TF-013 to TF-017, which is why they showed open here), TF-023 to TF-036 on 2026-09-11, TF-037 to TF-041 on 2026-09-12, TF-042 to TF-045 on 2026-09-13, TF-046 to TF-050 on 2026-09-14 and TF-051 and TF-052 on 2026-09-15. A fix counts as done only once it has been re-checked here: run each entry's "Verify from here" step in its Resolution status block, then close it with `bash .tfcore/utils/tf-feedback.sh TfLens --close <ID> "<what you ran and what it showed>"`. `bash .tfcore/utils/tf-feedback.sh TfLens` prints the state of every entry. Never describe a fixed entry as an open problem.

#### Detail

The bullets below are the history as it was written at the time. The state today is the paragraph above.

- **`TF-020` (new 2026-09-09, major)** — `tf-metrics.sh`'s `seg()` never reads `req_class`, so the
  framework's own `FR` requirement verdicts are pooled into whichever application `project_type` segment
  their records carry — which SCHEMA.md §3 forbids in as many words. It is the one item of the
  2026-09-07 amendment the reference did not implement, and it flatters an application segment's
  first-pass rate. One line in `seg()`. **No TfLens figure is affected** — TfLens segregates them at the
  segment key (REQ-FN-110).
- **`TF-018` (new 2026-09-09, minor)** — `tf-emit.sh` refuses to write a `what` amendment that TfLens's
  amended BRD-116 requires, and `tf-metrics.sh`'s `FIELD_SINCE` carries no floor for `what`. Two small
  edits, or an explicit decision that the owner and the framework disagree. `sort` agrees exactly in
  both directions. **No wrong figure, nothing blocked.**
- **18 entries.** `TF-001`–`TF-004` closed. `TF-005` **confirmed fixed upstream and now matched by
  TfLens** (`REQ-FN-079`, 2026-09-02). `TF-013`, `TF-014`, `TF-015` and the new `TF-016` and `TF-017` are open. `TF-007`–
  `TF-012` are recorded below as fixed upstream on 2026-08-31 and **have not been re-verified here** —
  and at least one of them is not fixed in practice: **`TF-012` still fires on every screen**, nine
  `app-sidebar clip@1280` findings in the 2026-09-02 `mockup-parity` run, against a `.tfcore/` that
  already carries the 2026-08-31 scripts (`tf-metrics.sh` hashes `8759f71d…`). Treat the block below as
  the team's report, not as a verified state.
- **`TF-017` (new 2026-09-08, Low)** — `tf-split-brd.py --add-missing` finds **zero** requirements in a BRD
  whose ledger lines carry the `<a id="brd-N">` anchors that the §9 cross-links need and that
  `tf-doc-check.sh` refuses to see broken. The two scripts disagree about how a ledger item is written;
  one optional group in the regex fixes it. The twelve new checklist rows were written by hand instead.
  **No wrong figure, nothing blocked.**
- **`TF-016` (new 2026-09-08, Low)** — `*amend-docs` has no step that closes a miss it fixed, so a miss
  whose deficient artifact is a document stays open forever. The closing machinery is fine and
  `fix_cmd: "amend-docs"` is already legal in the schema; only the wiring is missing. 12 finished items
  in TfLens have shown as outstanding for up to two weeks. **No wrong figure, nothing blocked.**
- **`TF-015` (new 2026-09-02, High)** — `tf-emit.sh` accepts a run whose `ended` precedes its `started`;
  the two consumers then disagree about the negative duration, so the same corpus yields two different
  totals and neither flags the record. 14 such records exist in one repository, 13 invisible.
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
     of the 2026-08-31 block with the one-line fix. **The `sole` recount defect WAS present in TfLens** —
     it reported `cost_sole_n` 10 against the reference's 7, putting three multi-miss windows into the
     measured column — and was fixed on 2026-09-02 (`REQ-FN-079`). The warning was accurate.
- Last consolidated: **2026-09-14** (Phase 3 handoff); before that 2026-09-02 (handoff).

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

## Resolution status (TechieFlow team, 2026-09-15)

**TF-051 and TF-052 are fixed in the framework and deployed to this repository.** Nothing is blocked. Cases `tf_051` and `tf_052` in `tests/regression/run.sh`, each failing against the scripts you had; misses `MISS-TechieFlow-20260915-01` and `-02`. Close each with `bash .tfcore/utils/tf-feedback.sh TfLens --close <ID> "<what you ran and what it showed>"`.

| ID | Fix | Verify from here |
|----|-----|------------------|
| **TF-051** | `tf-triage.sh` `demote`, `new` and `note` take `--phase N`, as `tf-verify-list.sh` does. Without it, `demote` and `note` look for the row in `docs/TfLens-Checklist.md` and every `docs/TfLens-P<N>-Checklist.md`, write it where it is and say so; a row in two checklists asks for `--phase`, and an id in none is refused, naming every checklist searched. Proved on a copy of this repository's checklists: REQ-NFR-003 went from `Verified` to `Needs re-verify` in `docs/TfLens-Checklist.md` and the P3 checklist did not change; the old script printed your error. | `d=tests/.artifacts/tf051 && rm -rf $d && mkdir -p $d/docs $d/.tfcore && cp .tfcore/core-config.yaml $d/.tfcore/ && cp docs/TfLens*-Checklist.md $d/docs/ && ( cd $d && bash ../../../.tfcore/utils/tf-triage.sh TfLens demote REQ-NFR-003 "re-check TF-051" --kind data-logic )`: prints `note: REQ-NFR-003 is a row of docs/TfLens-Checklist.md, not of docs/TfLens-P3-Checklist.md; written there` and `REQ-NFR-003: Verified → Needs re-verify — re-check TF-051`, exit 0. It works on a copy, so your checklist is not touched. |
| **TF-052** | Three script changes. **(1)** `tf-phase.sh start` run inside another command keeps that command in the marker as `outer`; the new command's own start is still what every reader takes. **(2)** `tf-verify-emit.sh`, handed the caller's start, records the verify from its own start (its marker, or when its list was written if its step 0 was skipped) and prints which. **(3)** `tf-fix-close.sh` records the fix only in the time around any run it chained, with the rows on the first part, and takes each row's verdict from the gate records of every verify since the fix started, then from `docs/.last-verify.json` only when that ledger belongs to this fix. A row no verify graded gets no miss-fix and is named. `fix-issues.md` step 4 now says the inline verify runs its own step 0, and rows of another phase are a second verify with `--phase N`. Replayed on a copy of this repository's stream with your fix's start, 2026-09-15T12:41:41Z: the run record is written, and MISS-TfLens-20260911-39 (REQ-UI-072) gets `Verified` from the Phase 3 verify's gate record; the old script refused the record and wrote `Needs re-verify`. Your 2026-09-15 records stay as they are: the verify record holds 20 minutes that were partly the fix's, but no minute is counted twice. | `TF_REGRESSION_UTILS="$PWD/.tfcore/utils" bash /mnt/c/3AIGenCode/TechieFlow/tests/regression/run.sh tf_052` (on the Mac the suite is under `/Users/MyCode/TechieFlow`): four `ok` lines, `tf_052a` to `tf_052d`, run against this repository's own copy of the scripts. |

---

## Resolution status (TechieFlow team, 2026-09-14, fourth reply)

**TF-049 and TF-050 are fixed in the framework and deployed to this repository.** Cases `tf_049` and `tf_050` in `tests/regression/run.sh`; misses `MISS-TechieFlow-20260914-12` and `-13`. Close each with `bash .tfcore/utils/tf-feedback.sh TfLens --close <ID> "<what you ran and what it showed>"`.

| ID | Fix | Verify from here |
|----|-----|------------------|
| **TF-049** | `tf-build.sh --print` (and `test --print`) resolves the target and prints the command, nothing else, exit 0; it takes no lock. On this repository it prints `dotnet build TfLens.slnx`. | `bash .tfcore/utils/tf-build.sh --print` prints `dotnet build TfLens.slnx`, exit 0. |
| **TF-050** | The route scan skips the sample and test folders and any `*.spec.*` or `*.test.*` file, so a test's screenshot `path:` is not a route; `@page` and a client route list still are. On this repository the two `.png` lines are gone and 19 routes remain. | `bash .tfcore/utils/tf-devguide-list.sh TfLens`: no `.png` under "Pages in code with no UIDesign screen". |

---

## Resolution status (TechieFlow team, 2026-09-14, third reply)

**TF-048 is fixed in the framework and deployed to this repository.** Case `tf_048` in `tests/regression/run.sh`; requirement FR-73; miss `MISS-TechieFlow-20260914-10`. Close it with `bash .tfcore/utils/tf-feedback.sh TfLens --close TF-048 "<what you ran and what it showed>"`.

| ID | Fix | Verify from here |
|----|-----|------------------|
| **TF-048** | The lock `tf-build.sh` takes now lives in `tf-lock.sh`, and `tf-verify-tests.sh`, `tf-verify-screens.sh` and `tf-mockup-parity.sh` take it too, so a second one prints `wait  another build or browser check is running in this repository (…)` and starts when the first finishes. A script the holder starts shares it, so the tests runner's own `tf-build.sh test` does not wait on itself. FR-73 says it in one line. | Start `tf-verify-tests.sh --base <url>` and, while it runs, `tf-mockup-parity.sh --base <url> --screen misses=/misses --cookie …`: the second prints the `wait` line and runs after the first; parity then reports what it reports alone. |

---

## Resolution status (TechieFlow team, 2026-09-14, second reply)

**TF-047 is fixed in the framework and deployed to this repository.** Nothing is blocked. Run the
row's "Verify from here" step, then close the entry with
`bash .tfcore/utils/tf-feedback.sh TfLens --close TF-047 "<what you ran and what it showed>"`.
TechieFlow never closes an entry for you. The fix carries case `tf_047` in `tests/regression/run.sh`,
which fails against the script you had and passes now; it is logged as `MISS-TechieFlow-20260914-04`.

| ID | Fix | Verify from here |
|----|-----|------------------|
| **TF-047** | Three changes, one per finding kind. **(1)** A badge or border is accepted one element away. When one side draws the fill or the ring and the other does not, the plain side is read at the box around it, when that box is the same row and its other children carry no text (an icon, or the box a library wraps one in), or at its only child. So the active link inside its `<li>` and the ring on the `InputGroup` around the filter input count as the same treatment. Two plain elements still agree as before, and a dashed rule against a solid one is still reported. **(2)** An `icon` finding is dropped when the parent or the grandparent is paired and holds the same number of icons on both sides, up to four: the icon is under the same header, in another child. A header that really gains or loses an icon is still reported. **(3)** `wrap` is graded only when the two texts match with digits folded; on different text it is not applicable and not counted as graded. The same text in a narrower box is still reported. Proved on this repository's app, signed in as the demo user from `tests/verify/_helpers.ts`, on `/misses`, `/effort` and `/prices` at 1280 and 390, old and new script run together on the same data: your 15 findings became 0 and all three screens PASS; the comparison counts (242, 248, 222) are unchanged, and `wrap` on `/misses` is graded on 18 pairs instead of 26. | `bash .tfcore/utils/tf-mockup-parity.sh --base <url> --screen misses=/misses --screen effort=/effort --screen prices=/prices --cookie …`: exit 0, three PASS verdicts, `findings_n` 0. The before-and-after JSON from our run is in `tests/.artifacts/fix-parity/tf047/` (`old-2.json`, `new-2.json`). |

---

## Resolution status (TechieFlow team, 2026-09-14)

**TF-046 is fixed in the framework and deployed to this repository.** Nothing is blocked. Run the
row's "Verify from here" step, then close the entry with
`bash .tfcore/utils/tf-feedback.sh TfLens --close TF-046 "<what you ran and what it showed>"`.
TechieFlow never closes an entry for you. The fix carries case `tf_046` in `tests/regression/run.sh`,
which fails against the script you had and passes now; it is logged as `MISS-TechieFlow-20260914-03`.

| ID | Fix | Verify from here |
|----|-----|------------------|
| **TF-046** | The page probe numbers every icon on the page and records which icons each element holds. An `icon` finding is dropped when every icon it holds is already reported on a deeper element, so one icon gives one finding, on the innermost element that carries it. A container that holds an extra icon of its own is still reported, and the same rule applies the other way round, to an icon the mockup draws and the app lost. Proved on this repository's app, signed in, on `/misses`, `/effort` and `/prices` at 1280 and 390, with the old and the new script run against the same data at the same time: 26 `icon` findings became 12, each of the 14 removed was an element containing one that was kept, and the 16 other findings and the three FAIL verdicts did not change. | `bash .tfcore/utils/tf-mockup-parity.sh --base <url> --screen misses=/misses --screen effort=/effort --screen prices=/prices --cookie …`: 12 `icon` findings; none keyed `misses-page > div[0]`, `effort-page > div[0]` or `effort-routing`; `misses-period` and `effort-period` reported once per width. |

---

## Resolution status (TechieFlow team, 2026-09-13)

**All four problems filed on 2026-09-12 and 2026-09-13 are fixed in the framework and deployed to
this repository.** Nothing is blocked. Run each row's "Verify from here" step, then close the entry
with `bash .tfcore/utils/tf-feedback.sh TfLens --close <ID> "<what you ran and what it showed>"`.
TechieFlow never closes an entry for you. Every fix carries a case in `tests/regression/run.sh` that
fails against the scripts you have and passes now; they are logged as `MISS-TechieFlow-20260913-01`
to `-05`.

| ID | Fix | Verify from here |
|----|-----|------------------|
| **TF-042** | The clip check now asks whether anything really cuts the content off. An element whose overflow is visible draws what spills past its edge in full, so it is reported only when an ancestor that clips ends first. While fixing it we found a slip of ours from TF-039: a card that really clips, holding screen-reader text, cut its own content before measuring it and read as holding everything. That is fixed too (`MISS-TechieFlow-20260913-02`). | `bash .tfcore/utils/tf-mockup-parity.sh --base <url> --screen effort=/effort --widths 1280`: no `clip` finding keyed `app-sidebar`. Next `*verify ui`: the 13 rows it held back can reach Verified on this check. |
| **TF-043** | Three changes. **(1)** `tf-verify-boot.sh start` no longer runs `dotnet run` over the shared `bin/` and `obj/`. It publishes the project in Debug into `tests/.artifacts/verify/run-<port>/` and runs that copy, reading settings, user secrets and files beside the project from the project folder, as `dotnet run` does. The copy sits inside the repository, so your search for `database/001-schema.sql` upward from the binary still finds it. **(2)** `tf-build.sh` runs one build, test or publish at a time per repository: a second one waits and prints `wait  another build is running`, and a lock left by a build that died is taken over. **(3)** `stop --port <n>` also stops an app whose starter died, found by the copy it runs from. Proved on a real Blazor Server app: under the old script a sibling build changed the stylesheet the running app served; under the new one the running app kept serving its own copy byte for byte while the shared build folder carried the change, and every scope stamp on the page matched a rule in its stylesheet. Not exercised here: the Windows-side run of the copy (`cmd.exe`), which is used only when the WSL rungs cannot publish. | Start two apps during a build: `bash .tfcore/utils/tf-verify-boot.sh start --port 5361` and `--port 5371`. Each BOOTED line names `copy=tests/.artifacts/verify/run-<port>`. Run `bash .tfcore/utils/tf-build.sh` in another shell, then `assertScopedCssLoaded` passes on both ports. The hand-published copies are no longer needed. |
| **TF-044** | Sign-in waits for the page to settle, types, checks that the typed values are still in both fields, and only then presses the button. When the page stays on the sign-in address it types and presses again, up to four times. The user field is chosen by the best-matching rule first, never the first text box in the page, so a search box before the email field is not typed into. Proved on a real Blazor Server sign-in page that becomes interactive after it is drawn: the old sign-in failed one run in three, landing on `/login?`; the new one signed in three runs of three. | `bash .tfcore/utils/tf-verify-screens.sh --list … --base <url> --login-path /login --user <u> --password <p>`: no `LOGIN failed` line, and `screens.json` shows `login.ok: true` with the attempts it took. `--storage-state` is no longer needed. |
| **TF-045** | Four changes. **(1)** A badge the mockup draws is looked for in the app by its text under the same anchor, at any depth, with numbers allowed to differ, so live "87 of 91 misses assessed" matches the mockup's "39 of 41". Once found it is compared like any paired element: a wrong colour or ring is still reported, and so is a badge turned into plain text. **(2)** A badge found neither by text nor by position is missing only while the app draws fewer badges under that parent than the mockup does, the rule icons have followed since TF-027. **(3)** A reported badge says what was seen: "flattened into plain text" only when the app shows that text, otherwise "could not be located". **(4)** Colours are grouped by hue, so a deep amber and a light amber are both warning. **Not changed:** an icon the app carries and the mockup does not is still reported, because the mockup is the design and should settle it; a wrap caused by longer live data is still reported; and a border drawn on an `InputGroup` rather than its input still compares by position. | Next `*verify` on `/misses`, `/effort` and `/prices`: no `missing` finding on `miss-origin`, `miss-whymissed` or `miss-review-cost`; `coverage.relocated` in the JSON counts the badges found by text; the `bg-alert-warning-bg` tile has no colour finding. |

**One thing on your side of this file.** `bash .tfcore/utils/tf-doc-check.sh docs/TfLens-TechieFlow-Feedback.md`
fails on your entries TF-043, TF-044 and TF-045: each is longer than the 250-word maximum, and TF-045
writes `**Repro.**`, `**Expected.**` and `**Actual.**` where the check reads `Repro:`, `Expected:` and
`Actual:`. The detail is useful; move what goes past the limit into a note of your own and link it
when you close the entries.

---

## Resolution status (TechieFlow team, 2026-09-11)

**Every problem TfLens has filed is now fixed in the framework.** Fifteen are fixed upstream and wait
for TfLens to re-check them; the other twelve were closed here earlier. Nothing is blocked, and
nothing needs changing to keep TfLens working. Deploy first — `bash
/mnt/c/3AIGenCode/TechieFlow/update-framework.sh /mnt/c/1MyCode/TfLens` — then run each row's
"Verify from here" step and close the entry with
`bash .tfcore/utils/tf-feedback.sh TfLens --close <ID> "<what you ran and what it showed>"`.
TechieFlow never closes an entry for you.

| ID | Fix | Verify from here |
|----|-----|------------------|
| **TF-013** | Fixed 2026-09-09; our reply that day left it out, so this file showed it open. A new hook, `guard-verify-deps.sh`: a verify run may start a service the project defines, never create one, and never point the app at another database. | `printf '%s' '{"tool_name":"Bash","tool_input":{"command":"docker compose up -d"}}' \| bash .tfcore/hooks/guard-verify-deps.sh; echo $?` prints a refusal and `2`. |
| **TF-014** | Fixed 2026-09-09, reply left out the same way. `tf-gitignore-audit.sh` no longer skips dot-folders; `.vs/` and `.idea/` are checked whatever the stack. | `bash .tfcore/utils/tf-gitignore-audit.sh . --dry-run` names any `.vs/` or `.idea/` path that is not ignored. |
| **TF-015** | Fixed 2026-09-09, reply left out. The emitter refuses a run that ends before it starts, and every reader takes a duration from one place, so the two readers of a stream agree. | `bash .tfcore/utils/tf-selfcheck.sh` reports your one old impossible record as "discarded from every duration figure". |
| **TF-016** | Fixed 2026-09-09, reply left out. `*amend-docs` has a closing step for a miss whose fix is a document. | `grep -n tf-fix-close .tfcore/tasks/amend-docs.md` shows the step; `bash .tfcore/utils/tf-emit.sh --open-misses TfLens --artifact-class doc` lists the document misses it can close. |
| **TF-017** | Fixed 2026-09-09, reply left out. `tf-split-brd` reads a requirement line that carries its `<a id>` anchor. | `bash .tfcore/utils/tf-selfcheck.sh` prints "tf-split-brd TfLens: reads all 86 BRD items" for phase 3. |
| **TF-019** | Fixed 2026-09-09 (block below). Your session of 2026-09-11 confirmed the build list offers every not-started row. | Nothing to run. Close it. |
| **TF-022** | Fixed 2026-09-09 (block below). Your verify of 2026-09-11 09:24 graded `REQ-UI-034` and `REQ-UI-039` Verified; the old FAIL on both came from the run of 2026-09-09 14:54, before the fix was deployed. | Nothing to run: both rows are Verified in `TfLens-P3-Checklist.md`. Close it. |
| **TF-023** | Both places in `docs/Decision-TfLens-Duration-Parity-2026-09-09.md` (TechieFlow repository) now say the four new counts go in the top-level key list only, and that each phase's own duration list keeps its five keys. | Your `tools/parity-compare.py` already does this. Read the two places and close it. |
| **TF-024** | New `app-phase-brd-tmpl.md`: Summary, Screens and flow, Requirements, Non-functional (only what the phase adds), Development status, Where the rest lives — which must link to the phase-1 BRD. The checker uses it for `{App}-P<n>-BRD.md`; day-1 writes phase 2 onward from it. **The same was done for phase UI designs** (`app-phase-uidesign-tmpl.md`: screens only, plus the link back), at the owner's request. | `bash .tfcore/utils/tf-doc-check.sh --app TfLens --strict`. The phase BRDs go from 20 findings to 6, the phase UI designs lose their 10 phase-1 findings. What remains is real and repaired through `*amend-docs`: a Feature catalog section (removed from every BRD at the reset), Development status placed second, a missing "Where the rest lives" section in each phase UI design (your pointer is a sentence, not the section), and a "Library gaps" section that belongs in the TrBlazeUI feedback file. |
| **TF-025** | `--add-missing` counts every `BRD-N` anywhere on a status-table row, and on a `*BRD:*` detail line, as already tracked. | On a copy of your phase-3 shape one new item appended one row, where the old script appended four. Next `*amend-docs` that adds an item: the printed list names only the new item. |
| **TF-026** | The screen check reads `data-testid` only on elements. Style rules, script strings and comments are ignored. | Next `*verify`: "anchored control source-mode is not on the page" no longer appears for `/misses` or `/effort`. |
| **TF-027** | Two fixes. **(1)** The root cause of the colour findings was not the wrapper: TrBlazeUI writes its colours as `oklch(…)`, and the tool read those three numbers as red, green and blue, so every tile the library colours read as neutral. The browser now converts the colour. **(2)** An icon counts as missing only when the matching parent in the app carries fewer icons than the mockup's, and never more reports than icons short. | Next `*verify`: the sidebar-icon and tile-colour findings on `/misses` and `/effort` are gone. A genuinely missing icon is still reported — pinned by a case. |
| **TF-028** | Filed and fixed 2026-09-11, after the rows above. The heading pattern in `tf_feedback.py` now stops at the end of the heading line (`[ \t]` in place of `\s`), so a bare `## TF-013` has no title and the closing line under it counts. Run on all 24 feedback files in the 20 repositories that carry the script, the old and new versions agree on every entry except that one title. The fixed file is already in TfLens. | `python3 -c "import sys;sys.path.insert(0,'.tfcore/utils');import tf_feedback as t;print(repr(t.ENTRY_HEAD.search('## TF-013\n\n- **Severity:** major\n').group(3)))"` prints `''` (it printed `'**Severity:** major'` before the fix). |
| **TF-029** | Filed and fixed 2026-09-11. When another command is running (the marker names `amend-docs`, `fix-issues` or any command but `log-miss`), `tf-log-miss.sh` writes no run record: that command's own record covers the time, so it is no longer refused. The miss and miss-fix records are unchanged and still name the running command's start. A `*log-miss` run on its own still writes its record. Also fixed on the same lines: a marker older than 24 hours, left by a session that died, is now ignored as every other reader ignores it. Before, a `*log-miss` run after one would have been recorded as starting days earlier. The fixed script is already in TfLens. | `grep -n TF-029 .tfcore/utils/tf-log-miss.py` shows the rule. At the next `*amend-docs` that logs a miss, the logger's report reads "Run record : none written — the running *amend-docs's own record covers this time", and the amendment's record is accepted with no void. |
| **TF-030** | Filed and fixed 2026-09-11. Two changes. **(1)** The document check now reads every screen a requirement names, in the template's `*Screen:* <name>` field or as `**<name>** screen` in the text. It fails when no phase's Screens and flow table has that screen: "BRD-200 names the screen "Price providers", which has no row in any Screens and flow table; add the row, then its UI design entry and its mockup, before it is built". **(2)** The check at the end of every command now refuses to finish a turn that wrote a BRD or a UI design while a screen still lacks its row, its UI design entry or its mockup link. A BRD's findings are never recorded as old, so this blocks the amendment that adds the screen. The checklist's "UI row without a mockup link" stays non-blocking, because ten projects carry 412 such old rows (Lekhak alone 132). On your three BRDs with the Price providers row taken out, the old check says nothing and the new one names BRD-200. On every project's real documents today, old and new agree finding for finding. | Take the Price providers row out of a copy of `docs/TfLens-P3-BRD.md` and run `bash .tfcore/utils/tf-doc-check.sh --strict <that copy>`: it names BRD-200. On the real file it names nothing. |
| **TF-031** | Filed and fixed 2026-09-11. **(1)** The config `tf-verify-env.sh` writes now says `baseURL: process.env.BASE_URL`. A config that sets its own `baseURL` is changed to read `BASE_URL` first and keep its own address after it; one with none gets the line added. `--check` reports a config that ignores `BASE_URL`. **(2)** `tf-verify-tests.sh --base <url>` now refuses to run the browser tests when neither the config nor a spec reads `BASE_URL`: "NOT RUN — nothing reads BASE_URL, so the tests would open another address than --base". It no longer tests whatever is on the default port. Your config already reads it. | `bash .tfcore/utils/tf-verify-env.sh --check` prints READY. Next `*verify`: the browser tests open the port `tf-verify-boot.sh` booted. |
| **TF-032** | Filed and fixed 2026-09-11. `tf-verify-screens` now draws each mockup at every width it checks. A control the mockup shows at one width and hides at another is not owed where it is hidden, and the screen's JSON lists it as `hidden_in_mockup`. A control the mockup shows at that width is still owed, and so is one it hides at every width, like a closed dialog. Run on your real `misses.html` and `effort.html` with the sidebar taken out of the page: the old tool asked for it at 390px and 1280px, the new one only at 1280px. | Next `*verify`: "anchored control "app-sidebar" is not on the page" no longer appears at 390px on `/misses` or `/effort`. The twelve rows written RENDER-FAIL for it can be re-verified. |
| **TF-033** | Filed and fixed 2026-09-11. Sign-in now presses `button[type="submit"]` first, then `input[type="submit"]`, then a button, link or `role="button"` element whose test id says submit, signin, sign-in or login, then any button in the form. It never presses a text field. When none is found it says so, rather than clicking the first match. | `tf-verify-screens.sh … --login-path /login --user <u> --password <p>` signs in: no "LOGIN failed" line, and the screens are not redirected to the sign-in page. |
| **TF-034** | Filed and fixed 2026-09-11. Every start writes its own `tests/.artifacts/verify/boot-<port>.json` and `app-<port>.log`, so a second start no longer empties the first one's log. `boot.json` is still written, as a copy of the latest start, for the verdict. `stop --port <n>` stops only that app. A bare `stop` while two or more apps are running refuses and names the ports. The BOOTED line prints the exact stop command. When a Windows-side start times out, whatever listens on that start's port is stopped too, so no stray app keeps holding the build output. | Start two apps on two ports, then `bash .tfcore/utils/tf-verify-boot.sh stop`: it prints NOT-STOPPED with both ports. `stop --port <one>` leaves the other answering. |
| **TF-035** | Filed and fixed 2026-09-11. **(1)** `MSB3021`, `MSB3027`, "being used by another process" and "Access to the path … is denied" are now a lock. The same rung waits and tries again, twice by default, then prints NOT-RUN "the build output is held by a running process", naming the process to stop. It never falls to the next rung. **(2)** On WSL, a build that changes side over the same `obj/` first clears every `obj/**/scopedcss`, whether the change happens within one run or since the last one. `obj/.tf-build-side` records which side built last. The page and its stylesheets are then named by one side again. | Run `bash .tfcore/utils/tf-build.sh` while an app holds `bin/Debug`: it prints NOT-RUN about the lock, not "PASS … via winrun dotnet (rung 3)". After any Windows-side rung, `obj/.tf-build-side` reads `windows`. |
| **TF-036** | Filed and fixed 2026-09-11. A control whose `display` is `inline` and that wraps over several lines is now compared by the pieces it draws on each line (`getClientRects()`), each cut to what is visible. Two sentences that share a line no longer overlap. Two wrapped inline elements drawn over each other still do: the test case pulls one paragraph up over another and the overlap is reported. | Next `*verify` of `/effort`: "kpi-wallclock-derived overlaps kpi-wallclock-recomputed" no longer appears. |
| **TF-037** | Filed and fixed 2026-09-12, and both halves were ours from the day before. `tf-verify-tests.sh` now reads the line that starts `PASS`, `FAIL` or `NOT-RUN`, not the first line, so a note printed before the verdict no longer hides it. The note itself came from the TF-035 fix; it stays, because it says the stylesheets were cleared. `tf-build.sh` no longer counts the lines of a log it has not written yet: the shell error that produced was the first line you saw. | `bash .tfcore/utils/tf-verify-tests.sh --no-browser` after a build that prints a note: the unit line reads `PASS test …` and the rows carrying test names count. `bash .tfcore/utils/tf-build.sh probe` prints no error line. |
| **TF-038** | Filed and fixed 2026-09-12. A border counts only when it has width **and** a colour that is not transparent. That one rule serves all three places: the border-style class, the badge/pill class, and the colour class, which now skips `border-color` when nothing is drawn and falls through to the element's own colour. Proved on a page with `border: 1px solid transparent` in the mockup, no border in the app, and a reset setting `border-color` everywhere: the old tool reported nine badges and a colour difference, the new one reports neither, and a real border is still read. | Next `*verify`: no "mockup renders this as a badge/pill" or "border style differs — mockup solid, app none" on the ghost and primary buttons, and no "semantic colour differs" on the chip icons. |
| **TF-039** | Filed and fixed 2026-09-12. In the fallback branch every descendant's rectangle is now cut by each ancestor that scrolls, the same rectangle `tf-verify-screens` has measured since TF-021. A card whose table scrolls inside its own wrapper is no longer read as cut off, and a card that really is cut off still is. | Next `*verify` at 390px: no "content is cut off horizontally" on `effort-phases` or `effort-routing`. |
| **TF-040** | Filed and fixed 2026-09-12. The emitter compares **windows**, not the order of writing. A record is refused only when its window overlaps a live record's; two that merely touch at a boundary are fine. So a command that chains another one inside itself can record the segment it ran before the inner run started. The refusal now names the record it collides with. `--allow-overlap` is unchanged. | Emit the build's first segment (its marker time to the verify's `started`) after the verify's record: it is appended. A record that really overlaps is still refused, naming the run it overlaps. |
| **TF-041** | Filed and fixed 2026-09-12. When the Phases document is not named on the command line, the checker reads it from disk for the cross-phase rules and throws away findings about the document itself, which belong to a run that names it. The message now says "no Phases document is on disk", so it can only appear when the file is really absent. Run on your real checklists without naming it: the old checker reported it missing, the new one reports nothing and the phase rules still run. | `bash .tfcore/utils/tf-doc-check.sh docs/TfLens-Checklist.md docs/TfLens-P2-Checklist.md docs/TfLens-P3-Checklist.md`: no "Phases document" line. |

Every fix carries a case in the framework's `tests/regression/run.sh` that fails against the script
as you have it today and passes now.

### Why this file said TF-019 and TF-022 were open, and what changed

It was the framework's fault first (`MISS-TechieFlow-20260911-04` and `-05`):

1. **Your status file was wrong about this file.** `tf-status-facts` counted every `###` heading as an
   entry and recognised no closing mark in use, so PROJECT-STATUS read "TechieFlow: 62 open of 62"
   for 27 entries, and "TrBlazeUI: 2 open of 2" for 28.
2. **The self-check never read this file**, and the command start showed your agent only "self-check
   clean".
3. **Our reply of 2026-09-09 left out TF-013 to TF-017**, so they showed open here with no way for you
   to know.

Your agent then repeated the status file instead of reading the reply at the top of this file.

What the framework does now: one reader, `tf_feedback.py`, is used by every script that reports on a
feedback file. PROJECT-STATUS shows open, fixed-upstream-not-yet-re-checked, and closed separately.
The self-check lists every fix waiting to be re-checked at the start of every command, and the start
line passes that list through. `tf-feedback.sh --close` closes an entry once its fix is re-checked
here. The document check refuses a Summary whose counts disagree with the entries. The closing
check (below) refuses a message that calls a fixed entry open. And the framework's own test suite now
fails whenever a problem it has fixed is still open in the framework's copy of this file.

### A NEW CHECK AT THE END OF EVERY COMMAND (FR-74)

Your hand-off of 2026-09-11 was logged as `MISS-TechieFlow-20260911-03`. From this deploy, on the
turn that closes a command, the Stop hook runs `tf-owner-text.sh` on the closing message and on any
free-form document that message hands over. It refuses the stop, once, when the text

- uses a word from `.tfcore/standards/owner-words.txt` or a name from inside a script;
- names an open upstream problem without a table row saying what it affects and whether it blocks or
  breaks anything, or without the prompt that fixes it in a code block;
- describes a problem this file records as fixed upstream or closed as if it were open;
- names a command still to run without the line to paste; or
- ends without the next prompt in a code block.

`docs/TfLens-Parity-Zero-2026-09-11.md` fails it on five lines as it stands: "denominator" at line
229, and TF-019, TF-022, TF-023 and TF-024 described as open. Your Decision Request's decision 1 asks
the owner to fix TF-018 to TF-022, which were fixed on 2026-09-09; it no longer needs a decision.

### Your TrBlazeUI file has the same problem

`docs/TfLens-TrBlazeUI-Feedback.md` opens with the library team's reply of 2026-08-31 saying every
entry filed by then is fixed (24 of them, on 2.1.0 or the next release), and its Summary, written
later, still says "all 28 open. None is fixed upstream". `bash .tfcore/utils/tf-feedback.sh TfLens`
reads it as 3 open, 24 fixed upstream, 1 closed. Those are re-checked after upgrading the package,
not before.

---

## Resolution status (TechieFlow team, 2026-09-09)

**All five open entries are FIXED upstream — `TF-018`, `TF-019`, `TF-020`, `TF-021`, `TF-022`.** Deploy
with `update-framework.sh <repo>`, then re-verify from your side and close them; TechieFlow does not
close a consumer's entries for it. Every fix carries a case in `tests/regression/run.sh`, and each case
was run against the script **as this file describes it** before the fix and after it: a case that passes
both ways proves nothing and is not kept.

| ID | Fix | Verify from here |
|----|-----|------------------|
| **TF-019** | Your first option, taken further: the working list is now **every open row**, and `FIX` only says which ones come first. Nothing is held back, so nothing can be silently omitted. An `Order:` line names the failing rows and counts the not-yet-started ones behind them, and the counts line is checked arithmetically — every row must land in exactly one of built / terminal / Blocked, and a row in none of them is printed as a warning naming the status the script does not know. | Run `bash .tfcore/utils/tf-build-list.sh TfLens --prompts` on phase 3 with `REQ-FN-067` and `REQ-FN-070` still at `Needs re-verify`. The twelve rows `REQ-UI-052`…`054` and `REQ-FN-106`…`114` must appear. Reproduced here on a fixture built from your checklist's exact shape: two permanently gated rows plus two never started. |
| **TF-022** | `add()` has a third outcome. A skipped clause is recorded as skipped — never a pass, never a defect — and a row whose clauses were **all** skipped is `NOT-TESTED`, which `tf-verify-verdict.py` reads as not measured: no gate record, no `Verified`, and no return to FIX mode. The unit path records a skipped test the same way instead of dropping it, so the row shows the evidence rather than looking untested. `tests.json` gains `passed`, `failed` and `skipped` per row and `skipped` on the browser block; the printed line names the NOT-TESTED rows and why. | Re-run `*verify all`. `REQ-UI-039` (6 passed, 2 skipped) must read `PASS`; `REQ-UI-034` (all skipped) must read `NOT-TESTED` with the skip's own reason carried through — your `test.skip` description is what the row now says. A real assertion failure is still `FAIL`: proved on the same run. |
| **TF-021** | Two changes, one per half of your entry. **(1)** Overlap and off-screen are measured on the rectangle an element **paints** in — its own box intersected with the clip rectangle of every ancestor whose overflow is not `visible` — so a 1167px row inside a 492px scroller no longer reaches the card beside it. Zero-size still uses the element's own box, because a control scrolled out of view is not a collapsed one. **(2)** `--render-wait` (default 5000 ms) waits for the screen's first render before reading the page; when nothing appears in time the page is graded exactly as it stands, so a control that is genuinely missing is still reported. | Your `/misses` case was rebuilt here from the measurements in your entry — a `.tflens-scroll-x` container at `clientWidth 492`, `scrollWidth 1167`, and a neighbouring card at x=846. Before: `miss-origin overlaps miss-whymissed at 1280px`. After: `render OK, visual OK`. The `/effort` case was rebuilt as a screen that paints at 2.5 s: before, `anchored control "app-sidebar" is not on the page`; after, `render OK`. A genuinely overlapping screen still fails — that case is kept precisely so the fix cannot buy its quiet by going blind. |
| **TF-020** | One line in `seg()`, in the shape your entry suggested. An `FR` verdict lands in its own `framework-requirement` segment ahead of the `project_type` test, so it can never join an application's `records`, `reqs_scored`, `first_pass_rate`, `escape_rate` or gate distribution. | Roll up a repository carrying both kinds: `bash .tfcore/telemetry/tf-metrics.sh --rollup <repo> --json`. The `framework-requirement` key must exist and the `app` segment must hold only the application's records. Your `Segment.KeyFor` and the reference now agree by construction; the `ADDED_KEYS` declaration in `parity-compare.py` can go. |
| **TF-018** | Both halves, and the owner's reading was taken. `what` is amendable, in its own free-text set (`AMENDABLE_TEXT`) rather than as an empty vocabulary inside the closed-vocabulary map — the structural separation your entry argued for, for the same reason. It is validated as non-empty text. The protection was never the vocabulary: an amend may only fill a field that is still `null` and can never overwrite one that is set, so free text can supply a missing fact and not rewrite one. `FIELD_SINCE` gains `"what": "2026-09-07"`. SCHEMA §5.5.7 says so. | `bash .tfcore/utils/tf-emit.sh --amend <miss_id> what "one sentence"` on a record whose `what` is empty → `amended`. The same command on one that already has it → a printed refusal, exit 0, nothing appended. Both are pinned here. |

**On the numbering.** A defect found *here* while verifying these took the label `TF-021` for a few
hours in our own regression suite, which was wrong: `TF-` numbers belong to your feedback file. It is
renamed (`guard_reads`), and `TF-021` means your screens entry and nothing else.

**Logged as misses in the framework's own stream**, since a framework defect is exactly as countable as
an app's: `MISS-TechieFlow-20260909-04` (TF-021) and `-05` (TF-022), both `wrong-behaviour` / `src` /
major, both sorted `weak-check` — a check existed and was too weak — with
`why_missed: insufficient-verify-method`. `-01` to `-03` carry TF-013…TF-020 and the guard defect.

### What changes on YOUR side (deploy `update-framework.sh` first — done here 2026-09-09)

Nothing is required to keep TfLens working, and no figure it publishes today becomes wrong. This is
the pick-up list, in the order it is worth doing.

1. **Two rows you can re-grade immediately.** `REQ-UI-039` and `REQ-UI-034` carry `FAIL` only because
   a skipped clause was counted as a failing one. Re-run the verifier: `REQ-UI-039` should come back
   `PASS`, and `REQ-UI-034` `NOT-TESTED` with your own `test.skip` description as its reason. Neither
   needs a code change in TfLens.
2. **`tests.json` has three new per-row fields** — `passed`, `failed`, `skipped` (a list of test
   names) — and the browser block has `skipped`. A row's `result` may now be `NOT-TESTED`, which was
   never emitted before. Anything that reads that file and assumes `PASS`/`FAIL` needs the third case.
3. **`screens.json` entries carry `render_wait_ms`**, and anchor entries carry `cx`/`cy`/`cw`/`ch`
   and `clipped` — the painted rectangle beside the laid-out one. Nothing was removed.
4. **A NEW RECORD KIND ON `runs.jsonl`: `kind: "run-void"`** (SCHEMA **§2.7**). This is the one that
   affects your parser and your parity gate, so it is worth reading before the next parity run. The
   streams are append-only, so a run record written with a wrong figure could never be corrected —
   this maintainer wrote one on 2026-09-09 with a guessed start time, five hours against a real
   nineteen minutes. A `run-void` names a run by `cmd` + `started` and carries a `reason`; the
   reference then excludes the named run from **every** figure and publishes `runs_voided_n`,
   `runs_voided` (the reasons) and `run_voids_orphaned_n` (voids naming a run that is not on the
   stream). What TfLens needs, in the shape it already handles `miss-amend`: **dispatch the new kind
   rather than counting it as an invalid line, exclude the run it names, and publish the three keys.**
   Nothing is deleted and nothing is edited — both records stay, in order. TechieFlow's own stream
   holds one such pair today, so a parity run over this repository will see it.

5. **Your status document has been wrong about itself, and the fix is in this deploy.**
   `tf-status-facts` read only the last line of `docs/.last-verify.json`, which the verifier writes
   as one pretty-printed object — so it parsed the closing brace and fell back to "never verified".
   TfLens, 69 of 73 rows Verified with an 86-line ledger, reported `last_verified_build: not-run`
   and `last_verified_date: never`. After this update it reads the real date and result. Nothing in
   TfLens caused it and nothing there needs changing.
6. **A row the verifier cannot measure no longer points at another verify run.** When every row that
   is built is `NOT-TESTED`, the status line says so and names what it needs — data, a changed
   acceptance line, or `N/A` — instead of telling you to run `*verify` again for a result that
   cannot change. `REQ-UI-034` is exactly that row.

**What is NOT fixed, and is not ours to fix.** Nothing in this block touches TfLens's own documents, its
`DECISIONS.md`, or the 236 document findings in the second half of your decision request. The
architecture's `MissReview` table sketch — a `bigserial` key with `UserId` as text and `Ts` as
`timestamptz`, against four sibling tables with no surrogate key, an integer `UserId` and a text `Ts` —
is specification territory and belongs to `*amend-docs` in your window, not to a framework session.

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

## TF-013 — `verify-phase` has no rule against starting services nobody asked for, and none that a missing database is a question, not a substitution

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11: fed the row's docker-compose test input to .tfcore/hooks/guard-verify-deps.sh; it printed the refusal (bare compose up starts every service; name the service) and exited 2. The live hook also refused the same text when I first typed it as a command.

- **Severity:** major
- **Blocks:** no — the run finished, its verdicts were discarded as measured against the wrong database, and the work carried on
- **Repro:** run `*verify all TfLens` with the configured dev database (`localhost:5550`) stopped
- **Expected:** the run stops and asks, naming the one command that starts the project's own database
- **Actual:** it ran a bare `docker compose up -d`, creating containers and an image nobody asked for, then pointed the test suite at a different PostgreSQL and reported "689/689 pass"
- **Encountered in:** `*verify all`, 2026-09-01 (`MISS-TfLens-20260901-02`)
- **Workaround:** the unasked-for container and image were deleted by hand and the run repeated against the right database
- **Suggested fix:** three lines in `verify-phase.md` — provision nothing, start a defined service by name, and treat an unreachable dependency as an ASK, never a substitution. Full wording under Detail.

#### Detail

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

**Status:** fixed upstream 2026-09-09 (`guard-verify-deps.sh`); re-checked and closed here 2026-09-11. The title was added on 2026-09-11: without one, `tf-feedback.sh` could not read this entry's closing line (TF-028).

---

## TF-014 — `tf-gitignore-audit.sh` skips every dot-directory, so it cannot see the IDE-state folders it exists to catch

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11: ran tf-gitignore-audit.sh . --dry-run: exit 0, rules present, nothing tracked. .vs/ exists here and .gitignore line 47 (/.vs/) ignores it, so nothing was named. The script now checks .vs/ and .idea/ for every stack (IDE_STATE, line 92) and prunes dot-folders by a name list only (line 125), not all of them.

- **Severity:** major
- **Blocks:** no — `.gitignore` was widened here by hand and the work carried on
- **Repro:** `bash .tfcore/utils/tf-gitignore-audit.sh .` on a repository with tracked files under `.vs/ProjectEvaluation/`
- **Expected:** the audit names the tracked files and prints the `git rm -r --cached` line for them
- **Actual:** it skips every dot-directory, so it reports nothing — on a repository where three tracked `.bin` files carried 246 absolute paths from one developer's machine
- **Encountered in:** housekeeping on this repository, 2026-09-02
- **Workaround:** the ignore rule was corrected by hand
- **Suggested fix:** stop skipping dot-directories. A dot-directory is exactly where IDE state lives, which is what this tool exists to catch.

#### Detail

**Severity:** Medium · **Raised:** 2026-09-02 · **Status:** closed 2026-09-11 (fixed upstream 2026-09-09) · **Found by:** owner

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

---

## TF-015 — `tf-emit.sh` accepts a run whose `ended` precedes its `started`, and the two consumers then disagree about it

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11: ran tf-selfcheck.sh: 'runs.jsonl 1 old record(s) end before they start — discarded from every duration figure, and the report says so'.

- **Severity:** blocker
- **Blocks:** no — nothing here stopped; the figure is silently wrong in both readers and neither flags it
- **Repro:** emit a `run` record whose `ended` precedes its `started`
- **Expected:** the emitter refuses the record and names the two timestamps, or stores no duration and says why
- **Actual:** it stores a negative `duration_s` without complaint. `tf-metrics.sh` admits it and TfLens excludes it, so the same corpus yields two different totals. 14 such records exist in one repository, 13 of them invisible.
- **Encountered in:** the parity check against `tf-metrics.sh`, 2026-09-02
- **Workaround:** TfLens excludes negative durations and reports how many it excluded
- **Suggested fix:** refuse the record at emit time, naming both timestamps in the refusal.

#### Detail

**Severity.** High — it produces a wrong figure that looks right, in the stream the framework uses to
measure itself.

**What happens.** Nothing stops a `run` record being emitted with `ended` earlier than `started`.
`tf-emit.sh` computes and stores the negative `duration_s` without complaint. In `TechieBlog` this
produced `{"cmd":"fix-issues","started":"2026-08-22T11:30:00Z","ended":"2026-08-22T11:27:14Z",
"duration_s":-166}` — a run that finished two minutes and forty-six seconds before it began.

**Why it matters more than one bad row.** The two consumers of that field disagree, so the same corpus
yields two different answers and neither flags the record:

- `tf-metrics.sh` filters with `if r.get("duration_s")` — truthy — which **admits negatives**. The
  record enters `duration_s_total`, the per-phase totals, the median, and `throughput`.
- A strict consumer that filters `duration_s > 0` **excludes** it.

On this dataset that is `phases.duration_s_total` 445854 vs 446020, `fix-issues.duration_s.n` 38 vs 37,
and a throughput median of 6.26 REQs/hour against a true 6.52 — because `1 / -166` is a *negative*
REQs-per-second that drags the median down. Every one of those is a plausible number that a reader
cannot tell is wrong.

**It is not one record.** The same repository holds **13 more** with `started` after `ended` which are
completely invisible, because they store a plausible round `duration_s` (3600, 2700, 1800, 1500, 1200,
900, 600, 420) *instead of* the negative arithmetic — e.g. `refresh-status` started `20:05:00`, ended
`17:59:39`, stored `600`. Only the one record that happened to store the negative was ever detectable.
A validation at emit time would have stopped all 14 at the source; nothing downstream can recover them,
because the true start times are gone.

**Expected.** `tf-emit.sh` refuses a `runs` record where `ended < started`, the same way it already
refuses a value outside a closed vocabulary (`tf-emit: REFUSED — 'code' is not in the closed vocabulary
for artifact`). That refusal message is exactly the right shape and already exists — it simply is not
applied to the timestamps.

**Suggested fix.** Two lines, both at emit time:

1. Refuse when `ended < started`, naming both values.
2. Refuse when a supplied `duration_s` disagrees with `ended − started` by more than a second — that is
   what would have caught the other 13, whose stored durations bear no relation to their timestamps.

Optionally, `tf-metrics.sh`'s `if r.get("duration_s")` becomes `if (r.get("duration_s") or 0) > 0`, so a
historical negative already on a stream cannot reach a figure. That is a consumer-side guard and does
not remove the need for the emit-time one: an impossible record should never be written.

**Encountered in:** TfLens, BRD §13 parity run 2026-09-02. The diff held at 4 findings entirely because
of this one record. Recorded locally as `REQ-FN-063` and `REQ-NFR-005`. **Not fixable from TfLens** —
`.tfcore/` is framework-owned and the offending data belongs to another repository (`REQ-NFR-018`).

---

## TF-016 — a document miss stays open forever, because `*amend-docs` is the one fix command with no closing step

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11: grep tf-fix-close .tfcore/tasks/amend-docs.md shows step 9 (line 23); tf-emit.sh --open-misses TfLens --artifact-class doc listed 16 open document misses (architecture, brd, checklist, devguide) that step can close.

- **Severity:** minor
- **Blocks:** no — nothing is blocked and no figure is wrong; 12 finished items simply show as outstanding, and the work carried on
- **Repro:** run `*amend-docs` on a change that fixes a miss whose deficient artifact is a document
- **Expected:** the miss closes when the amendment lands, the way `*fix-issues` closes one when the code lands
- **Actual:** `amend-docs.md` step 9 asks only the opening question. There is no closing step, so a document miss stays open forever.
- **Encountered in:** `*amend-docs` on TfLens, 2026-09-08
- **Workaround:** none — the items stay open in the list
- **Suggested fix:** call `tf-fix-close.sh` from `amend-docs` after the checklist step. `fix_cmd: "amend-docs"` is already legal in the schema; only the wiring is missing.

#### Detail

**Severity: Low** (ergonomic — no wrong number, no data loss, nothing blocked). Found 2026-09-08 during
`*amend-docs` on TfLens.

**This is not a hole in the closing machinery.** `tf-fix-close.sh` works, and `fix-issues` and
`build-phase` both call it. The gap is only about which command is wired to it.

**Expected.** A miss whose deficient artifact is a document (`artifact ∈ {brd, architecture, uidesign,
checklist, devguide}`) is fixed by `*amend-docs` — that is the command that edits those files. When the
amendment lands, that miss should be closeable.

**Actual.** `amend-docs.md` step 9 asks only the *opening* question — "is this amendment itself a miss?
then `*log-miss`". There is no step that closes an existing miss the amendment resolves. Closing happens
in `fix-issues` (step 5) and `build-phase`, and both close off **checklist rows** the verifier touched —
which never covers a miss whose artifact is a document and whose fix was an edit to the BRD.

**Consequence.** The miss stays open indefinitely, and the backlog figure that reads it overstates
outstanding work. It is a display problem, not a data problem: no figure is *wrong*, the open-miss count
is just permanently inflated by work that is finished.

**Evidence — TfLens, 2026-09-08.** 37 open misses; 16 name a document artifact. Every one that carries a
`req_id` is already `Verified` or `Implemented` in `docs/TfLens-Checklist.md`, closed by the `*amend-docs`
runs of 2026-08-29 and 2026-09-01:

| Miss | `req_id` | `artifact` | Checklist status today |
|---|---|---|---|
| `MISS-TfLens-20260901-06..12` | `REQ-FN-090`, `-092`, `-093`, `-094`, `-102`, `-103`, `-105` | brd / architecture | **Verified** |
| `MISS-TfLens-20260830-03` | `REQ-NFR-020` | brd | **Implemented** — owned by `BRD-144`, appended 2026-08-29 |
| `MISS-TfLens-20260829-01` | `REQ-NFR-019` | architecture | **Implemented** — owned by `BRD-143`, appended 2026-08-29 |
| `MISS-TfLens-20260830-01` | `REQ-UI-027` | checklist | **Verified** |
| `MISS-TfLens-20260829-21` | `REQ-UI-011` | devguide | **Verified** |

Twelve finished items reported as outstanding, for between one and two weeks.

**Repro.** On any project with an open miss whose `artifact` is `brd`: run `*amend-docs {App} "<the
change that fixes it>"`. The BRD is amended correctly; `misses.jsonl` gains no `miss-fix` record; the
miss remains open.

**Suggested fix.** One step in `amend-docs.md`, between the current steps 8 and 9 — the mirror of the
question already asked in step 9:

> **Close what this amendment fixed.** List open misses for `{App}` whose `artifact` is a document
> (`bash .tfcore/utils/tf-emit.sh --open-misses {App} --artifact-class doc`). For each one this
> amendment resolves, emit a `miss-fix` with `fix_cmd: "amend-docs"` and `verdict_after: "Verified"`.
> Where the mapping is unclear, ask — do not guess.

`fix_cmd` already admits `amend-docs` in the SCHEMA §5.5.2 vocabulary, so **no schema change is needed**
— the value is legal today and nothing emits it.

The same gap plausibly applies to `*mockups --update` for `artifact: uidesign`, though we have not hit
that case here.

**Encountered in:** TfLens, `*amend-docs` 2026-09-08. **Not fixable from TfLens** — `.tfcore/` is
framework-owned.

---

## TF-017 — `tf-split-brd.py` cannot read a BRD ledger line that carries the HTML anchor its own link checker needs

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11: ran tf-selfcheck.sh: 'tf-split-brd TfLens: reads all 86 BRD items' for phase 3 (65 and 48 for phases 1 and 2).

- **Severity:** minor
- **Blocks:** no — the checklist rows were written by hand and the amendment completed in full; no figure, gate or document is wrong as a result
- **Repro:** on any BRD whose ledger items are written `- <a id="brd-N"></a>**BRD-N** — …`, run `bash .tfcore/utils/tf-split-brd.sh {App} --add-missing`
- **Expected:** the new BRD items are appended to the checklist as rows
- **Actual:** `tf-split-brd: docs/{App}-BRD.md has no **BRD-N** items in its Requirements section` — it finds **zero** items, not just the new ones
- **Encountered in:** `*amend-docs` on TfLens, 2026-09-08 (step 7)
- **Workaround:** write the rows and their detail entries by hand, which is what this amendment did for its twelve new requirements
- **Suggested fix:** allow an optional anchor before the bold id in the ledger regex at `.tfcore/utils/tf-split-brd.py:88` — `^\s*[-*]\s*(?:<a id="[^"]*"></a>\s*)?\*\*(BRD-\d+)\*\*\s*[—:-]+\s*(.*)$`. One optional group; nothing else changes.

#### Detail

The two halves of the framework disagree about how a ledger item is written.

`tf-split-brd.py:88` matches a ledger item with:

```
^\s*[-*]\s*\*\*(BRD-\d+)\*\*\s*[—:-]+\s*(.*)$
```

which requires `**BRD-N**` to follow the bullet immediately. But a BRD's §9 screen inventory links to
each requirement as `[BRD‑21](#brd-21)`, and markdown generates no id for bold text inside a list item —
only for headings. The only way to make those links resolve is an explicit anchor on the ledger line,
which is what TfLens's BRD has carried since day one:

```
- <a id="brd-21"></a>**BRD-21** — User can see, on Coverage, …
```

`tf-doc-check.sh` refuses a broken link, so the anchors are required. `tf-split-brd.py` cannot parse a
line that has one. **Every** item is affected, not only new ones: TfLens's BRD has **179** ledger items
and the regex matches **0**.

**Why it went unnoticed.** `*split-brd` runs once, at day-1, before the anchors are added; `--add-missing`
is only reached later, from `*amend-docs`. The first amendment on a project with §9 cross-links is where
it surfaces. `tf-doc-check.py:617` carries the same regex and the same blind spot, and reports
`no **BRD-N** items found in the Requirements section` on the same documents.

**What is NOT affected.** The BRD, Architecture, mockups and checklist are all correct — this is a
convenience script that could not append rows, not a rule that was skipped or a number that is wrong.
`tf-doc-check.sh --app TfLens` passes on the amended documents. Nothing about the change-set, the twelve
new requirements or the seven edits depends on the script running.

**Encountered in:** TfLens, `*amend-docs` 2026-09-08. **Not fixable from TfLens** — `.tfcore/` is
framework-owned.

---

## TF-018 — the reference cannot fold a `what` amendment the owner's BRD requires, and its `FIELD_SINCE` carries no floor for `what`

- **Severity:** minor
- **Blocks:** no — TfLens implements the owner's amended BRD-116 / BRD-117 and every figure it publishes
  is correct; the divergence is that `tf-emit.sh` will refuse to *write* an amendment TfLens can read
- **Repro:** `bash .tfcore/utils/tf-emit.sh --amend MISS-App-20260907-01 what "one sentence"`
- **Expected:** the amendment is written to `docs/metrics/misses.jsonl`, as it is for `why_missed` and `sort`
- **Actual:** `REFUSED what is not an amendable field (SCHEMA.md §5.5.7)` — `tf-emit.sh:339` `AMENDABLE`
  holds only `why_missed` and `sort`, and `.tfcore/telemetry/tf-metrics.sh:62` `AMENDABLE_FIELDS` matches it
- **Encountered in:** TfLens, `*build-phase` cluster A, 2026-09-09 (REQ-FN-075, REQ-FN-076)
- **Workaround:** none needed here — TfLens accepts the field on read, so an amendment written by any
  other door still folds. Nothing is blocked; the field simply cannot be amended through `tf-emit.sh`.
- **Suggested fix:** two small edits, if the framework agrees with the owner's reading:
  1. `tf-emit.sh:339` and `tf-metrics.sh:62` — allow `what` with a non-empty-text check instead of a
     vocabulary, and say so in SCHEMA.md §5.5.7's allowlist table beside the "closed vocabularies only"
     sentence, which currently forbids it outright.
  2. `tf-metrics.sh:56` — `FIELD_SINCE` gains `"what": "2026-09-07"` beside `"sort"`.

#### Detail

Two independent differences, both in the same direction, both from the 2026-09-08 amendment of TfLens's
BRD-116 and BRD-117.

**1. `what` is not amendable upstream.** SCHEMA.md §5.5.7 lists exactly two amendable fields —
`why_missed` and `sort` — and states the extension rule as *"**Closed vocabularies only**, so the kind can
never become a free-text back door (§9, constraint 7)"*. `what` is §5.5.1's one free-text field, so under
that sentence it can never join the allowlist. TfLens's owner amended BRD-116 on 2026-09-08 to add it,
validated only as non-empty text. TfLens therefore folds a `what` amendment; `tf-emit.sh` refuses to
write one.

The two positions are both defensible and the disagreement is worth an explicit decision rather than a
silent drift. TfLens keeps the back-door concern addressed structurally: `what` sits in its own
`AmendableFreeTextFields` set, not as an empty vocabulary inside the closed-vocabulary map, precisely so
"no legal values" can never be misread as "every value is legal" for some other field later.

**2. `FIELD_SINCE` has no floor for `what`.** `tf-metrics.sh:56` reads
`{"why_missed": "2026-08-28", "sort": "2026-09-07"}`. TfLens's BRD-117 adds `what` at 2026-09-07 for the
same reason `sort` is there — a record written before the field existed had nothing to fill, and pooling
it into a denominator understates every category. Any figure the reference builds over `what` today uses
the whole record set as its denominator.

**What is NOT affected.** No figure TfLens publishes is wrong, and no gate, sync or parse is blocked.
`sort` agrees exactly: TfLens and the reference both floor it at 2026-09-07, both hold the same four-value
vocabulary, and both treat an out-of-vocabulary value as an orphan rather than coercing it. The
carries-the-field escape (`_eligible`'s `or bool(rec.get(field))`, Session 5) is implemented identically,
so an old miss completed by an amend counts as sorted in both. `why_missed` is untouched. Nothing in the
misses stream, the four record kinds or the dedupe keys is in question here.

**Encountered in:** TfLens, `*build-phase` cluster A 2026-09-09. **Not fixable from TfLens** — `.tfcore/`
is framework-owned.

## TF-019 — one permanently owner-gated row pins `tf-build-list` in FIX mode, so `Not Started` rows are never scheduled again

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11: nothing to run per the reply; the session of 2026-09-11 saw every not-started row offered, and tf-selfcheck.sh today prints 'tf-build-list TfLens p3: 117 rows, all accounted for'.

- **Severity:** major
- **Blocks:** no — this pass read the checklist directly and built the twelve omitted rows in the same
  run. But the omission is silent: a pass trusting the printed list reports the phase finished.
- **Repro:** a checklist holding one row at `Needs re-verify` **and** one at `Not Started`; run
  `bash .tfcore/utils/tf-build-list.sh {App} --prompts`
- **Expected:** the working list covers every open row, or names the rows held back
- **Actual:** `Mode: FIX` over only the failing rows. The twelve `Not Started` rows appear nowhere — not
  in the list, the clusters, or the counts line `8 row(s) to build; 51 terminal, 0 Blocked, 73 total`.
- **Encountered in:** TfLens, `*build-phase` 2026-09-09, phase 3
- **Workaround:** read the Requirements Status table directly and build every non-terminal row.
- **Suggested fix:** at `tf-build-list.py:117`, fall through to the open rows once the FIX list drains;
  or state the held-back rows in the counts line so FIX is never mistaken for finished.

#### Detail

`tf-build-list.py:114` collects `fix_rows` for the four `FIX` statuses (line 22) and line 117 returns
`FIX, fix_rows` whenever that list is non-empty; `FRESH` over `open_rows` (line 119) is reachable only
when no row carries any of the four. FIX always wins.

What makes that a trap is that a row can sit at `Needs re-verify` **permanently**: `REQ-FN-067` and
`REQ-FN-070` have been so since 2026-08-27 because their acceptance needs a repository that emits
`events.ndjson`, and none exists. Every honest verify since re-confirmed that gate rather than clearing
it — the correct verdict, and exactly what pins the mode.

The counts line is where it turns silent. `51 terminal, 0 Blocked, 73 total` against `8 row(s) to build`
leaves twelve rows in no category the reader can see.

**What is NOT affected.** `FRESH` and `NOTHING` are correct, and a project with no permanently gated row
reaches `FRESH` next pass as intended. Cluster formation, the prompt template, the acceptance-line
refusal and the `Blocked` pass-through behave as documented.

**Not fixable from TfLens** — `.tfcore/` is framework-owned.

## TF-022 — a conditional `test.skip` is counted as a failing test, so "this state does not exist in the data" is reported as a defect

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11: nothing to run per the reply; docs/TfLens-P3-Checklist.md shows REQ-UI-034 and REQ-UI-039 both Verified, 100%, from the 2026-09-11 verify (PASS). The old FAIL came from the 2026-09-09 14:54 run, before the fix.

- **Severity:** major
- **Blocks:** no — the two rows are owner-gated and named under PROJECT-STATUS "Known blockers". But the
  checklist carries `FAIL` where no defect exists, and under `build-phase.md` step 7 a `FAIL` re-enters
  FIX mode for up to five cycles against a clause no code can satisfy.
- **Repro:** a spec guarding a clause on data being present, e.g.
  `test.skip(!SEEDED, "needs the seeded dataset")`, then `bash .tfcore/utils/tf-verify-tests.sh --base <url>`
- **Expected:** the clause is recorded as **not measured** — never a pass, and never a defect either
- **Actual:** the row is `FAIL`. `tf-verify-tests.sh:81` computes `ok = status in ("passed", "expected")`,
  so `skipped` takes the same path as a failed assertion.
- **Encountered in:** TfLens, `*build-phase` → chained `*verify all`, 2026-09-09 — `REQ-UI-039`
  (6 passed, 2 skipped) and `REQ-UI-034` (no Playbook repository emits `events.ndjson`).
- **Workaround:** none that keeps the verdict honest — grading by hand is refused by the hook, rightly.
- **Suggested fix:** give `add()` a third outcome, leaving a skipped clause **absent** rather than failed,
  as `verify-phase.md` prescribes for an unreachable head. The row then lands on `NOT-TESTED`.

**What is NOT affected.** A real assertion failure is still reported correctly, and a row whose tests all
pass is unaffected — this pass graded 70 rows `PASS` through the same path. Unit-test attribution, the
browser/unit split and per-row id matching all behave as documented.

**Not fixable from TfLens** — `.tfcore/` is framework-owned.

## TF-021 — `tf-verify-screens.sh` measures an unclipped box inside a horizontal scroller, so a correctly laid-out screen fails the visual check

- **Severity:** major
- **Blocks:** no — render and visual evidence came from the project's own gate
  (`tests/.artifacts/gates/render-visual.json`), which prior verified runs also cite. But left alone the
  tool writes a FAIL onto rows that are correct.
- **Repro:** any screen whose table sits in a horizontal-scroll container narrower than its content
  (here `.tflens-scroll-x` on `/misses`), then run `tf-verify-screens.sh` with a signed-in state
- **Expected:** no visual finding — nothing is drawn on top of anything
- **Actual:** three overlaps, e.g. `miss-origin-amend-docs overlaps miss-whymissed at 1280px`
- **Measured, not assumed:** the row's box is 1166.9px wide while its container reports `clientWidth 492`,
  `scrollWidth 1167`. The tool compares the **unclipped** box, so it reaches x=1480 while the pixels stop
  at x=805; the neighbouring card starts at x=846, inside the clipped-away region.
- **Second finding:** `anchored control "app-sidebar" is not on the page` for `/effort` — yet the sidebar
  is plainly rendered in the tool's **own** screenshot. It measures before the circuit's first render.
- **Workaround:** grade render and visual from the project's own gate and exclude this `screens.json`.
- **Suggested fix:** intersect each box with the clip rectangle of every scrollable ancestor before
  testing overlap (as TF-011/TF-012 ask for `clip`); and wait for a settled render before measuring.

**What is NOT affected.** `--storage-state` sign-in works. `tf-verify-tests.sh`, `tf-mockup-parity.sh`
and the gate specs are unaffected, and the verdict script faithfully reports what this tool hands it.

**Encountered in:** TfLens, `*build-phase` → chained `*verify all`, 2026-09-09.
**Not fixable from TfLens** — `.tfcore/` is framework-owned.

## TF-020 — `tf-metrics.sh` pools `req_class: "FR"` verdicts into the application segments its own SCHEMA says they must never join

- **Severity:** major
- **Blocks:** no — TfLens segregates them itself, so no TfLens figure is affected; the reference's own
  rollup and any parity run over a dataset carrying FR verdicts are
- **Repro:** roll up a repository whose `docs/metrics/gates.jsonl` carries records with
  `"req_class": "FR"` beside `UI`/`FN`/`NFR` ones:
  `bash .tfcore/telemetry/tf-metrics.sh --rollup <repo> --json`
- **Expected:** the FR verdicts appear in their own block, or not at all — SCHEMA.md §3 says
  *"it never pools with the others — a framework line and a screen requirement are not the same unit."*
- **Actual:** they are counted into whichever `project_type` segment their records carry. `seg()`
  (`tf-metrics.sh:998`) keys only on `project_type`, and the script's three mentions of `req_class`
  (`:881`, `:907`, `:918`) all sit in `backfill_gates()`, which *writes* the field and never reads it.
  So `reqs_scored`, `first_pass_rate`, `escape_rate` and the gate distribution of an application segment
  can all include framework requirement verdicts.
- **Encountered in:** TfLens, `*build-phase` cluster C2, 2026-09-09 (REQ-FN-110, BRD-177)
- **Workaround:** none needed in TfLens — `Segment.KeyFor` gives an FR verdict its own segment key ahead
  of the `project_type` test, and the segment map has no "all" key and no total row. The divergence is
  declared in `tools/parity-compare.py`'s `ADDED_KEYS` rather than hidden.
- **Suggested fix:** one line in `seg()`, in the same shape as the `project_type_inferred` test that is
  already there:
  ```python
  def seg(records):
      d = defaultdict(list)
      for r in records:
          if r.get("req_class") == "FR":
              key = "framework-requirement"
          else:
              key = "unclassified" if r.get("project_type_inferred") else r.get("project_type", "app")
          d[key].append(r)
      return d
  ```

#### Detail

**This is the same shape as the three corrections the framework has already made.** BRD-178, BRD-179 and
BRD-180 were each a sum the reference was doing wrong in the direction that flatters, and each is now
fixed in `tf-metrics.sh`: `rk()` keys a requirement `(app, req_id)`, `analyse_phases()` derives
`duration_s` from `started`/`ended` and publishes `derived_n`, and `analyse()` derives `attempt` in
stream order. BRD-177 is the fourth item of the same 2026-09-07 amendment and is the one the reference
did not implement. The schema states the rule; the rollup does not apply it.

**Why it flatters in the same direction.** The framework grades itself against its own 63 requirement
lines and writes one gate record per line. Those verdicts are written by a purpose-built runner over a
document the framework controls, so they pass at a far higher rate than application requirements
verified against a running app. Pooled into an application segment they raise its first-pass rate and
dilute its escape rate, and — because they are numerous relative to one project's REQ set — they can do
so by a wide margin. A reader of the pooled figure has no way to see it: nothing on the output says a
framework rule was counted as an application requirement.

**What is NOT affected.** The `(app, req_id)` keying, the `duration_s` derivation and the `attempt`
derivation are all correct in the reference and TfLens now matches them key for key — the regenerated
`tests/TfLens.Core.Tests/Fixtures/Engine/reference.json` is the oracle's own output and the three parity
tests pass against it. `project_type` segmentation, the live/backfilled split, the taint set, the pooled
block, `analyse_misses` and `analyse_phases` are all untouched by this entry. No stream, no emitter and
no gate is involved: this is a read-time segmentation rule in one function. And nothing here affects a
repository that emits no FR verdicts at all, which today is every repository but the framework's own —
which is exactly why it can sit unnoticed until the framework reads its own rollup.

**Encountered in:** TfLens, `*build-phase` cluster C2 2026-09-09. **Not fixable from TfLens** —
`.tfcore/` is framework-owned.

---

## TF-023 — the TfLens duration-parity decision document names a change that would break the parity gate forever

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11: read both places in TechieFlow docs/Decision-TfLens-Duration-Parity-2026-09-09.md: line 81 and the prompt at lines 166-170 now say the four keys go in PHASES_TOP_KEYS only and each phase's duration_s keeps its five keys. tools/parity-compare.py matches: the four counts are in PHASES_TOP_KEYS (line 157); PHASES_NESTED_KEYS duration_s keeps total, median, max, n, derived_n (line 172).

- **Severity:** major
- **Blocks:** no — the correct half was implemented. But an agent following the document literally,
  as it is written to be pasted, makes the gate permanently unpassable.
- **Repro:** `docs/Decision-TfLens-Duration-Parity-2026-09-09.md`, "Where the work lands in TfLens" and
  again in the pasteable prompt: *"`tools/parity-compare.py` — `PHASES_TOP_KEYS` and the `duration_s`
  tuple both need the new keys"*.
- **Expected:** the four keys are added to `PHASES_TOP_KEYS` only.
- **Actual:** the `duration_s` tuple is `PHASES_NESTED_KEYS["duration_s"]`, which lists the members of
  **each individual phase's** `duration_s` object. `tf-metrics.sh` emits exactly
  `total`/`median`/`max`/`n`/`derived_n` there and nothing else — the four counts are published once per
  repository at the top of the `phases` block, not per phase. Adding them to the nested tuple raises an
  `UNCOVERED` finding per phase, per repository, permanently.
- **Encountered in:** TfLens, `*build-phase`, 2026-09-10 — verified in `tf-metrics.sh` and against live
  rollups for all five repositories.
- **Workaround:** add the keys to `PHASES_TOP_KEYS` only; the per-phase block changes only its values,
  which its existing five keys already diff.
- **Suggested fix:** in the decision document, change both occurrences to name `PHASES_TOP_KEYS` alone,
  and state that the per-phase `duration_s` block keeps its five keys and changes only its values.

**What is NOT affected.** Everything else in that document is correct and was followed as written,
including the expected figures — TechieBlog's 33 of 46 with 13 impossible and 4 recomputed reproduced
exactly.

---

## TF-024 — the document checker applies the whole-project BRD template to phase BRDs, so a correct phase BRD cannot pass

> ✅ **Closed 2026-09-11** — re-checked here: Re-checked 2026-09-11 after *amend-docs: bash .tfcore/utils/tf-doc-check.sh --app TfLens --strict shows no section finding on TfLens-P2-BRD.md, TfLens-P3-BRD.md, TfLens-P2-UIDesign.md or TfLens-P3-UIDesign.md. The phase BRDs no longer carry a Feature catalog, Development status sits after Requirements, and each phase UI design has a Where the rest lives section linking to phase 1 and no Library gaps section. The ten phase-document section findings went to zero (strict total 265 to 255 FAIL); P2 BRD reads OK and P3 BRD carries only a word-count note under its maximum.

- **Severity:** major
- **Blocks:** no — no figure or code is affected. But it puts 87 unfixable `FAIL` lines into every
  `tf-doc-check.sh --app` run, hiding the real failures.
- **Repro:** `bash .tfcore/utils/tf-doc-check.sh --app TfLens` on a Large project split into phases.
- **Expected:** a phase BRD (`docs/{App}-Pn-BRD.md`) is checked against the sections a phase BRD actually
  carries.
- **Actual:** it is checked against `.tfcore/templates/v4custom/app-brd-tmpl.md`, the whole-project
  template, which marks `Scope`, `Users and roles`, `Non-functional requirements`, `Context diagram`,
  `Constraints and assumptions` and `Risks` as required. A phase BRD carries none of them **by design** —
  they belong to the phase-1 BRD — and its "Where the rest lives" table is reported as *"not in the
  template"*. No phase-BRD template exists.
- **Encountered in:** TfLens, `*amend-docs` and `*build-phase`, 2026-09-10 — `docs/TfLens-P2-BRD.md` and
  `docs/TfLens-P3-BRD.md`. 87 of 236 `FAIL` lines across the project are template-shape findings.
- **Workaround:** none that is honest. Writing the six missing sections into each phase BRD would
  duplicate the phase-1 BRD and create exactly the two-answers problem phasing exists to prevent.
- **Suggested fix:** add `app-phase-brd-tmpl.md` whose required sections are the ones a phase BRD really
  has (Development status, Screens and flow, Feature catalog, Requirements, Where the rest lives), and
  select it in `tf-doc-check.sh` when the filename matches `{App}-P<n>-BRD.md`.

**What is NOT affected.** The phase-1 BRD is checked correctly. The checklist, Architecture, PROJECT-STATUS and mockup checks are unaffected, and the
`BRD-N` range check works correctly once a phase's range row is extended.

---

## TF-025 — `tf-split-brd --add-missing` does not recognise a row whose title reads `(BRD-N, Phase 3)`, so it appends a second row for a requirement already built and verified

- **Severity:** major
- **Blocks:** no — the verifier set the 31 copies to `N/A` naming the original row and graded the phase.
  But the copies inflated the unbuilt count, and a build pass trusting the checklist would build them again.
- **Repro:** a checklist whose rows cite their BRD item inside a longer bracket — `(BRD-76, Phase 3)` —
  with no `*BRD:*` detail line; add one BRD item and run `tf-split-brd.sh {App} --add-missing`.
- **Expected:** one row appended, for the new item.
- **Actual:** on TfLens phase 3 (2026-09-10) it appended 44 rows for 13 new items: 30 copies of rows
  already `Verified` and a copy of REQ-FN-112 as REQ-FN-135, each with a placeholder acceptance line.
- **Encountered in:** TfLens, `*amend-docs` 2026-09-10; found by `*verify all` 2026-09-11.
- **Workaround:** mark each copy `N/A` naming the original row; write the new rows' acceptance lines by hand.
- **Suggested fix:** `.tfcore/utils/tf-split-brd.py:226` — the pattern `\((BRD-\d+)\)` accepts only a
  bracket holding one id. Collect every `BRD-\d+` on each `| REQ-` row instead, and print the items it
  is about to append before writing them.

**What is NOT affected.** The BRD and every pre-existing row are unchanged. The 13 genuinely new items
(BRD-189 to BRD-201) were appended with correct ids, and no id was reused.

---

## TF-026 — `tf-verify-screens` reads the mockup's stylesheet as well as its markup, so a CSS rule becomes a control the page must carry

- **Severity:** minor
- **Blocks:** no — on TfLens both affected screens also fail for a real reason, so no verdict changed.
- **Repro:** a mockup whose `<style>` holds `[data-testid="source-mode"] .tab{…}`; run
  `tf-verify-screens.sh --list … --base …` on a screen built from it.
- **Expected:** anchors come from elements in the mockup's body.
- **Actual:** `anchored control "source-mode" is not on the page` on `/misses` and `/effort`; neither
  mockup has such an element, only two CSS selectors.
- **Encountered in:** TfLens, `*verify all`, 2026-09-11.
- **Workaround:** none needed here; on another project, read the finding against the mockup's body.
- **Suggested fix:** in `anchorsOf` (`tf-verify-screens.mjs:72`) strip `<style>` and `<script>` blocks
  before matching.

**What is NOT affected.** Every other anchor was read correctly, and the visual half, the asset check and
the screenshots are unaffected.

---

## TF-027 — `mockup-parity` reports an icon as missing when the app draws it one wrapper deeper than the mockup

- **Severity:** minor
- **Blocks:** no — the affected rows already fail an earlier check. But the report lists 136 findings at
  1280 px over two screens, most describing icons plainly visible in the tool's own screenshot.
- **Repro:** `tf-mockup-parity.sh --screen misses=/misses --screen effort=/effort` on TfLens; compare with
  `tests/.artifacts/verify/screens/misses-rework-1280.png`.
- **Expected:** an icon the app renders where the mockup draws one is not reported missing.
- **Actual:** `the mockup carries an icon here and the app renders no such element` on sidebar groups,
  info boxes and KPI tiles. The `missing` clause (`tf-mockup-parity.mjs:360`) pairs elements by tree
  position, so one extra wrapper makes every icon below it "missing". The KPI colour findings are
  similar: `semanticColor` reads a wrapper whose translucent tint it buckets as neutral.
- **Encountered in:** TfLens, `*verify all`, 2026-09-11.
- **Workaround:** read the `missing` and `color` findings against the screenshot before acting on them.
- **Suggested fix:** search the paired parent's subtree for a matching icon before reporting `missing`;
  take colour from the first descendant with an opaque fill.

**What is NOT affected.** The clip, wrap and badge clauses are separate and were not examined here.

---

## TF-028 — `tf-feedback.sh` cannot read an entry's closing line when the entry's heading has no title

- **Severity:** minor
- **Blocks:** no — adding a title to the heading works around it, and it did here. But `--close` reports
  success while the entry keeps showing as waiting to be re-checked, so a finished re-check looks unfinished.
- **Repro:** close an entry whose heading is a bare `## TF-013`, then list the entries.
- **Expected:** after `tf-feedback.sh TfLens --close TF-013 "…"`, the entry reads `closed`.
- **Actual:** `--close` wrote its line straight under the heading and printed `TF-013 closed`, but the
  entry still read `fixed`. The heading pattern in `.tfcore/utils/tf_feedback.py` line 30 ends in
  `` [`\s—–-]*(.*)$ ``, and `\s` also matches a line break, so on a bare heading it runs on and takes
  the next line with text as the title — here, the closing line.
- **Encountered in:** TfLens, closing re-checked entries, 2026-09-11.
- **Workaround:** give the heading a title. TF-013 now has one, and reads `closed`.
- **Suggested fix:** use `[ \t]` in place of `\s` in that character class. Add a case with a bare
  `## XX-001` heading followed by a closing line.

**What is NOT affected.** Every other entry in the three TfLens feedback files has a title on its heading
line (TF-013 was the only bare heading, checked with `grep` on 2026-09-11), so every other state and
count the script prints is right. Where `--close` writes its line is right too.

#### Detail

One line reproduces it:
`python3 -c "import re;H=re.compile(r'(?m)^(#{2,3})\s+\`?([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*-\d+)\b[\`\s—–-]*(.*)$');print(repr(H.search('## TF-013\n\n- **Severity:** major\n').group(3)))"`
prints `'**Severity:** major'`: the line after the blank one has become the title. Before the close, the
listing showed TF-013's title as exactly that.

## TF-029 — `tf-log-miss.sh` run inside `*amend-docs` records its run over the amendment's window, so the amendment's own run record is refused

> ✅ **Closed 2026-09-11** — re-checked here: Re-checked 2026-09-11 in *amend-docs started 14:06:47Z: grep shows the TF-029 rule in .tfcore/utils/tf-log-miss.py; logging MISS-TfLens-20260911-23 inside the amendment printed 'Run record : none written — the running *amend-docs's own record covers this time', and the amendment's run record was then accepted with no void.

- **Severity:** minor
- **Blocks:** no. Voiding the log-miss record, then writing the amendment's record, repairs it.
- **Repro:** inside `*amend-docs`, run `tf-log-miss.sh <App> … --fixed` as step 10 says, then emit the
  amendment's run record.
- **Expected:** the misses are recorded and the amendment's run record is accepted.
- **Actual:** `.tfcore/utils/tf-log-miss.py` lines 93–97 take `started` from the phase marker whatever
  command owns it, so the first call wrote a `log-miss` run over the amendment's window (13:17:38 to
  13:36:49) with its tokens; later calls' run records were refused for overlap, silently. The
  amendment's own record was then refused under SCHEMA §2.7b.
- **Encountered in:** TfLens, `*amend-docs` of 2026-09-11, step 10.
- **Workaround:** a `run-void` for the log-miss record, then the amendment's record; both are in
  `docs/metrics/runs.jsonl` for 2026-09-11.
- **Suggested fix:** when the marker's `cmd` is not `log-miss`, write no run record; the enclosing
  command's record covers that time. Add a case that runs the script under an `amend-docs` marker.

**What is NOT affected.** The miss and fix records are correct, and their run ids rightly name the
amendment's start. A `*log-miss` run on its own is unaffected, and nothing was counted twice.

## TF-030 — `tf-doc-check` lets a requirement add a screen that has no screen row, UI design entry or mockup

- **Severity:** minor
- **Blocks:** no. The screen can be drawn afterwards, and here it was.
- **Repro:** add a BRD item that says "User can see, on a **Price providers** screen, …" with no row in
  the Screens and flow table, then run `bash .tfcore/utils/tf-doc-check.sh --app <App>`.
- **Expected:** a FAIL naming the screen the requirement introduces and the missing row and mockup.
- **Actual:** nothing about the screen. `cross_checks` in `.tfcore/utils/tf-doc-check.py` compares only
  the Screens and flow table with the UI design's entries, so a screen named nowhere but a requirement
  is never seen. The one finding that did appear, "REQ-UI-072 is a UI row without a mockup link", was
  on the checklist, which the command start records as old, so it never blocked. TfLens built and
  verified `/prices` with no design, and two amendments passed over it.
- **Encountered in:** TfLens, BRD-200, found by the owner on 2026-09-11 (MISS-TfLens-20260911-23).
- **Workaround:** draw the screen by hand: a row, an entry and a mockup.
- **Suggested fix:** treat a UI checklist row with no mockup link as blocking when its BRD item was added
  after the phase's mockups were drawn; or have the BRD check list every `**<Name>** screen` phrase in a
  requirement and fail when that name has no screen row.

**What is NOT affected.** Every screen that has a row in a Screens and flow table is cross-checked
against its UI design entry as before, and the mockup link check on existing UI rows still runs.

## TF-031 — `tf-verify-tests.sh --base` sets `BASE_URL`, which no Playwright config reads, so the tests run against whatever is on the default port

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11 build-phase verify: the app booted on 5014 and tf-verify-tests.sh --base http://localhost:5014 ran 207 browser tests against it; a targeted fix (the routing delta) showed up in the run, so the tests hit the booted build

- **Severity:** major
- **Blocks:** no. The project's `playwright.config.ts` now reads `BASE_URL` first, and the run carried on.
- **Repro:** boot the app on any port but the one `playwright.config.ts` names, then run
  `bash .tfcore/utils/tf-verify-tests.sh --base http://localhost:<that port>`.
- **Expected:** the browser tests open the address given to `--base`.
- **Actual:** every test opens the config's default address. `tf-verify-tests.sh` line 36 passes the
  address as the environment variable `BASE_URL`, but Playwright never reads that variable by itself,
  and the config `tf-verify-env.sh` writes has no `baseURL` line at all. Here the boot script picked
  port 5014 and every test failed with `ERR_CONNECTION_REFUSED at http://localhost:5099/login`. Worse
  case: an older build left running on the default port is tested instead, and its passes are recorded
  against the new build.
- **Encountered in:** TfLens `*verify` of 15 rows, 2026-09-11.
- **Workaround:** `baseURL: process.env.BASE_URL || process.env.TFLENS_BASE_URL || 'http://localhost:5099'`
  in the project's `playwright.config.ts`.
- **Suggested fix:** write `baseURL: process.env.BASE_URL` into the config template in
  `tf-verify-env.sh`, and have `tf-verify-env.sh` fail a config that sets a `baseURL` without reading
  `BASE_URL`.

**What is NOT affected.** `tf-verify-screens.sh`, `tf-assets.sh` and `tf-mockup-parity.sh` take the
address as their own argument and drive it directly. Earlier TfLens runs booted on port 5099, the
config's default, so their results were taken against the right build.

## TF-032 — `tf-verify-screens` asks for every mockup control at every width, so a sidebar the mockup hides on a phone fails the render check

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11 build-phase verify: tf-verify-screens --list over /misses, /effort, /prices at 1280 and 390 with the sidebar off the page on a phone — render OK at both widths, no app-sidebar finding (tests/.artifacts/verify/screens.json)

- **Severity:** major
- **Blocks:** no. The verdicts were written as the checker graded them, and each affected row's Remark says the finding is this fault.
- **Repro:** a mockup whose `aside data-testid="app-sidebar"` is hidden below 768px (a slide-out menu), an app that
  adds the sidebar to the page only when the menu is opened, then
  `bash .tfcore/utils/tf-verify-screens.sh --list tests/.artifacts/verify/list.json --base <url> --storage-state <file>`.
- **Expected:** at 390px the sidebar is not required, because the mockup does not show it at that width.
- **Actual:** `anchored control "app-sidebar" is not on the page` at 390px on `/misses` and `/effort`, the only
  finding at that width. `anchorsOf()` reads the mockup's markup once, with no width, and `grade()` fails any
  anchor absent from the page. Tapping the menu shows all nine items, as owner decision 4 requires.
- **Encountered in:** TfLens `*verify` of 15 rows, 2026-09-11. Twelve rows written `RENDER-FAIL` and twelve misses emitted from this alone.
- **Workaround:** none in the tool; the adjudication is in each row's Remark, with screenshots `tests/.artifacts/verify/screens/*-390-menu-open.png`.
- **Suggested fix:** render the mockup at each width and require only the anchors the mockup itself shows at that width.

**What is NOT affected.** Desktop width, and every anchor the mockup shows at both widths. The overlap and
clipping checks passed on all three screens at both widths.

## TF-033 — `tf-verify-screens --login-path` clicks the first element whose test id contains "login", which is a form field, so sign-in never submits

- **Severity:** minor
- **Blocks:** no. A fresh `--storage-state` file was made by signing in through the form, and the run carried on.
- **Repro:** a sign-in page whose fields are `data-testid="login-email"` / `"login-pass"` and whose button is
  `"login-submit"`, then `tf-verify-screens.sh … --login-path /login --user <u> --password <p>`.
- **Expected:** the tool presses the sign-in button.
- **Actual:** `LOGIN failed … still on the sign-in page`, and every screen reads "redirected to the sign-in page".
  The button locator in `login()` lists `[data-testid*="login" i]` first, and `.first()` takes the email field.
- **Encountered in:** TfLens `*verify` of 15 rows, 2026-09-11.
- **Workaround:** `--storage-state tests/.artifacts/verify/storage-state.json`, written by a Playwright sign-in.
- **Suggested fix:** prefer `button[type="submit"]` and test ids containing "submit" or "signin" over "login", and never a text field.

**What is NOT affected.** `--storage-state` and `--cookie` sign-in, and the other verify tools.

## TF-034 — `tf-verify-boot.sh` keeps one state file and one log for the whole repository, so the parallel builders `*build-phase` spawns stop and overwrite each other's apps

- **Severity:** major
- **Blocks:** no. Each builder booted its own app on its own port and put the shared state file back afterwards, and the run carried on.
- **Repro:** `*build-phase` with two or more clusters, each told by its prompt to smoke with `tf-verify-boot.sh start`. Builder 1 runs
  `tf-verify-boot.sh start --port 5147`, builder 2 runs `tf-verify-boot.sh start --port 5261`, then builder 2 runs `tf-verify-boot.sh stop`.
- **Expected:** each builder starts and stops only its own app.
- **Actual:** the script writes the state to the fixed path `tests/.artifacts/verify/boot.json` and the log to `tests/.artifacts/verify/app.log`
  (lines 27, 49 and 98). The second start overwrites the first builder's state and empties its log, and `stop` kills whichever app the file
  names last. In TfLens on 2026-09-11 one builder stopped another builder's app on port 5147 and had to restart it, and a timed-out start left a
  stray app holding the build output, which could have locked the next build.
- **Encountered in:** TfLens `*build-phase`, five clusters in parallel, 2026-09-11.
- **Workaround:** each builder booted on its own `--port`, copied the state file back after its own start, and stopped its app by process id.
- **Suggested fix:** key the state file and the log on the port (`boot-<port>.json`, `app-<port>.log`), let `stop --port <n>` stop only that one,
  and keep `boot.json` pointing at the verifier's own app.

**What is NOT affected.** A single `*verify` run, which boots one app, and any run with one cluster. The apps themselves built and ran correctly.

## TF-035 — `tf-build.sh` answers a locked output file by rebuilding on the Windows side in the same `obj/`, and the app it passes serves stylesheets that match nothing

- **Severity:** major
- **Blocks:** no. Stale files were rewritten by hand; the smoke ran on its own configuration.
- **Repro:** on WSL, hold `bin/Debug` with a running app, then `bash .tfcore/utils/tf-build.sh`; rung 2 fails
  `MSB3021 … Access to the path '…/TrBlazeUI.Components.dll' is denied`.
- **Expected:** a locked output file reads as a lock (retry, or NOT-RUN).
- **Actual:** `MSB3021` matches neither the code-error pattern nor `WRONG_RUNG` (lines 103, 128), so rung 3 (`winrun dotnet`) rebuilds the
  same `obj/` from Windows and prints PASS. Blazor hashes scoped-stylesheet names from the path and `_ProcessScopedCssFiles` is
  timestamp-incremental, so `.rz.scp.css`/`TfLens.styles.css` keep WSL names while `TfLens.dll` carries Windows ones: 29 scope names in
  the dll, 27 in the bundle, **none in common**. `/effort` drew five KPI tiles one per row at 1280; an earlier fall-through mismatched
  `Effort.razor.css`/`StatTile.razor.css` (`b-ba2vancesw` vs `b-rrwc1bouo3`).
- **Encountered in:** TfLens `*build-phase`, five parallel clusters, 2026-09-11; locks from the other clusters' apps (TF-034).
- **Workaround:** touch the affected `.razor.css` files; smoke via `tf-verify-boot.sh start --port <n> --config <name>`, a fresh
  `obj/<name>` built by one rung.
- **Suggested fix:** treat `MSB3021`, `MSB3027`, "being used by another process" and "Access to the path … is denied" as a lock, not a
  wrong rung; never cross WSL→Windows rungs over one `obj/` without a separate `BaseIntermediateOutputPath` or clearing
  `obj/*/scopedcss`.

**What is NOT affected.** Compile errors still FAIL; builds whose rungs stay on one side (macOS, Linux, Windows, Docker) are consistent;
unit and guardrail tests don't read scoped CSS.

## TF-036 — `tf-verify-screens` measures an inline element that wraps across lines by its bounding box, so two sentences on shared lines "overlap"

> ✅ **Closed 2026-09-11** — re-checked here: 2026-09-11 build-phase verify: /effort at 1280 carries the wrapping wall-clock sentences (kpi-wallclock-derived, kpi-wallclock-recomputed) and tf-verify-screens reports visual OK with no overlap finding (tests/.artifacts/verify/screens.json)

- **Severity:** major
- **Blocks:** no. The finding is adjudicated in the affected rows' Remarks with the fragment measurement below.
- **Repro:** a paragraph holding two adjacent inline elements that each carry a `data-testid` and each wrap over several lines, e.g.
  `<b data-testid="a">…</b> · <span data-testid="b">…</span>` inside a narrow tile, then `tf-verify-screens.sh --base <url> …`.
- **Expected:** no overlap, because no pixel of one is drawn over the other.
- **Actual:** `kpi-wallclock-derived overlaps kpi-wallclock-recomputed at 1280px` on TfLens `/effort`. `inspect()` takes
  `getBoundingClientRect()`, which for an inline element is the union of its line fragments. `a` runs from line 1 to line 3 and `b` from line 3
  to line 7, so the two unions share line 3 across the tile's full width. Measured with `getClientRects()`: the two elements' fragments
  intersect over **0 px²**. The union-box intersection is 30% of the smaller box, which is past the tool's 25% "a touch" floor.
- **Encountered in:** TfLens `*build-phase` smoke of `/effort`, 2026-09-11 (`tests/.artifacts/verify/effort/screens.json`).
- **Workaround:** none in the tool. The screen was judged by the fragments.
- **Suggested fix:** for an element whose computed `display` is `inline`, compare its `getClientRects()` fragments pairwise instead of its bounding box.

**What is NOT affected.** Block and inline-block controls, which are the overwhelming majority, and the render check. A genuine overlap between
two inline elements is still found by the fragment comparison.

## TF-037 — `tf-verify-tests.sh` keeps only the first line `tf-build.sh test` prints, so a note line before the result makes every unit test read as not run

> ✅ **Closed 2026-09-12** — re-checked here: Forced the note condition (set src/TfLens/obj/.tf-build-side to windows with scopedcss present) and ran tf-build.sh build: the note is the first line, the PASS verdict is the second, so the old head -1 would still have taken the note. tf-verify-tests.sh --no-browser over the same condition printed 'unit tests: PASS  test on wsl via ~/.dotnet/dotnet (rung 2)' and tests.json records unit.ran true with 19 rows PASS, 0 FAIL. tf-build.sh probe printed two clean lines (platform/target and the rung order) and no error line, exit 0.

- **Severity:** major
- **Blocks:** no. The unit tests were re-run without the note and the verdict read the real results.
- **Repro:** build from the Windows side of a WSL machine (or let `tf-build.sh` fall to a Windows rung), then
  `tf-verify-tests.sh --base <url>` from WSL.
- **Expected:** the unit-test line reads `PASS test on wsl …` and tests with a row id count for that row.
- **Actual:** `tf-build.sh` prints `note  the scoped stylesheets were built on the other side …` before its PASS line;
  `tf-verify-tests.sh` line 54 takes `| head -1`, records the note, finds no log path and writes `"unit": {"ran": false}`. On 2026-09-11
  all 975 TfLens unit tests passed, yet 17 rows (REQ-FN-072–076, 106–111, 113, 114; REQ-NFR-013, 014, 022, 023) lost their only test and
  would have read "not verified". Same day: line 141 runs `wc -l < "$LOG" 2>/dev/null` on a `…-test-<pid>.log` not yet created, the `<`
  fails before `2>/dev/null` applies, and that shell error becomes the first line.
- **Encountered in:** TfLens `*build-phase` chaining `*verify all`, 2026-09-11.
- **Workaround:** ran `tf-build.sh test -- --logger "console;verbosity=normal"` alone, then fed its PASS line and log, with the Playwright
  report, to the mapping step.
- **Suggested fix:** take the line starting `PASS`, `FAIL` or `NOT-RUN` (`grep -m1 -E '^(PASS|FAIL|NOT-RUN)'`), not the first line.

**What is NOT affected.** The browser tests, read from the Playwright report, and `tf-build.sh`, whose result line is right.

## TF-038 — `tf-mockup-parity` counts a transparent border as a visible one, and reads an icon's colour from a border it does not draw

> ✅ **Closed 2026-09-12** — re-checked here: Ran tf-mockup-parity.sh over /effort, /misses and /prices at 1280 and 390. On /prices the findings fell from 15 to 6: every ghost and primary button pair is gone (prices-remove-anthropic, -openai, -opencode-go, -openrouter, prices-add, prices-save-rate) and the chip-icon finding 'semantic colour differs - mockup accent, app neutral' on prices-standing-note is gone. No 'semantic colour differs' finding appears on any of the three screens now. The one badge/stroke pair left, on prices-filter-openrouter, is a real difference, not the old false positive: a computed-style probe reads border-top 1px solid rgb(38,38,38) in the mockup and 0px in the app, so a drawn border is still read. That difference is an app-side gap, logged separately.

- **Severity:** major
- **Blocks:** no. Every finding was checked by eye against the mockup and recorded as the tool's error.
- **Repro:** a mockup button `.btn.ghost.sm` with `border:1px solid transparent`, an app ghost Button with no border, and a Lucide icon in
  a coloured chip on a page whose reset sets `border-color` everywhere, then `tf-mockup-parity.sh --screen prices=/prices`.
- **Expected:** two borderless buttons match; an icon drawn blue in both matches.
- **Actual:** `strokeOf()` sets `style` from border width alone (line 152) and `chromeOn()` calls an element "ringed" when
  `borderTopWidth > 0` (line 172), so a transparent 1px border reads as a solid ring — "mockup renders this as a badge/pill" and "border
  style differs — mockup solid, app none" on every ghost and primary button. `semanticColor()` reads `borderTopColor` before `color`
  (line 137) without checking width, so a borderless svg takes the reset's grey: "semantic colour differs — mockup accent, app neutral".
  On `/prices` 13 of 15 findings are these two causes; same on `/misses` and `/effort`.
- **Encountered in:** TfLens `*build-phase` fix cycle 1, 2026-09-11.
- **Workaround:** none in the tool; every finding is adjudicated in `tests/.artifacts/verify/*-fix/findings-verdict.md`.
- **Suggested fix:** treat a border as present only when its width **and** its colour's alpha are above zero, in both `strokeOf()` and
  `chromeOn()`; in `semanticColor()` skip `borderTopColor` when `borderTopWidth` is 0.

**What is NOT affected.** Fills, real borders, text colours on elements that carry no border, and the other five parity classes.

---

## TF-039 — `tf-mockup-parity`'s `clip` clause measures descendants unclipped, so a table that scrolls inside its own container is reported as cut off

> ✅ **Closed 2026-09-12** — re-checked here: Ran tf-mockup-parity.sh --screen effort=/effort --widths 1280,390. At 390px there is no 'content is cut off horizontally' finding at all: effort-phases, effort-routing and effort-page > div[7], all three reported clipped at 390 in the pre-fix run (tests/.artifacts/verify/effort-fix/parity-full.json), are clean. The only clip finding left is app-sidebar at 1280, which is on the same two pre-fix runs as well, so it predates this fix and is a separate matter.

- **Severity:** minor
- **Blocks:** no. The three findings were checked against screenshots and adjudicated in the verdicts file.
- **Repro:** a card holding a wide table in an `overflow-x: auto` wrapper (BRD-144's rule) **and** any sub-2px element — an icon `path`,
  a collapsed `CollapsibleContent` — then `tf-mockup-parity.sh --screen effort=/effort --widths 390`.
- **Expected:** no `clip` finding: the card does not overflow (`scrollWidth == clientWidth == 340`) and the mockup scrolls the same table
  at 390 (`docs/mockups/effort.html:176`).
- **Actual:** `content is cut off horizontally; the mockup is not clipped` on `effort-phases`, `effort-routing` and their grid at 390.
  `clipOf()` (`tf-mockup-parity.mjs:225`) uses `scrollWidth - clientWidth` only while the subtree holds no hidden element; TF-012's
  sr-only guard also catches any box under 2px, so the fallback takes the right-most `getBoundingClientRect()` of every visible
  descendant, unclipped by ancestor scrollers — columns scrolled out of view count as card overflow. The mockup, with no such element,
  keeps the `scrollWidth` branch.
- **Encountered in:** TfLens `*build-phase` fix cycle 1, `/effort`, 2026-09-11
  (`tests/.artifacts/verify/effort-fix/parity-full.json`, verdict 8).
- **Workaround:** none in the tool; read every 390px `clip` finding against the screenshot.
- **Suggested fix:** in the fallback branch, intersect each descendant's rectangle with the clip rectangle of every ancestor whose
  `overflow-x` is not `visible`, as TF-021 fixed `tf-verify-screens`. Raising the 2px floor would not help — a real sr-only span flips
  the branch too.

**What is NOT affected.** Vertical clipping, the `scrollWidth` branch, the other six classes, and genuinely clipped cards, which either
branch still reports.

## TF-040 — a command that chains the verifier can never record its own first segment: the emitter refuses any run that starts before the last record ends

> ✅ **Closed 2026-09-12** — re-checked here: Reproduced TF-040's exact shape on a sandbox stream (TF_METRICS_ROOT to a scratch dir, so the real runs.jsonl was untouched). Wrote the chained verify's record first (2026-09-11T18:13:36Z to 21:13:15Z), then emitted the build's own first segment (16:53:16Z to 18:13:36Z) after it: appended, the stream went to two records. A run that really overlaps (17:30:00Z to 19:00:00Z) was still refused, and the refusal named the record it collides with - 'overlaps the verify-phase run of TfLens already on the stream (2026-09-11T18:13:36Z to 2026-09-11T21:13:15Z)' - and appended nothing.

- **Severity:** major
- **Blocks:** no. The segment after the chained verify was recorded, and the run record the status gate demands exists.
- **Repro:** `*build-phase`, whose task file says to start with `tf-phase.sh start` (marker 16:53:16Z), chain `*verify` inline at step 6
  (which writes its own run record, 18:13:36Z to 21:13:15Z), then append the build's own run record at step 8 with that marker as `started`.
- **Expected:** `build-phase` records the work it did before the verify.
- **Actual:** `tf-emit.sh runs` answers `REFUSED — this run starts at 2026-09-11T16:53:16Z, before the previous run for TfLens ended at
  2026-09-11T21:13:15Z`, and refuses a back-dated record even when its `ended` (18:13:36Z) precedes the later run's `started`: the check
  compares against the last record on the stream, not against the interval. The first 80 minutes of the build — five builder clusters, 18 rows —
  are on no run record, so effort and token totals for `build-phase` are that much too small.
- **Encountered in:** TfLens `*build-phase` with a chained `*verify`, 2026-09-11.
- **Workaround:** the build recorded one run from the verify's `ended` to the end of the pass, carrying `"mode":"fix"`.
- **Suggested fix:** accept a record whose whole interval lies before the last record's `started`, or let a nested run be declared
  (`--nested-of <started>`) so the outer command records its own span with the inner one subtracted.

**What is NOT affected.** A command that chains nothing, and the chained verify's own record, which is written correctly.

## TF-041 — `tf-doc-check` says the Phases document does not exist when it exists but was not named on the command line

> ✅ **Closed 2026-09-12** — re-checked here: Ran bash .tfcore/utils/tf-doc-check.sh docs/TfLens-Checklist.md docs/TfLens-P2-Checklist.md docs/TfLens-P3-Checklist.md, without naming docs/TfLens-Phases.md. No 'Phases document' line appears anywhere in the output (grep count 0) and the run ends 0 FAIL, 179 WARN over 3 documents, with the 50 old findings carried as OLD. The cross-phase rules still ran.

- **Severity:** minor
- **Blocks:** no. The status gate passed once the file was named in the same command.
- **Repro:** a project with `docs/<App>-P2-Checklist.md` and `-P3-Checklist.md` present and `docs/<App>-Phases.md` on disk, then
  `bash .tfcore/utils/tf-doc-check.sh PROJECT-STATUS.md docs/<App>-P3-Checklist.md docs/<App>-Checklist.md docs/<App>-P2-Checklist.md`.
- **Expected:** either the file is read from disk, or the message says it was not checked.
- **Actual:** `FAIL docs/TfLens-Phases.md: phase files exist (P2, P3) but the Phases document does not; write it from app-phases-tmpl.md`
  (tf-doc-check.py line 1182), although the file is there and 3.6 KB. The rule tests the parsed document set, which holds only the arguments.
  An agent reading that line would write a document that already exists, over the top of the real one.
- **Encountered in:** TfLens `*build-phase` status gate, 2026-09-11.
- **Workaround:** name `docs/<App>-Phases.md` in the same `tf-doc-check.sh` call; the FAIL then disappears.
- **Suggested fix:** load the Phases document from disk for this cross-document rule, or word it as "was not checked in this run".

**What is NOT affected.** Every rule over documents actually named on the command line.

---

## TF-042 — `tf-mockup-parity`'s `clip` clause calls content cut off when nothing clips it, so one control straddling its container's edge fails every screen

- **Severity:** major
- **Blocks:** no. The rows are written `Needs re-verify` carrying the finding, never falsely `Verified`, and the run finished.
- **Repro:** a shell sidebar carrying a shadcn-style rail handle — a 16px button centred on the sidebar's right edge with
  `-translate-x-1/2` — inside a sidebar whose `overflow-x` is `visible`, then `tf-mockup-parity.sh --screen effort=/effort`.
- **Expected:** no `clip` finding. With `overflow-x: visible` and no clipping ancestor, the button is painted in full; nothing is cut off.
- **Actual:** `content is cut off horizontally; the mockup is not clipped` on `app-sidebar` at 1280 on every screen graded.
  `clipOf()` (`tf-mockup-parity.mjs:248`) takes `scrollWidth - clientWidth` without asking whether the element clips.
  Measured: app `scrollWidth` 263, `clientWidth` 255, own `overflow-x` `visible`, every ancestor to `html` also `visible`,
  the rail drawn whole. The mockup's sidebar is 255/255 with `overflow-x: hidden`, so the sentence is backwards too. First
  failing check on all 13 rows of this verify, across three screens.
- **Encountered in:** TfLens `*verify ui`, 2026-09-12.
- **Workaround:** none in the tool; read every `clip` finding against that element's own `overflow-x` before believing it.
- **Suggested fix:** in `clipOf()`, report no horizontal overflow when the element's own `overflow-x` is `visible` and no
  ancestor up to the compared root clips — `paintedRight()` already walks exactly that chain.

**What is NOT affected.** Vertical clipping, an element that really does clip (`overflow-x` `auto`, `hidden` or `scroll`), the
TF-039 fallback branch, and the other six comparison classes.

---

## TF-043 — `tf-verify-boot.sh` boots every concurrent builder against one shared `obj/`, so a running app serves an empty scoped-CSS bundle with a 200 and every geometry measurement silently becomes meaningless

- **Severity:** blocker
- **Blocks:** no — the pass completed. But it is filed blocker because a run that hits it produces
  *confident wrong measurements* rather than a failure: the smoke reports geometry it never measured,
  and a builder acting on it will "fix" a screen that was never broken and delete a rule that was
  doing its job. Nothing downstream can tell the difference afterwards.
- **Repro:** run `*build-phase` with more than one cluster, as this framework's own `build-phase.md`
  step 2 instructs ("Spawn every cluster in one turn"). Each cluster calls
  `bash .tfcore/utils/tf-verify-boot.sh start web --port <n>`, which runs
  `dotnet run --project src/TfLens/TfLens.csproj --urls http://localhost:<n>` — the **same project and
  the same `obj/`** for every port. Then, from any of them, fetch the Blazor scoped-CSS bundle twice:

  ```
  curl -s -o /dev/null -w '%{http_code} %{size_download}\n' -H 'Accept-Encoding: gzip'     .../TfLens.<fp>.styles.css
  curl -s -o /dev/null -w '%{http_code} %{size_download}\n' -H 'Accept-Encoding: identity' .../TfLens.<fp>.styles.css
  ```

- **Expected:** both return the same stylesheet. A browser, which always sends `Accept-Encoding: gzip`,
  gets the app's scoped CSS.
- **Actual:** measured on this machine on 2026-09-12 with four instances up, all on one `obj/`:

  | port | `Accept-Encoding: gzip` | `Accept-Encoding: identity` |
  |---|---|---|
  | 5093 | 200 · 131,738 B · `content-encoding: gzip` | 200 · 131,685 B |
  | **5361** | **200 · 0 B · no content-encoding** | 200 · 131,685 B |
  | **5371** | **500 · `ArgumentOutOfRangeException`** | 200 · 127,584 B |
  | 5401 | 200 · 128,548 B · `content-encoding: gzip` | 200 · 128,500 B |

  Two of four were broken; **one of the two returned 200 OK with a zero-length body**. The 500 names
  the mechanism exactly:

  ```
  System.ArgumentOutOfRangeException:  (Parameter 'count')  Actual value was 36757.
     at Microsoft.AspNetCore.Http.SendFileFallback.SendFileAsync(Stream, String filePath, Int64 offset, Nullable`1 count, …)
     at Microsoft.AspNetCore.StaticAssets.StaticAssetsInvoker.SendAsync(StaticAssetInvocationContext)
  ```

  `Microsoft.AspNetCore.StaticAssets` serves a pre-compressed asset by `(offset, count)` taken from the
  manifest it read **at startup**, out of `obj/Debug/net10.0/compressed/*.gz`. A sibling builder's
  `dotnet run` rewrites those files, the length on disk no longer matches the length in the running
  app's manifest, and `SendFileAsync` either throws (500) or sends nothing (200, zero bytes).

  The consequence for a builder is the part that matters: with the bundle empty the browser parses
  **zero** scoped-CSS rules, so every `::deep` rule is inert, every page-scoped rule is absent, and the
  page still renders and still returns 200 everywhere. Measured on the affected instance, the app shell
  header came out **105px instead of 64px** and the framework switch **40px instead of 34px** — which is
  exactly the shape of a real layout regression. This pass had six clusters each deleting `::deep`
  workarounds and measuring whether the library's own fix had taken over; on a poisoned instance every
  one of those measurements would have read "the workaround is gone and the fix works" whether or not
  it did.

- **Encountered in:** TfLens `*build-phase`, 2026-09-12, seven clusters. Found by cluster D, which
  noticed the header measuring 105px, checked per-encoding with `curl` rather than believing the
  screenshot, and switched to a published copy. Reproduced independently by the orchestrator against
  the live instances above.
- **Three further symptoms of the same shared `bin/`+`obj/`, from cluster C in the same run.** These
  are loud rather than silent, so they cost time instead of correctness — but they have the same cause
  and the same fix, and they show the corruption is not confined to the compressed-asset manifest:
  1. an app killed mid-run by a sibling's build (`Application is shutting down`);
  2. `BadImageFormatException` at startup, from a WSL rung and a Windows rung writing the same `bin/`
     — the ladder already clears `obj/**/scopedcss` when a build changes side (TF-035's note), but that
     guard is per-build, and here two *different* builders are on the two sides at once;
  3. an orphaned Windows-side `TfLens.exe` (PID 18848) holding `bin/` open after its parent was killed,
     which no later rung could get past until it was killed by hand.
- **Workaround:** do not smoke `dotnet run` out of the shared tree while another builder is building.
  `dotnet publish -o <a directory of the builder's own under tests/.artifacts/>` (copying
  `database/001-schema.sql` beside the DLL) and running that published copy is isolated and works.
  Before trusting any geometry, fetch the scoped-CSS bundle with `Accept-Encoding: gzip` and confirm the
  length is not 0 — an empty stylesheet is a 200, so nothing else reveals it.
- **Suggested fix:** `tf-verify-boot.sh` already isolates the state file and the log per port and its
  own comment claims "builders booting side by side never empty or stop each other's" (TF-034) — but
  that isolation stops at the state file. The build output is shared, and for a Blazor app the build
  output *is* the stylesheet. Give each port its own output: pass
  `--property:BaseOutputPath=tests/.artifacts/verify/build-<port>/ --property:BaseIntermediateOutputPath=tests/.artifacts/verify/obj-<port>/`
  on the `dotnet run` rung, or publish once per port and serve that. Failing that, have `start` refuse a
  second concurrent boot of the same project unless the caller has asked for an isolated output, so the
  hazard is loud instead of silent. The framework should not hand a builder a poisoned instance and let
  it write measurements into a checklist.

**What is NOT affected.** Anything that is a DOM fact rather than a painted one: element presence, text
content, row counts, `data-testid` uniqueness, tab switching, binding, Escape and click behaviour, and
the compiler. A single-instance run is unaffected — the corruption needs a concurrent build. The
identity encoding always returned the correct bytes, so a tool that fetches without `Accept-Encoding:
gzip` sees nothing wrong, which is part of why this survived to be found by eye.

**A guard worth adopting, from cluster C in the same run.** Rather than only checking the bundle by
hand before measuring, it made the check part of the suite: `tests/verify/ui-prices.spec.ts` asserts
three known scoped-CSS rules are live in the page (`flush padding 0px`, `foot border-top 1px`,
`message-idle display none`) **before** it measures anything else, so the spec fails loudly instead of
blessing a page that loaded no scoped CSS at all. Any screen suite whose findings depend on `::deep`
rules should open the same way. It costs three assertions and it closes the hole on that screen
permanently, whatever the framework decides to do about the boot.

**Two further variants found later the same day — both defeat the byte-count check this entry first
suggested, and each was caught only because a cluster asserted a rule instead of a size.**

1. **The fingerprinted URL and the unfingerprinted one do not fail together (cluster D).** Curling
   `/TfLens.styles.css` returns a healthy body while `/TfLens.<fingerprint>.styles.css` — the URL the
   page actually links — serves empty. Cluster D "verified" the bundle against the unfingerprinted
   path, measured a 105px header, and only then realised the check had been meaningless. **Only the
   fingerprinted path is a valid check.**
2. **A correct-sized bundle whose scope ids do not match the DOM (cluster E).** On a Release publish
   the bundle was a healthy 125,625 bytes and every page-scoped rule still missed:

   ```
   DOM stamp on /repos elements ....... b-gf5ckms75x     (Debug scope)
   rule in the served bundle .......... .tflens-repos-filter[b-viw4vvlnt2] { max-width: 240px }   (Release scope)
   computed max-width on that element . none
   ```

   Blazor derives a **different scope id per configuration** for the same file, and the shared
   `src/TfLens/obj|bin` produced a publish carrying Debug-stamped Razor against a Release CSS bundle.
   Identical consequence to the empty bundle — every scoped and `::deep` rule inert — and **invisible
   to any size check**. Note also that a private `-p:BaseIntermediateOutputPath` is *not* a way out: it
   collides with the in-tree `obj/` on duplicate `AssemblyInfo.cs` (`CS0579`). Publishing **Debug**,
   whose tree is self-consistent, is what worked.

**So the workaround above is upgraded.** Do not check the bundle's size. Assert a **real computed
value** for two or three rules you know are live on the screen — which also catches the scope
mismatch, the empty body and the wrong URL in one step. `tests/verify/_helpers.ts` now carries
`assertScopedCssLoaded(page, rules)` for exactly this, and clusters B, C, D, E and F each guard their
own suite with it. Cluster E's version additionally requires the `b-<scope>` attribute on pages that
own a `.razor.css`, and passes `ownsScopedCss: false` for the two that legitimately have none.

**This strengthens the suggested fix rather than changing it.** Per-port build output would prevent
all three variants at once, because all three come from two builders sharing one tree.

**A fourth face, and the worst of them: a stale binary (cluster D, same run).** Every variant above
corrupts the *stylesheet*. This one corrupts the *application*. Cluster D published from the shared
`src/TfLens/obj/` while other clusters were building, and the resulting `TfLens.dll` **predated its
own source edit** — so the app faithfully rendered the previous markup. The page loaded, the
stylesheet was healthy, no request failed and nothing errored. On the strength of two probe runs
against that app it filed a library defect ("`Badge.Truncate` does not truncate") that does not exist,
and restored a CSS workaround to compensate for it. The finding was withdrawn only after the shipped
assembly was probed directly and the measurement repeated on a fresh build.

**A scoped-CSS liveness gate does not catch this**, because the CSS is fine. Nor does a byte count,
a fingerprint check, or a scope-id check. The rule that does catch it is the general one this whole
entry keeps arriving at: **on a contended tree, a measurement is evidence only if the artefact
measured is known to be newer than the change it is supposed to demonstrate.** Cheap ways to know:
publish to a directory of your own and check the DLL's mtime against the source file you edited, or
assert something in the page that only your change could produce before you measure anything else.

Three of the four faces here cost a cluster time. This one cost a **wrong entry in an upstream
feedback file**, which is the kind of error that outlives the run — an upstream team could have spent
a day looking for a bug that was never there. That is the case for fixing the isolation rather than
documenting the workaround.

**A consequence of the workaround, for whoever implements the fix.** Once clusters follow the advice
above — publish your own copy, run it on your own port — `tf-verify-boot.sh` can no longer stop them,
because it only knows about apps it started itself. After this run two orphaned instances were still
listening (ports 5371 and 5426) from clusters killed mid-flight by a session rate limit;
`tf-verify-boot.sh stop --port 5371` answered `STOPPED nothing was started on port 5371`, and they had
to be identified from `/proc/<pid>/cmdline` and killed by hand. They were inert — not rebuilding — but
they still held the shared `bin/`, which is the lock that produces `MSB3021` on the next build.

This is not a separate defect; it is the same one seen from the other end. **Per-port build output
would let the tool own every instance again**, so builders would have no reason to boot outside it and
nothing would be left un-stoppable. If instead the workaround is blessed as the supported path, then
`stop` needs to be able to stop an app it did not start — matching on the port's listening process
rather than on its own state file — and `status` should list what is listening, not only what it
remembers.

---

## TF-044 — `tf-verify-screens.mjs` fills the sign-in form before the Blazor circuit attaches, so the email is blanked by the first interactive render and every screen grades UNREACHABLE

- **Severity:** blocker
- **Blocks:** no — `--storage-state` is an escape hatch and the run completed through it. It is filed
  blocker because without that hatch the render and visual gates cannot grade a single screen of an
  authenticated app, and the failure is reported as **the application's** fault ("redirected to the
  sign-in page"), not the tool's.
- **Repro:** any Blazor Server app with a sign-in page, then
  `bash .tfcore/utils/tf-verify-screens.sh --list … --base <url> --login-path /login --user <u> --password <p>`.
- **Expected:** the tool signs in and grades the screens.
- **Actual:**

  ```
  FAIL Misses & rework (/misses) — render UNREACHABLE, visual n/a, 29 anchors — /misses redirected to the sign-in page (not signed in)
  FAIL Phase effort (/effort)    — render UNREACHABLE, visual n/a, 21 anchors — /effort redirected to the sign-in page (not signed in)
  FAIL Price providers (/prices) — render UNREACHABLE, visual n/a, 47 anchors — /prices redirected to the sign-in page (not signed in)
  LOGIN failed at http://localhost:5500/login: still on the sign-in page
  screens 3: render 0 OK / 0 failed / 3 unreachable; visual 0 OK / 0 failed
  ```

- **Root cause, measured — not inferred.** `login()` at `.tfcore/utils/tf-verify-screens.mjs:149`
  navigates with `waitUntil: 'domcontentloaded'`, which returns as soon as the **static SSR HTML**
  parses, and fills immediately. The Blazor circuit then attaches and its first interactive render
  replaces the input nodes, discarding what was typed. A probe reproducing the tool's exact sequence
  and reading the values straight back:

  ```
  A — tool sequence (domcontentloaded + immediate fill)
     right after fill : {"user":"","pass":13}
     after hydration  : {"user":"","pass":13}
     VALUES SURVIVED  : false

  B — fill, read back, retry
     signed in after 2 fill attempt(s) -> /
  ```

  The **email reads back empty the instant after `fill()` returns**; the password survives only
  because it is typed a moment later, on the far side of the same re-render. The form therefore
  submits with an empty email and the app correctly refuses it. **One retry is enough** — B signed in
  on its second attempt.

  Two facts rule out the app being at fault: the Playwright acceptance suite signed in against **the
  same app, the same credentials and the same running instance minutes earlier** (39/39 passed), and
  it does so because `tests/verify/_helpers.ts` was given exactly this settle-and-retry after cluster
  E hit the identical race there during this build.

- **Encountered in:** TfLens `*build-phase` → chained `*verify`, 2026-09-12, on `/misses`, `/effort`
  and `/prices`.
- **Workaround:** sign in with Playwright, write a storage state, and pass `--storage-state` instead
  of `--login-path/--user/--password`. With it the same three screens graded
  **render 3 OK / visual 3 OK / 0 unreachable, 97 anchors** on the very next run. The probe and the
  state-writer are kept at `tests/.artifacts/verify/login-race-probe.mjs`.
- **Suggested fix:** in `login()`, after filling, read both fields back and re-fill until the values
  stick before clicking submit — the loop is four lines and is already proven in this repo's
  `_helpers.ts`. `waitUntil: 'networkidle'` alone is **not** sufficient and neither is a fixed
  `waitForTimeout`: the correct test is that the typed value is still in the field. While there, note
  that the first-match user locator ends in `input[type="text"]`, which on a form whose email box is
  `type="text"` can select a different field entirely — the same family as TF-033.

**What is NOT affected.** Every other gate: the acceptance tests (they own their own sign-in), the
assets gate, mockup parity and perf when given a cookie or a storage state, and the whole tool on an
app with no sign-in page or on a server-rendered app that hydrates nothing. `--cookie` and
`--storage-state` both work. Only `--login-path` driving an interactive Blazor form is broken.

---

## TF-045 — `tf-mockup-parity` addresses elements by tag-and-index, so a component library that adds one wrapper makes every anchored child "missing" — and buries the real findings

> ✅ **Closed 2026-09-14** — re-checked here: Re-checked in *verify ui TfLens on 2026-09-14: tf-mockup-parity over /misses, /effort and /prices at 1280 and 390 with a signed-in session produced 0 findings of the missing class (the gate compared that clause 10, 10 and 2 times) and re-keyed 9, 1 and 1 relocated elements, where the 2026-09-13 runs failed the same 13 rows on tag-and-index missing findings. The 42 findings left are icon, wrap, badge and stroke findings on elements both sides carry, so the wrapper no longer makes anchored children read as missing.

- **Severity:** major
- **Blocks:** no. The rows are written `Needs re-verify` carrying the findings, never falsely
  `Verified`, and the run finishes. It is major because the gate reports **correct screens as broken**,
  and because the false findings outnumber the true ones roughly ten to one, which is how a gate stops
  being read.
- **Repro.** Any screen whose mockup is hand-written HTML and whose app is composed from a component
  library that emits its own wrapper elements — which is every screen in this project, and the normal
  case for shadcn-style libraries. Then
  `bash .tfcore/utils/tf-mockup-parity.sh --base <url> --screen misses=/misses`.
- **Expected.** An element the mockup anchors is compared with the element the app anchors. Where both
  carry the same content and the same treatment, no finding.
- **Actual.** Findings of the form:

  ```
  class : missing
  key   : miss-origin > div[0] > span[0]
  detail: the mockup draws a badge/pill here ("linked only") and the app renders no such element
          — the value is flattened into plain text
  app_text: null
  ```

  **The app renders it, as a badge, with the right text.** Measured on the live page, the same
  `<span>` carrying the same content:

  | anchor | mockup | app |
  |---|---|---|
  | `miss-origin` → "linked only" | depth 2, `<span>` | **depth 3**, `<span>` |
  | `miss-whymissed` → "…assessed" | depth 2, `<span>` | **depth 3**, `<span>` |
  | `miss-review-cost` → "copied · never computed" | depth 2, `<span>` | **depth 3**, `<span>` |

  One extra wrapper, every time, in the same direction. `CardHeader` composes it; the consumer never
  wrote it and cannot remove it. The gate walks `parent > div[0] > span[0]`, finds a `div` where it
  expected the `span`, and reports the value as absent — then adds *"the value is flattened into plain
  text"*, which is a specific and wrong diagnosis rather than "I could not locate it".

  Confirmed against the rendered DOM: `miss-whymissed` carries
  `<span class="inline-flex items-center rounded-full border px-2.5 py-0.5 …">87 of 91 misses
  assessed</span>` — a real `Badge`, correct content, correct pill treatment.

- **The scale of it, on three screens.** 69 findings, of which:

  | class | n | what they actually are |
  |---|---|---|
  | `missing` | 32 | the element exists one wrapper deeper — verified by hand on six of them |
  | `icon` | 24 | mostly *the app carries an icon the mockup does not* |
  | `wrap` | 7 | live data is longer than the mockup's sample (91 records against 41) |
  | `color` | 2 | the app computes `bg-alert-warning-bg` — amber, exactly as the mockup specifies — and is binned as "negative" |
  | `badge` / `stroke` | 4 | an `InputGroup` puts the border on the group, not the input |

  **Roughly four are worth a developer's time.** One of those four was real and is now fixed (a missing
  search magnifier). Finding it took reading all 69 by hand.

- **Encountered in:** TfLens `*build-phase` → chained `*verify`, 2026-09-12/13, on `/misses`, `/effort`
  and `/prices`. Thirteen rows cannot reach `Verified` on this gate although they pass build,
  acceptance (13/13), render, assets and visual.
- **Workaround:** none. The app cannot be restructured to match, because the extra wrapper belongs to
  the library, not to the consumer. Regenerating the mockups from the built DOM would trade a real
  design document for a snapshot of the implementation, which removes the gate's whole point — it
  could then never disagree with the app.
- **Suggested fix:** address elements by **`data-testid`**, not by tag-and-index. The framework already
  requires every comparable element to carry one — `mockups.md` §6: *"Anchor every element the build
  must match with `data-testid` … The verifier compares only anchored elements"* — so the identity is
  already there and the positional walk is redundant. Compare anchor-to-anchor and let the wrapper
  depth differ. Where a mockup anchors something the app does not, that is a genuine finding and
  survives. Failing that: when the positional walk misses, **search the subtree for the text before
  declaring it absent**, and say "could not locate" rather than "flattened into plain text" — a wrong
  cause in a finding costs more than a vague one.

**What is NOT affected.** The gate's premise, which is sound and has caught real drift before
(TF-008). Findings keyed on an element the tool did locate — the `wrap`, `clip` and `stroke` classes
compare measurements on a found element and are trustworthy once you have checked the element is the
one you meant. And `--screen`/`--cookie`/`--widths`, all of which work.

---

## TF-046 — `tf-mockup-parity` reports one extra icon once for every anchored ancestor that contains it

> ✅ **Closed 2026-09-14** — re-checked here: Re-checked 2026-09-14 11:4x against the installed .tfcore/utils/tf-mockup-parity.mjs (byte-identical to tests/.artifacts/verify/tf046/parity-after.mjs). Booted the published app on http://localhost:5014, signed in as the demo user, ran tf-mockup-parity.sh --screen misses=/misses --screen effort=/effort --screen prices=/prices --cookie <session>: 28 findings, 12 of them icon (was 26 of 42 on the 09:45 run), misses-period and effort-period each once per width at 1280 and 390, and no icon finding keyed on an ancestor of another; badge 5, wrap 9 and stroke 2 unchanged. Output tests/.artifacts/verify/parity.json.

- **Severity:** minor
- **Blocks:** no. No verdict changes: every row it touched also fails on `wrap` or `badge` findings,
  and a row is never written `Verified` on the strength of it.
- **Repro:** `bash .tfcore/utils/tf-mockup-parity.sh --base <url> --screen misses=/misses --cookie …`
  on a screen where the app draws one icon the mockup does not — here the chevron of a `Select`, where
  the mockup draws a native `<select>`.
- **Expected:** one `icon` finding, keyed on the innermost element that carries the icon.
- **Actual:** one finding per containing element: `misses-page > div[0]`, `misses-page > div[0] > div[1]`
  and `misses-period`, all *"app carries an icon the mockup does not"*, all for the same chevron. Across
  `/misses`, `/effort` and `/prices` at 1280 and 390, **14 of the 26 `icon` findings** are an ancestor
  repeating a finding already made on a descendant.
- **Encountered in:** TfLens `*verify ui`, 2026-09-14, `tests/.artifacts/verify/parity.json`.
- **Workaround:** none taken. The duplicates are read past.
- **Suggested fix:** before emitting an `icon` finding on an element, drop it when a descendant of that
  element already carries the same icon finding at the same width.

**What is NOT affected.** The `missing` class (TF-045's fix holds: 0 such findings), `wrap`, `badge`,
`stroke`, the verdict script, and the 12 `icon` findings that are not repeats.

---

## TF-047 — `tf-mockup-parity` grades a treatment one element away from where the app draws it, and grades wrapping on different text

> ✅ **Closed 2026-09-14** — re-checked here: Re-checked 2026-09-14 against the installed .tfcore/utils/tf-mockup-parity.mjs (updated 12:46, carries the TF-047 changes). Booted the published app with tf-verify-boot.sh on http://localhost:5014, signed in through /login as the demo user from tests/verify/_helpers.ts (tflensdemo@techierathore.com), and ran tf-mockup-parity.sh --screen misses=/misses --screen effort=/effort --screen prices=/prices --cookie <session> at 1280 and 390: exit 0, status measured, three PASS verdicts, 0 findings, 0 ungradeable, 0 without a mockup. Not an empty pass: misses compared 127 and 115 elements, effort 130 and 118, prices 117 and 105. The 15 findings this entry recorded (sidebar badge 3, filter badge and stroke 4, header icons 4, wrap on differing text 4) are all gone. Output tests/.artifacts/fix-parity/parity-tf047.json.

- **Severity:** major
- **Blocks:** no. Rows stay `Needs re-verify`. Major because, with the mockups updated on 2026-09-14,
  these are the **only** findings left on 13 TfLens rows.
- **Repro:** `tf-mockup-parity.sh --base <url> --screen misses=/misses --screen effort=/effort --screen prices=/prices --cookie …`
- **Expected:** a correct screen gives no finding.
- **Actual:** 15 findings, each checked in the browser against both sides:
  1. **Sidebar, 3.** "badge on `nav > a[4]`, app plain": the app's active `<a>` has radius 8px and the accent
     background, as the mockup's does; the tool paired it with the `<li>` around it.
  2. **Filter, 4** (`badge` + `stroke` on `prices-filter-openrouter`): the app draws the 1px border and 8px
     radius on the `InputGroup` around the anchored input.
  3. **Header icons, 4** (`miss-detail > div[0] > div[0] > div[1]`, `prices-provider-openrouter > …`): each
     side has exactly one icon in that header; one extra wrapper pairs a description with a toolbar.
  4. **Wrap, 4** (`miss-what`, `miss-taint-count`, `miss-sort-predates`): the wrap clause
     (`tf-mockup-parity.mjs:436`) compares row counts without checking the texts match; each pair differs.
- **Encountered in:** TfLens `*fix-issues`, 2026-09-14, `tests/.artifacts/fix-parity/`.
- **Workaround:** none; copying library wrappers into mockups would defeat the gate.
- **Suggested fix:** for `badge`/`stroke`, also accept the treatment on the anchor's nearest single-child
  ancestor or descendant; pair icon containers by content, as TF-045 did for badges; skip `wrap` when
  the digit-folded texts differ.

**What is NOT affected.** The `missing` class, `clip`, `color`, `token`, the verdict script, and every
finding on text that matches.

---

## TF-048 — `verify-phase` never says its browser checks must not share a signed-in user while they run

> ✅ **Closed 2026-09-14** — re-checked here: Re-checked 2026-09-14 against the installed tf-lock.sh, tf-verify-tests.sh, tf-verify-screens.sh and tf-mockup-parity.sh (15:06, each takes the shared lock). Booted the app with tf-verify-boot.sh on http://localhost:5014 and signed in as the demo user. At 16:56:19 started tf-verify-tests.sh --base http://localhost:5014 (scoped with TF_VERIFY_GREP to REQ-UI-072, 14 tests, so the pair fits in one run) and, while it ran, tf-mockup-parity.sh --screen misses=/misses --cookie <session>. Parity printed at 16:56:23: 'wait  another build or browser check is running in this repository (148937 LAPTOP-IBNQ33KO verify-tests 2026-09-14T16:56:19Z); this one starts when it finishes'. The tests finished at 17:01:24 (14/14 passed); parity produced its result only after that, at 17:01:38: exit 0, misses PASS, 0 findings, 242 elements compared, the same as parity run alone earlier today. Logs in tests/.artifacts/fix-parity/tf048/.

- **Severity:** minor
- **Blocks:** no. The false findings were caught and re-run before any verdict was written.
- **Repro:** in `*verify`, run `tf-verify-tests.sh` and `tf-mockup-parity.sh` (or `tf-verify-screens.sh`)
  at the same time against one app, signed in as the same test user.
- **Expected:** each check reads the page the design describes, whatever else is running.
- **Actual:** the acceptance tests change that user's saved settings (the header's framework switch)
  and the window size. A parity run beside them on 2026-09-14 reported 11 false `missing` findings at
  390 on `/misses` and `/effort`, with `/effort` comparing 192 elements instead of 248. The same run
  alone minutes later: exit 0, 3 PASS, 0 findings. Logged as `MISS-TfLens-20260914-01`.
- **Encountered in:** TfLens `*verify ui`, 2026-09-14, `tests/.artifacts/fix-parity/parity-concurrent.json`.
- **Workaround:** run the test groups first, then screens and parity one at a time.
- **Suggested fix:** one requirement line, "a verify runs its browser checks one at a time", with a
  check: `tf-verify-tests.sh`, `tf-verify-screens.sh` and `tf-mockup-parity.sh` take the same lock
  `tf-build.sh` already uses, so a second one waits.

**What is NOT affected.** Each check run on its own, the verdict script, and every verdict written on
2026-09-14.

---

## TF-049 — `handoff-phase.md` tells the agent to run `tf-build.sh --print`, which `tf-build.sh` does not have

> ✅ **Closed 2026-09-15** — re-checked here: 2026-09-15: ran 'bash .tfcore/utils/tf-build.sh --print' on WSL. It printed exactly 'dotnet build TfLens.slnx' and exited 0, with no lock and no build started.

- **Severity:** minor
- **Blocks:** no. The build and run commands were taken from the Architecture stack table and the
  Coding Standards, which the same paragraph also names.
- **Repro:** `bash .tfcore/utils/tf-build.sh --print`
- **Expected:** it prints the build command this repository uses, as `handoff-phase.md` §1 says.
- **Actual:** `--print` is not an option (the script takes `build|test|run|publish|probe`), so it is
  read as a target: `dirname: unrecognized option '--print'`, then `NOT-RUN no rung could build on wsl`.
  `probe` prints the platform and the rungs, but no command.
- **Encountered in:** TfLens `*handoff-phase`, 2026-09-14.
- **Workaround:** read the commands from the Architecture and Coding Standards documents.
- **Suggested fix:** add `--print` (resolve the command, print it, exit 0), or name `probe` in the task
  and make `probe` print the resolved command.

**What is NOT affected.** `tf-build.sh build`, `test`, `run`, `publish` and `probe`.

---

## TF-050 — `tf-devguide-list.py` reads `path:` in a Playwright spec as a page route, so screenshot files are listed as pages

> ✅ **Closed 2026-09-15** — re-checked here: 2026-09-15: ran 'bash .tfcore/utils/tf-devguide-list.sh TfLens' (exit 0). It reports 19 routes in code; 'Pages in code with no UIDesign screen' lists 16 routes, all @page routes from .razor files, and no .png (export-banner.png and export-surface.png are gone).

- **Severity:** minor
- **Blocks:** no. The two false lines were read past.
- **Repro:** `bash .tfcore/utils/tf-devguide-list.sh TfLens --update`
- **Expected:** only pages routed in the application's own code.
- **Actual:** under "Pages in code with no UIDesign screen" it lists
  `tests/.artifacts/parity/export-banner.png` and `export-surface.png`, both from
  `tests/verify/parity-gate-smoke.spec.ts`. The `.ts` route pattern (`tf-devguide-list.py:26`) matches
  `page.screenshot({ path: '…' })` at lines 52 and 75 of that spec, and `routes_in_code()` (`:89`) calls
  `walk()` without `skip_samples=True`, so `tests/` is searched although `SAMPLE_DIRS` names it.
- **Encountered in:** TfLens `*handoff-phase`, 2026-09-14.
- **Workaround:** none needed.
- **Suggested fix:** pass `skip_samples=True` in `routes_in_code()`, or skip `*.spec.*` and `*.test.*`.

**What is NOT affected.** The work list (Misses, Effort, Prices) and `@page` detection in `.razor` files.

---

## TF-051 — `tf-triage.sh demote` cannot reach a row in an earlier phase's checklist

- **Severity:** minor
- **Blocks:** no. The two rows were fixed and re-verified with `tf-verify-list.sh … --phase 1`, which does
  take a phase; only the demotion step before the fix could not be written.
- **Repro:** with `appPhase: 3`, run
  `bash .tfcore/utils/tf-triage.sh TfLens demote REQ-NFR-003 "<symptom>" --kind data-logic`
- **Expected:** the row in `docs/TfLens-Checklist.md` (Phase 1) moves to `Needs re-verify` with the remark,
  as it does for a Phase 3 row.
- **Actual:** `tf-triage: REQ-NFR-003 is not a row of docs/TfLens-P3-Checklist.md`. `checklist(app)`
  (`tf-triage.py:61-63`) resolves only the `appPhase` checklist and the script has no `--phase` option, while
  `tf-verify-list.py:204-211` accepts `--phase` and the verdict script follows the checklist `list.json` names.
- **Encountered in:** TfLens `*fix-issues`, 2026-09-15, for REQ-NFR-003 (secret-hygiene test) and
  REQ-NFR-015 (stale-cache test), both Phase 1 rows found failing by the Phase 3 verify.
- **Workaround:** none taken; the rows stay at their old status until the scoped verify rewrites them.
- **Suggested fix:** give `tf-triage.py` a `--phase N` option like `tf-verify-list.py`, or have `demote`
  search every `docs/{App}*-Checklist.md` for the id.

**What is NOT affected.** `demote`, `new` and `note` on rows of the current phase; `tf-verify-list.sh
--phase`, the verdict and the emit scripts.

---

## TF-052 — inside `*fix-issues`, the inline verify's `--started <the step-0 time>` swallows the fix's own run record

- **Severity:** minor
- **Blocks:** no. The verify's run record covers the window, the miss-fix records were written, and the
  fix's later segment was recorded separately.
- **Repro:** follow `fix-issues.md` as written. Step 4 executes `verify-phase.md` inline, whose step 6 runs
  `tf-verify-emit.sh {App} --started <the step-0 time>`; inside a fix the only step-0 time is the fix's.
  Step 5 then runs `tf-fix-close.sh {App} --started <the step-0 time>`.
- **Expected:** one verify-phase run record for the verify and one fix-issues run record for the fix.
- **Actual:** the verify-phase record covers the fix's whole window (12:41:41 to 13:01:57), and
  `tf-fix-close.sh` prints `REFUSED — this run … overlaps the verify-phase run`. Neither task file says which
  start time an inline verify passes. Also: `docs/.last-verify.json` keeps only the last scoped verify, so after
  a Phase 3 then a `--phase 1` verify the row form read the Phase 3 rows as absent ("Needs re-verify"), and put
  that verdict on an unrelated open miss of a touched row (MISS-TfLens-20260911-39).
- **Encountered in:** TfLens `*fix-issues`, 2026-09-15.
- **Workaround:** the fix's own misses closed by id (`--misses … --verdict Verified`); its post-verify
  segment recorded as its own run.
- **Suggested fix:** have the inline verify pass its OWN start time and the fix record its pre-verify segment
  first; or let `tf-fix-close.sh` read several ledgers or the gate records.

**What is NOT affected.** A standalone `*verify`; the gate records and misses a chained verify writes;
`tf-fix-close.sh --misses`.
