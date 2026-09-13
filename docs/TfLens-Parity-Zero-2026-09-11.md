# TfLens — the numbers now match, and what to do next (2026-09-11)

*Corrected later on 2026-09-11. The verify this document asked for has run, and the framework
problems it described as open are fixed. The corrected parts say so. The account of the work is
unchanged.*

## The short version

TfLens and the framework's own reporting script now produce **exactly the same numbers**, for all five
of your projects. Every number, not most of them.

| Project | Before this session | Now |
|---|---|---|
| TechieBlog | 114 disagreements | **0** |
| TfLens | 211 disagreements | **0** |
| TechieRag | 99 disagreements | **0** |
| TechieFlow | 122 disagreements | **0** |
| MyDiary | 114 disagreements | **0** |

Tests: 766 of 766, plus 130 of 130 in the rules suite, plus 107 of the browser tests. The build has no
warnings. All of it ran against your real database.

---

## What to do next

**Done.** This verify ran on 2026-09-11 at 09:24, and the phase 3 checklist carries its grades. The
prompt stays here as the record of what was asked.

```
/TechieFlow:agents:verifier *verify TfLens all
```

That grades the fifteen requirements built this session against the running application and moves them
from "built" to "verified". Until it runs, they are only my word that they work — and my word is not
what this project accepts.

**You do not need to fix the framework first.** See the next section for why.

If you would rather use OpenCode, the same command there is:

```
/verifier *verify TfLens all
```

### The skipped-test problem (TF-022) is fixed

This section warned that a framework problem, TF-022, would count a deliberately skipped test as a
failed one, and offered a prompt to get it fixed. That is no longer needed. The framework fixed it on
2026-09-09. It was re-checked and closed here on 2026-09-11: the verify of 09:24 graded both
requirements that had carried the wrong FAIL, `REQ-UI-034` and `REQ-UI-039`, as Verified.

---

## Does anything in the app break because the framework is unfixed?

**No. Nothing.** Not one screen, not one number, not one feature.

Here is what each framework problem touched, and where it stands now. All four are fixed in the
framework.

| Problem | What it affected | Where it stands |
|---|---|---|
| **TF-022** — a skipped test counted as a failed one | The *grades* in your checklist. A requirement that cannot be shown on this machine was recorded as broken rather than as untested. It never broke the app. | **Fixed** 2026-09-09. Re-checked and closed here 2026-09-11: `REQ-UI-034` and `REQ-UI-039` are now Verified. |
| **TF-023** — a wrong instruction in a framework document | Nothing in TfLens. It was a trap for the next agent that followed that document literally. | **Fixed** and closed here 2026-09-11. The document now names the right place, which is where TfLens's own check already had it. |
| **TF-024** — the document checker uses the wrong template | How much noise the document checker makes: 87 complaints that could not be fixed, burying the real ones. It never broke the app. | **Fixed** in the framework 2026-09-11. Not yet re-checked here; a later step re-checks it with `bash .tfcore/utils/tf-doc-check.sh --app TfLens --strict`. |
| **TF-019** — the build list gets stuck | Which requirements got scheduled for building. It never broke the app. | **Fixed** 2026-09-09. Closed here 2026-09-11: the build list offers every not-yet-started row. |

**Nothing was waiting on the framework,** and nothing is now.

---

## What was actually built, in plain terms

### 1. Money figures

**The problem.** Your framework changed on 10 September. It decided it will only record *what it
measured* — how many tokens a run used, on which model — and will never store a price. Working out
what that costs in money became the report's job.

The reason is good: if a price changes, you re-run the report and every run you ever recorded gets the
new price. No stored number was ever wrong, because no price was ever stored.

TfLens was not doing that job at all. Seventeen money numbers per command were simply missing — that
was 191 of the 211 disagreements on TfLens's own project.

**What it does now.** It reports money three separate ways and never adds them together:

> **Example.** A `build-phase` run used 500,000 output tokens on Claude Opus 5.
>
> - **List price: $12.50.** What those tokens would cost at Anthropic's published rate. This is the
>   only number that lets you compare a run on your Claude subscription with a run on a pay-per-token
>   API, because it asks both the same question.
> - **Money billed: $0.00.** You are on a flat monthly subscription. It billed you nothing extra for
>   this run, and that zero is true.
> - **Plan allowance used: $0.00.** Nothing, because a subscription is not a plan with a per-model
>   meter.
>
> Adding those together would give $12.50 and mean nothing at all. That is why they stay apart.

Two rules that sound small and are not:

- **A model with no published price is left out and counted, never priced at zero.** If TfLens priced
  an unknown model at nothing, a whole phase that ran only that model would read as *free*. Instead the
  page says "3 runs could not be priced".
- **When a run used two models, its price is not split between them.** Splitting a bill by token share
  is arithmetic, not measurement. The run's price counts toward the phase total, but not toward either
  model's line.

### 2. Cancelled runs

**The problem.** The telemetry files are append-only — nothing is ever edited or deleted. So when a run
gets recorded with a wrong figure, the framework cannot go back and fix it. It writes a *second* record
that says "ignore that earlier run, and here is why".

