# TfLens — decisions I need from you

| | |
|---|---|
| App | TfLens |
| Written | 2026-09-09, added to 2026-09-10 and 2026-09-11, corrected 2026-09-11 |
| Waiting on | Nothing. Decisions 1 and 3 no longer need you. Your amendment that follows this correction answers 2, 4 and 5. |

## What happened

While building the last phase of TfLens I ran into two piles of work that were not
mine to do from here.

The first was the build tooling: five problems in files the framework owns, two of
which changed what your reports said. The framework team fixed all five on
2026-09-09, so that pile is gone and decision 1 no longer needs you.

The second is the project's own written documents. When this was written, the checker
refused 236 things across nineteen files, mostly sections that no longer match their
template since the documents were split into three phases. One of them is a real
contradiction: a database sketch in the architecture document disagrees with every
table around it and with the code (decision 2).

Three more questions came up later: whether phase 3 had grown too large (decision 3,
which turned out not to need you), how the menu should look on a phone (decision 4),
and how the list-price figures are named (decision 5).

Neither pile blocks the application. It builds and runs, and 69 of phase 3's 86 live
requirements are verified.

## What I need you to decide

### 1. Fixing the five problems in the build tooling

**Correction, 2026-09-11: this decision no longer needs you.** The framework team fixed
all five on 2026-09-09. Two have been checked again here and closed: the printed build
list now offers every requirement not yet started, and the two rows that wrongly said
FAIL are now Verified. The other three are fixed in the framework and wait for their own
check here; nothing depends on them. The question below is kept as the record of what
was asked.

The question was how much of the framework repair to hand over. Two of the five changed
what your reports said: one hid work that had never been started, and one recorded a
deliberately skipped test as a failure.

| Option | What happens | What it costs |
|---|---|---|
| **A — fix all five now** | The tooling stops hiding unstarted work, stops calling absent data a defect, and stops reporting a correctly-built screen as broken. | One session in the framework window. |
| **B — fix only the two that change reports** | The two that matter are gone; the three cosmetic ones stay and keep producing noise every run. | Less work now, a second pass later. |
| **C — leave all five** | Every future pass repeats them. The hidden-work one is the dangerous one: it will quietly skip new requirements again. | Nothing now, and a real risk of shipping a phase that was never finished. |

**My recommendation: A** — they are already diagnosed and written up, so the
expensive part is done; fixing three more costs very little on top.

### 2. Repairing the project's documents

**Answered by your amendment that follows this correction.** Nothing more is needed here.

The 236 findings are not evenly serious. One is a genuine contradiction: the
architecture document sketches a new database table with a different key and
different column types from every other table beside it, and the code follows the
surrounding house style rather than the sketch. That one should be settled before
anyone reads the document and believes it. The rest are shape and template drift.

| Option | What happens | What it costs |
|---|---|---|
| **A — repair everything in one pass** | All nineteen documents match their templates again. | The longest option, and most of it is cosmetic. |
| **B — settle the contradiction first, then the rest later** | The architecture stops disagreeing with the code immediately; the cosmetic backlog waits. | Two sessions instead of one. |
| **C — leave it** | The documents keep drifting and the checker's output stays too noisy to read. | Nothing now, more later. |

**My recommendation: B** — the contradiction is the only part that can mislead
someone; the rest can wait for a quiet moment.

### 3. Phase 3's work list has gone past its size limit

Today's amendment added four requirements — the four counts that must be published
beside a duration total. The step that adds rows also fills in anything missing, and
it found thirty rows that an earlier amendment had written into the business document
and never tracked. Phase 3's list went from 73 rows to 107; the guidance is 100. It
was already over in substance — the rows simply had not been written down.

| Option | What happens | What it costs |
|---|---|---|
| **A — leave it, split later** | Phase 3 stays at 107 and the duration work finishes uninterrupted. | Nothing now; the decision returns when the phase is closer to done. |
| **B — split now** | A phase 4 takes the thirty newly tracked rows, bringing phase 3 back under 100. | Document surgery mid-correction, and phase 4 would have no theme — the thirty rows came from different amendments. |

**My recommendation: A** — the limit exists to stop a phase becoming unreviewable, and
today made phase 3 *more* reviewable: thirty invisible requirements are now listed.
Nothing here blocks the build.

**Correction, 2026-09-11 (verifier): this decision no longer needs you.** The thirty rows
were not new work. Each one repeats a requirement that an older row already tracks and
that was already built and tested — for example REQ-FN-117 and REQ-FN-068 both carry
BRD-76. The step that adds rows did not recognise the older rows' titles (filed upstream as
TF-025), and it also added REQ-FN-135 as a copy of REQ-FN-112. All 31 copies are now marked N/A, each
naming the row that covers it, and no id was reused. Phase 3 has 86 live rows, under the
limit of 100, so neither option is needed.

