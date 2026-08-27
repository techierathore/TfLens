# TfLens — TechieFlow framework feedback

Defects found in the **TechieFlow framework itself** (`.tfcore/`) while building TfLens. That directory
is owned and maintained by the TechieFlow team and is gitignored here — `update-framework.sh` overwrites
it — so nothing in it is fixed locally. This file is the hand-off: each entry is reproducible, with the
evidence and a suggested fix.

Same schema as the per-library feedback files (Severity / Repro / Expected / Actual / Encountered in /
Workaround / Suggested fix). One file per upstream owner; this one is TechieFlow.

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

| ID | Severity | Component | Summary |
|----|----------|-----------|---------|
| [TF-001](#tf-001--tf-metricssh-never-de-duplicates-the-sessions-stream-so-sessions-and-token-totals-are-overstated) | **High** | `tf-metrics.sh` | Sessions stream is never de-duplicated, so session counts and every token total derived from them are overstated. Blocks any consumer's parity check. |
| [TF-002](#tf-002--tf-perfsh-cannot-measure-an-authenticated-app-and-does-not-say-so) | Medium | `tf-perf.sh` | No cookie/auth option, so on a login-gated app it times the redirect and reports it as a page-load figure. |
| [TF-004](#tf-004--tf-render-htmls-checklist-guard-matches-any-checklistmd-not-just-the-requirements-checklist) | Low | `tf-render-html` | Refuses any file ending `-Checklist.md`, including a human deployment runbook. The ban is meant for the agent's Requirements checklist only. |
| [TF-003](#tf-003--generate-html-has-no-renderer-html-is-hand-authored-by-the-model-from-a-494-line-spec) | ✅ **Fixed 2026-08-27** | `*generate-html` | No renderer shipped; the agent hand-authored every HTML file from a 494-line spec. `tf-render-html.sh` now ships and the task calls it — verified here on 5 documents / 392 KB. |

---

## TF-001 — `tf-metrics.sh` never de-duplicates the sessions stream, so sessions and token totals are overstated

**Severity:** High — produces wrong numbers silently, and blocks any consumer's parity check.

**Component:** `.tfcore/telemetry/tf-metrics.sh` (`--report`, `--rollup`) · sha256 `326b586e…4412`
**Found:** 2026-08-27, on the first full run of TfLens's BRD §13 parity gate.

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

## TF-002 — `tf-perf.sh` cannot measure an authenticated app, and does not say so

**Severity:** Medium — reports a meaningless number without flagging it.

**Component:** `.tfcore/utils/tf-perf.sh`
**Found:** 2026-08-27, grading REQ-NFR-001's `perf-budget` during `*verify all`.

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

## TF-004 — `tf-render-html`'s checklist guard matches any `*-Checklist.md`, not just the requirements checklist

**Severity:** Low — a false positive with an easy workaround, but it blocks a legitimate document.

**Component:** `.tfcore/utils/tf-render-html.py` line ~464
**Found:** 2026-08-27, rendering a deployment runbook the owner asked to be named `Deployment-Checklist.md`.

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