TfLens did not understand those records. It did two wrong things at once: it counted the cancellation
note itself as if it were a run, and it kept the bad run too.

> **Example.** TechieFlow had three cancellation notes. TfLens reported **81 runs**; the framework
> reported **75**. The six-run gap was three notes counted as work, plus three known-bad runs that
> should have left every total.

**What it does now.** The cancelled run leaves every figure. The note is not counted as a run. And the
page publishes what was removed — how many, and the reason given for each — because a total that
quietly got smaller is just a different wrong number. Nothing is deleted; both records stay on file.

### 3. A screen for price lists

New page at **`/prices`**, in the sidebar under Repos. It sits with Repos rather than with the reports
because a price list is something *you maintain*, not something your projects produced.

It lists Anthropic, OpenAI, OpenCode Go and OpenRouter. For each one: every model's price, a link to
the page where you can check it yourself, and **when it was last checked**. You can add a provider, add
or change a price, and remove a provider.

It keeps two things visibly apart, because they are not the same kind of claim:

- **Read from the provider's own feed** — OpenRouter publishes its prices as data, so TfLens fetches
  them. The Refresh button pulled **432 live prices** when I tested it.
- **Typed from the provider's published page** — Anthropic, OpenAI and OpenCode Go do not publish
  prices as data, so someone reads their pricing page and types the numbers in.

If two providers list the same model, the first one wins and the page **tells you about the clash**. It
does not average them: the average of two published prices is a price nobody publishes.

---

## Four bugs found along the way

These were not on anyone's list. The number-matching check found three; the live network test found the
fourth.

### Two cancellation notes could cancel each other out

A run used to be identified by *when it happened, for which app, and which command*. That sounds unique
and is not.

> **Example.** TechieFlow wrote two cancellation notes in the same second, naming two different runs.
> Because both notes shared a timestamp, an app name and a command, TfLens treated them as one record
> and threw the second away. The run that second note was cancelling quietly came back into every
> total — and nothing anywhere would have shown it.

A run is now identified by **its line number in its file**. Because the files are append-only, line 7 is
always the same record. Reading the same file twice still changes nothing, and two genuinely different
records now stay two records.

### The report gave two answers to "how long did this run take"

The new duration rule was being applied in one part of the report but not in another, so the
throughput figure was dividing by a duration that a different part of the same report had already
rejected. The rule now runs once, and everything reads that one result.

### A price rounded the wrong way by one hundredth of a penny

> **Example.** One `triage-issues` run priced at exactly **$15.8516545** — a dead tie at the sixth
> decimal place. The framework rounded it up to $15.851655. TfLens rounded it down to $15.851654.

The cause: .NET's standard rounding shifts the number before it looks at it, which made a value that is
*fractionally above* the tie look exactly on it. One hundred-thousandth of a cent, and it was the
difference between a passing check and a failing one on three of your five projects.

### OpenRouter says "-1" when it will not tell you a price

Found by the test that hits OpenRouter's real endpoint, on its first run.

OpenRouter publishes `-1` as the price of models whose cost is variable or undisclosed. That is the
feed saying *I cannot answer* — not saying the model is free.

Storing it puts a negative price in your price list. Treating it as zero is worse: every run on that
model would then report as costing nothing, and nothing on the page would look wrong. TfLens now skips
those, and the runs that used them are reported as unpriced.

---

## Tests I changed, and why

Four tests were asserting things that are no longer true. None of them was switched off.

| Test | What it assumed | Why that changed |
|---|---|---|
| `DedupeTests`, `PostgresStoreTests` | Two identical run lines collapse into one | They no longer do — that is the cancellation-note fix. Both tests now say so. |
| `fn-miss-sort-stream` | Exactly 6 misses carry a "whose gap was it" answer | It could only be exactly 6 while no *real* miss existed after 7 September. This session logged one. The test now checks the **rule** — that old records are left out of the number it divides by — instead of a fixed count that drifts every time you log a miss. |
| `parity-gate-smoke` | The export banner says the figures are quotable | It said NOT QUOTABLE, correctly: the framework script had changed and no matching check had been recorded. The check now passes, so I recorded the pass. The banner was honest in both states. |

---

## Where the loose ends stand

- **The verify has run** (2026-09-11, 09:24). Its grades are in `docs/TfLens-P3-Checklist.md`.
- **Two browser tests fail** — `REQ-UI-026` and `REQ-UI-029`. Both need a fuller dataset than this
  machine holds. I confirmed they fail identically with the price list exactly as it was before this
  work, so neither is caused by it.
- **The four framework problems this document named are fixed.** TF-019, TF-022 and TF-023 were
  re-checked and closed here on 2026-09-11. TF-024 is fixed in the framework and waits for its re-check
  (the line to run is in the table above).

## Three things I got wrong in the first version of this document

Logged as `MISS-TfLens-20260911-01`, `-02` and `-03`.

1. **It was written in jargon**, after you had already asked twice for plain English with examples.
2. **It said two framework defects were filed and stopped there** — without saying which features
   depend on them (none), what breaks (nothing), or whether you should fix them before or alongside
   the verify.
3. **It said verify was pending but gave you no prompt to run**, which is the one thing a hand-off
   exists to provide.