### 4. On a phone, the menu slides out; the mockups draw a narrow icon strip

**Answered by your amendment that follows this correction.** Nothing more is needed here.

Added 2026-09-11 by the verifier. Twelve requirements on the Misses and Phase effort
pages pass every one of their own tests, and the pages look right at both widths. They
are held at `Needs re-verify` for one reason: when either page is opened on a phone-sized
screen, the app hides the sidebar behind the menu button at the top left and slides it
out when tapped, while the approved mockups keep a narrow strip of menu icons down the
left edge. The two are different designs, and the check compares the app with the mockup.
The same behaviour applies to every page, because they share one frame.

| Option | What happens | What it costs |
|---|---|---|
| **A — keep the slide-out menu** | The mockups are updated to draw the slide-out menu at phone width; the twelve rows are graded again and should pass. | A short document pass; no code change. |
| **B — build the icon strip** | The app is changed to match the mockups at phone width; the twelve rows are graded again after the build. | A change to the shared page frame, which every page uses. |

**My recommendation: A** — the slide-out menu is what the component library draws by
design, it leaves the whole narrow screen to the figures, and nothing in the requirements
asks for the icon strip.

### 5. Two business-document lines disagree about how list-price keys are named

**Answered by your amendment that follows this correction.** Nothing more is needed here.

Added 2026-09-11 by the verifier. BRD-128 says every figure worked out from the rate card
must have a name ending in `_usd_estimate`, so a reader can never mistake an estimate for
money paid. BRD-195 and BRD-197, added later, name the new list-price figures `list_usd`
and `cost_list_usd_per_miss`, without that ending. The app follows the later lines, and
so does the framework's reference script, which the parity check requires the export to
match key for key. No test fails today, because each test follows the line written for
its own requirement.

| Option | What happens | What it costs |
|---|---|---|
| **A — the list price is its own kind of figure** | BRD-128 gains one sentence: a list price is labelled as a list price, not as an estimate. Nothing else changes. | A one-line document amendment. |
| **B — the ending applies to list prices too** | The keys are renamed in the app, and the reference script has to be changed to match, or the parity check fails. | A code change plus an upstream request. |

**My recommendation: A** — the list price is published on purpose as the one money-shaped
figure every harness has, the Price providers page already says it is reported as a list
price and never as spend, and renaming it would break the parity check that reached zero
findings this week.

## What I do when you answer

- Decisions 1 and 3: nothing. Neither needs you any more.
- Decisions 2, 4 and 5: your amendment answers them, and I fold it into the documents
  with `*amend-docs` — the architecture contradiction first, then the mockups or the
  business-document lines the amendment names.
- Decision 4: once the mockups and the app agree, the twelve Misses and Phase effort
  rows are graded again.
- When all three are folded in, this file moves unchanged to `docs/OldDocs/`, and the
  live name is free for the next question.

## Copy this back to me

Decisions 1 and 3 need no reply, so their blocks are gone. The blocks for 2, 4 and 5
stay as the record of the choices; your amendment is the answer.

```
TfLens: decision 2 — go with option B. Run *amend-docs and settle the
architecture's MissReview table sketch first: it gives the table a bigserial
primary key and types UserId as text and Ts as timestamptz, while all four
sibling tables use no surrogate key, an integer UserId and a text Ts. The
code follows the siblings. Fix the document, then leave the rest of the
template drift for a later pass.
```

```
TfLens: decision 4 — go with option A. Run *amend-docs and update the
mockups so the sidebar is a slide-out menu at phone width, then re-run
*verify TfLens REQ-UI-035,REQ-UI-036,REQ-UI-037,REQ-UI-038,REQ-UI-045,
REQ-UI-046,REQ-UI-047,REQ-UI-048,REQ-UI-049,REQ-UI-052,REQ-UI-053,REQ-UI-054.
```

```
TfLens: decision 4 — go with option B. Build the narrow icon strip in the
shared page frame at phone width, then re-run the verifier on the twelve
Misses and Phase effort rows.
```

```
TfLens: decision 5 — go with option A. Run *amend-docs and add one sentence
to BRD-128: a list price is labelled as a list price, not as an estimate, so
list_usd and cost_list_usd_per_miss keep their names.
```

```
TfLens: decision 5 — go with option B. Rename list_usd and
cost_list_usd_per_miss to end in _usd_estimate, and file an upstream
request for the reference script to rename them too.
```
