# TfLens — what the metrics update asks for, and what I need decided

| | |
|---|---|
| Purpose | Turn `docs/TfLens-Metrics-Update-Prompt.md` into a change I can apply to the BRD and Architecture. Written in plain English, no framework shorthand. |
| Status | **Waiting on two decisions** (§6). Nothing has been changed in any document yet. |
| Written | 2026-09-08 by `*amend-docs` |
| Source | `docs/TfLens-Metrics-Update-Prompt.md`, checked against `.tfcore/telemetry/SCHEMA.md` and against TfLens's own live data |

---

## 1. Words this document uses

The prompt is written for someone who already knows the framework. This section is so you never have
to hold that in your head while reading the rest.

| Term | What it actually means |
|---|---|
| **A stream** | One of the `.jsonl` log files under `docs/metrics/`. Every framework command appends a line. Nothing is ever edited. |
| **A miss** | One recorded mistake — something that was wrong, in a document or in code. One line in `misses.jsonl`. |
| **A gate record** | One pass/fail verdict on one requirement, from one verify run. One line in `gates.jsonl`. |
| **A run record** | One framework command that ran — how long it took, what it cost. One line in `runs.jsonl`. |
| **The oracle** | The framework's own script, `.tfcore/telemetry/tf-metrics.sh`. It computes the same numbers TfLens computes. TfLens's *parity check* runs both and compares them number by number. If they disagree, TfLens is presumed wrong. **"Extends the parity check to `sort`"** simply means: *when TfLens starts showing the new breakdown, the comparison against that script should cover the new numbers too.* Nothing more. |
| **First-pass rate** | Of all requirements, what share passed verification on the very first try. |
| **A denominator** | The bottom half of a fraction — what you are dividing by. Most of the arguments below are about getting this right. |

---

## 2. What the prompt is asking for, in one paragraph

The framework's log files gained some new columns last week. TfLens reads those log files and draws
the dashboard. Right now TfLens ignores the new columns, so the dashboard cannot answer a question it
could now answer: **not just *what broke*, but *whose fault it was and whether a check should have
caught it*.** Separately, the prompt says three of TfLens's existing calculations are wrong, and gives
the arithmetic to fix them.

That is the whole thing. Twelve new requirements and seven edits to existing ones.

---

## 3. What I verified against TfLens's own data

I did not take the prompt's word for anything. I read TfLens's own log files.

| Claim | What I found in TfLens's own data |
|---|---|
| New `sort` column exists but is empty on old records | **True, and total.** All 86 of TfLens's own miss records have it empty. So does the plain-English `what` column. |
| Records are missing the `attempt` number | **True.** 35 of 809 gate records have no attempt number. Those same 35 also have no requirement class. |
| Records are missing a duration | **True, barely.** 1 of 52 run records — and it has a start and end time, so the duration can simply be worked out. |
| A dashboard that only shows known command names would hide records | **True.** One TfLens run is recorded as `rename-page`, which is not on any official list. A filter built from the official list would silently drop it. |
| The Codex tool is retired | **True here.** All 52 TfLens runs are `claude-code`. No Codex records to worry about. |

One extra thing I found that the prompt does not mention: **37 gate records use verdict words that
are not on the official list** — 33 say `PASS`, 2 say `NOT-OBSERVABLE`, 2 say `In Progress`. This is
the same problem as `rename-page`, on a different column. It is covered by the fix in §4, item 12.

---

## 4. The twelve new requirements, in plain English

New ID numbers carry on from the highest one currently in the BRD, which is **BRD-169**.

### Showing whose fault a mistake was (items 1–3)

**1. BRD-170 — Read and store the new "whose gap" column.**
Every mistake now records one of four answers: *the project's own spec didn't say it* · *the framework
never said it anywhere* · *there was a check and it didn't catch it* · *it was written down and ignored*.
TfLens should store that answer. Anything that isn't one of those four is flagged, not quietly filed
under one of them.

**2. BRD-171 — Show it as a chart, next to the existing one.**
The dashboard already shows *what kind* of mistake it was. This adds *whose gap it was* beside it,
with a filter. Written out in words on screen, not as the raw code word — "the check was too weak to
catch it", not `weak-check`.

**3. BRD-172 — Be honest about how many records actually have the answer.**
The column only started existing on 7 September. Older records don't have it. So the chart must say
**"33 of 65 answered"** and never turn that into a percentage of all mistakes — an old record isn't
an "unanswered" one, it's a record from before the question was asked. Those two must not be added
together.

### Showing what actually happened, in words (item 4)

**4. BRD-173 — Put the one-sentence description on the row.**
Every mistake now carries one sentence in your own words. Show it. A mistake identified only by a
category and a date is unrecognisable a month later. The rule that comes with it: that sentence is
the *only* prose allowed to travel — requirement text from a checklist is never copied into the
dashboard.

### Pricing what a review costs (items 5–6)

**5. BRD-174 — Accept a new type of record.**
There is now a fourth kind of record in the mistakes log: a *review* — you read a phase's output and
gave corrections. **This one matters more than the others**, because as things stand TfLens would
throw these records away as unrecognised junk. Something that counts mistakes must not count these
as mistakes; they're a different thing.

**6. BRD-175 — Show what reviews cost.**
Per phase: how many corrections you gave, what it cost to produce the thing you had to correct, and
what the corrections cost. Both figures are **copied from the runs the record points at, never
calculated by TfLens**. If a figure isn't there, say "not available" — never show a zero.

### Three small correctness items (items 7–9)

**7. BRD-176 — Say how many blanks were filled in later.**
Records can have a blank filled in afterwards. When a chart includes those, it should say how many.

**8. BRD-177 — Keep the framework's own scorecard separate.**
The framework now grades *itself* against its own rules, and those verdicts land in the same log file
as your application's. They must never be averaged together. A framework rule and an application
screen are not the same kind of thing.

**9. BRD-178 — Identify a requirement by project *and* number, never by number alone.**
Every project has a `REQ-UI-001`. If TfLens combines projects and keys on the number alone, TfLens's
`REQ-UI-001` and TechieBlog's become one requirement. **The prompt says the framework's own combined
score read 72% under this bug and 48% once fixed.** This is the single most valuable item on the list.

### Two arithmetic fixes (items 10–11)

**10. BRD-179 — Work out a run's duration when it isn't recorded.**
Some records have a start and end time but no duration. Adding up durations counts those as **zero
time while still counting their cost** — so a phase's time covers some runs and its cost covers
others. Subtract start from end. Say how many were worked out this way.

**11. BRD-180 — Work out the attempt number when it isn't recorded.**
The first-pass score counts records marked "attempt 1". A record with no attempt number drops out of
the score entirely. **The framework's own 12 requirement verdicts all passed and reported a first-pass
rate of 0%.** Count them in order to fill the number in. Never touch a record that already has one.

### One rule about filters (item 12)

**12. BRD-181 — Never drop a record just because a value is unfamiliar.**
If a command name, verdict or tool name isn't on the expected list, show it as it is and count it —
don't hide it. TfLens's own data already proves this matters: one `rename-page` run and 37
off-list verdicts would vanish from a strict filter.

---

## 5. The seven edits to existing requirements

Nothing is renumbered and nothing is deleted. These are edits in place.

| ID | What changes | Why |
|---|---|---|
| **BRD-113** | Recognise the new *review* record type | **Without this, review records get thrown away as junk** |
| **BRD-114** | How to spot a duplicate review record | Re-reading a file must not double-count |
| **BRD-115** | Three database tables become four | The reviews need somewhere to live |
| **BRD-116** | Name which columns can be filled in later: add the two new ones | They're currently not on the list |
| **BRD-117** | Record that the "whose gap" column starts on 2026-09-07 | This is what makes item 3 work automatically |
| **BRD-127** | Health page gains two counts: how many records lack the new columns | For TfLens's own repo both are currently 86 — worth seeing |
| **BRD-51** | Mark Codex as retired | No new Codex records; old ones still display; not offered in new filters |

**Also:** the comparison against the framework's own script grows to cover the new numbers, and the
prompt's eight "how to prove it's done" tests become the acceptance criteria. Around 12 new checklist
rows and 7 rows going back to "needs re-verify". The mistakes page and the health page change
appearance, so their mockups need refreshing.

**This is not a mistake on anyone's part.** The framework shipped these columns after TfLens's last
documentation update. That is new scope arriving, not something the documents should have said
earlier — so no mistake gets logged for it.

---

## 6. The two decisions I need from you

### Decision 1 — the rate card *(this is the real one)*

A **rate card** is the price list in `data/prices.json`. TfLens uses it to answer "what would this
have cost if every run had used the most expensive model" — the repricing panel on the routing page,
plus the small editor dialog for the price list.

**The prompt's §6 says: "No dollar figure is ever estimated. No rate card, ever."**

That contradicts working, verified TfLens features — BRD-62, BRD-123, BRD-128, BRD-160 — which allow
an estimated dollar figure *provided* it is labelled an estimate, sits on its own card, and is never
added to a real measured cost.

The prompt names `SCHEMA.md` as the authority and says the schema wins over the prompt. **I checked
the schema. It does not ban a rate card.** It bans presenting an estimate *as if it were a
measurement* — which TfLens already doesn't do. So the prompt is stricter than its own stated
authority.

| Option | What happens |
|---|---|
| **A — Keep TfLens as it is** *(my recommendation)* | No document changes. The existing safeguards already satisfy the schema. The prompt's §8 invites disagreement; this is one. |
| **B — Follow the prompt literally** | BRD-62 and BRD-123 struck out, the repricing panel and the price-list editor removed from the routing page, about 4 checklist rows marked not-applicable. |

**I recommend A** — the schema is the stated authority and it permits what TfLens does.

### Decision 2 — go ahead?

"Go" and I apply §4 and §5 to the BRD and Architecture, refresh the two mockups, update the checklist
and re-render the HTML. Or tell me what to change first.

---

## 7. The other thing you asked about — `TfLens-Misses.md` open items

**Short answer: no document needs changing. Nothing here is broken.**

There are 37 open items. Only 16 of them are about a *document* (as opposed to code or config), so
only those 16 could possibly mean the BRD or Architecture is wrong. I looked up every one that names
a requirement:

| The open item | Its requirement | Status in the checklist today |
|---|---|---|
| 7 items from 2026-09-01 | FN-090, 092, 093, 094, 102, 103, 105 | **All Verified** |
| 2026-08-30-03 | NFR-020 | **Implemented** — BRD-144 was written on 2026-08-29 |
| 2026-08-29-01 | NFR-019 | **Implemented** — BRD-143 was written on 2026-08-29 |
| 2026-08-30-01 | UI-027 | **Verified** |
| 2026-08-29-21 | UI-011 | **Verified** |

**Every single one was already fixed** — by your amendments on 29 August and 1 September. The
documents are right. What's missing is only the closing record in the log saying so. They show as
"open" because nobody wrote the closing line, not because there's outstanding work.

The remaining 4 name no requirement and have no description sentence, so there is genuinely nothing
to act on — I am not going to invent a documentation change out of a category label.

### Two housekeeping jobs, whenever you want them

Neither is part of amending documents, so I have not done either.

1. **Close the 12 stale items** — one closing record each, saying the fix landed in the amendment
   that fixed it.
2. **Fill in "whose gap" on the 86 records** — the dashboard work in §4 has nothing to display until
   at least some of them are answered. Worth doing the recent ones rather than all 86.

Say the word and I'll prepare the exact commands for both.

---

## 8. One small note about the framework

I raised this badly in conversation and want it recorded accurately instead.

`fix-issues` and `build-phase` *do* close mistakes automatically — they close them off checklist
rows. The 12 stale items above are about **documents**, and the document fix happened in
`amend-docs`, which has no closing step. So they stayed open.

**This is a minor ergonomic gap, not a defect**, and it does not undo any of the framework work of the
past three days — the closing machinery exists and works, it just isn't wired to this one command. It
is written up as `TF-016` (Low) in `docs/TfLens-TechieFlow-Feedback.md`. Nothing is blocked by it.
